using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.Utils;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CoderCommander.Models;

/// <summary>
/// Wraps a FileEntry with selection state and display-formatted strings.
/// </summary>
public sealed class FileSystemItem : INotifyPropertyChanged
{
    /// <summary>Underlying file entry data.</summary>
    public FileEntry Entry { get; }

    /// <summary>Full path to the file or directory.</summary>
    public string FullPath => Entry.FullPath;

    /// <summary>File or directory name including extension.</summary>
    public string Name => Entry.Name;

    /// <summary>True when the entry is a directory.</summary>
    public bool IsDirectory => Entry.IsDirectory;

    /// <summary>True when this item represents the ".." parent directory entry.</summary>
    public bool IsParent { get; }

    /// <summary>Size in bytes (0 for directories, unless <see cref="CalculatedSize"/> has been set).</summary>
    public long Size => CalculatedSize ?? Entry.Size;

    private long? _calculatedSize;

    /// <summary>
    /// Set once a background "calculate folder size" scan (see <c>MainViewModel.CalculateFolderSize</c>)
    /// finishes for this directory item - overrides the default 0-byte directory size in both
    /// <see cref="Size"/> and <see cref="SizeDisplay"/> until this item is replaced by a fresh listing.
    /// </summary>
    public long? CalculatedSize
    {
        get => _calculatedSize;
        set
        {
            if (_calculatedSize == value) return;
            _calculatedSize = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Size));
            OnPropertyChanged(nameof(SizeDisplay));
        }
    }

    private bool _isCalculatingSize;

    /// <summary>True while a background size calculation for this directory is in progress.</summary>
    public bool IsCalculatingSize
    {
        get => _isCalculatingSize;
        set
        {
            if (_isCalculatingSize == value) return;
            _isCalculatingSize = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SizeDisplay));
        }
    }

    /// <summary>Last write time in local time.</summary>
    public DateTime Modified => Entry.LastWriteTime;

    /// <summary>Creation time in local time.</summary>
    public DateTime Created => Entry.CreatedTime;

    /// <summary>File system attributes (ReadOnly, Hidden, etc.).</summary>
    public FileAttributes Attributes => Entry.Attributes;

    /// <summary>File extension including the leading dot (e.g. ".txt"), empty for directories.</summary>
    public string Extension => Entry.Extension;

    /// <summary>True when the Hidden attribute is set.</summary>
    public bool IsHidden => Entry.IsHidden;

    /// <summary>True when the System attribute is set.</summary>
    public bool IsSystem => Entry.IsSystem;

    /// <summary>True when the ReadOnly attribute is set.</summary>
    public bool IsReadOnly => Entry.IsReadOnly;

    /// <summary>Human-readable size string (e.g. "1.5 KB", "&lt;DIR&gt;").</summary>
    public string SizeDisplay
    {
        get
        {
            if (IsParent) return "";
            if (!IsDirectory) return FormatUtils.FormatSize(Entry.Size);
            if (IsCalculatingSize) return "…";
            return CalculatedSize is { } sz ? FormatUtils.FormatSize(sz) : LocalizationService.Current.GetString("Panel.Dir");
        }
    }

    private string? _modifiedDisplay;

    /// <summary>Last write time formatted as "yyyy-MM-dd HH:mm". Computed lazily (audit finding
    /// G048) - with the panel's ListView in VirtualMode, only visibly-rendered rows ever read this,
    /// so eagerly formatting it for every item in a large listing (most of which never scroll into
    /// view) was pure waste.</summary>
    public string ModifiedDisplay => _modifiedDisplay ??=
        IsParent ? "" : Entry.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private string? _attributesDisplay;

    /// <summary>Attribute flags as a short string (e.g. "RHA"). Computed lazily, same reasoning as
    /// <see cref="ModifiedDisplay"/>, via a precomputed 16-entry lookup table (one entry per
    /// combination of the 4 tracked bits) instead of a per-item <see cref="System.Text.StringBuilder"/>.</summary>
    public string AttributesDisplay => _attributesDisplay ??=
        IsParent ? "" : AttributeDisplayTable[AttributeTableIndex(Entry.Attributes)];

    private string? _nameWithoutExtension;

    /// <summary>Name without extension (e.g. "report" from "report.txt"). Computed lazily, same
    /// reasoning as <see cref="ModifiedDisplay"/>.</summary>
    public string NameWithoutExtension => _nameWithoutExtension ??= ComputeNameWithoutExtension();

    private string ComputeNameWithoutExtension()
    {
        if (IsParent) return "";
        var ext = Entry.Extension;
        return string.IsNullOrEmpty(ext) ? Entry.Name : Entry.Name[..^ext.Length];
    }

    private string? _typeDisplay;

    /// <summary>Extension without leading dot for Type column (e.g. "txt"). Empty for directories.
    /// Computed lazily, same reasoning as <see cref="ModifiedDisplay"/>.</summary>
    public string TypeDisplay => _typeDisplay ??=
        IsParent ? "" : (Entry.Extension.Length > 0 ? Entry.Extension[1..] : "");

    /// <summary>Custom display name (for Flat View — relative path).</summary>
    public string? DisplayName { get; init; }

    private GitFileStatus _gitStatus = GitFileStatus.None;

    /// <summary>
    /// Git working-tree status, set by <c>MainViewModel</c>'s background git-status refresh after
    /// confirming the containing directory is inside a git repository. Stays <see cref="GitFileStatus.None"/>
    /// otherwise (including for every item until that refresh completes).
    /// </summary>
    public GitFileStatus GitStatus
    {
        get => _gitStatus;
        set
        {
            if (_gitStatus == value) return;
            _gitStatus = value;
            OnPropertyChanged();
        }
    }

    private bool _isSelected;

    /// <summary>Whether the item is currently selected in the UI.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    /// <summary>Creates a new item wrapping the given entry.</summary>
    public FileSystemItem(FileEntry entry, bool isParent = false)
    {
        Entry = entry;
        IsParent = isParent;
    }

    /// <summary>Creates the ".." parent navigation entry.</summary>
    public static FileSystemItem CreateParent(string currentDir)
    {
        string parent;
        if (ArchivePath.IsArchivePath(currentDir))
        {
            var (archivePath, innerPath) = ArchivePath.SplitPath(currentDir);
            innerPath = innerPath.Replace('\\', '/').Trim('/');
            var lastSlash = innerPath.LastIndexOf('/');
            var parentInner = lastSlash > 0 ? innerPath[..lastSlash] : "";
            if (string.IsNullOrEmpty(parentInner))
            {
                // At archive root — exit to parent directory of the archive file
                parent = Path.GetDirectoryName(archivePath) ?? Path.GetPathRoot(archivePath) ?? archivePath;
            }
            else
            {
                parent = ArchivePath.MakePath(archivePath, parentInner);
            }
        }
        else if (RemotePath.IsRemote(currentDir))
        {
            // Path.GetFullPath on "smb://host/share/dir\.." resolves against the process's
            // current directory and produces a garbage local path. Remote paths need their
            // own parent arithmetic — same logic as PanelViewModel.GoToParentAsync.
            var remoteParent = VfsPath.GetParent(currentDir);
            if (string.IsNullOrEmpty(remoteParent))
            {
                // At the connection's root — parent is the user's home directory (exits the connection)
                parent = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            else
            {
                parent = remoteParent;
            }
        }
        else
        {
            parent = Path.GetFullPath(Path.Combine(currentDir, ".."));
        }
        var entry = new FileEntry(parent, true);
        return new FileSystemItem(entry, isParent: true) { DisplayName = ".." };
    }

    /// <summary>All 16 combinations of the 4 tracked attribute bits (ReadOnly/Hidden/System/Archive),
    /// indexed by <see cref="AttributeTableIndex"/> - built once per process instead of running a
    /// <see cref="System.Text.StringBuilder"/> for every item's <see cref="AttributesDisplay"/>.</summary>
    private static readonly string[] AttributeDisplayTable = BuildAttributeDisplayTable();

    private static string[] BuildAttributeDisplayTable()
    {
        var table = new string[16];
        for (var i = 0; i < 16; i++)
        {
            var sb = new System.Text.StringBuilder(4);
            if ((i & 1) != 0) sb.Append('R');
            if ((i & 2) != 0) sb.Append('H');
            if ((i & 4) != 0) sb.Append('S');
            if ((i & 8) != 0) sb.Append('A');
            table[i] = sb.ToString();
        }
        return table;
    }

    /// <summary>Packs the 4 tracked <see cref="FileAttributes"/> bits (which aren't contiguous in
    /// the real enum - Archive is bit 5, not bit 3) into a dense 0-15 index for <see cref="AttributeDisplayTable"/>.</summary>
    private static int AttributeTableIndex(FileAttributes attr)
    {
        var idx = 0;
        if ((attr & FileAttributes.ReadOnly) != 0) idx |= 1;
        if ((attr & FileAttributes.Hidden) != 0) idx |= 2;
        if ((attr & FileAttributes.System) != 0) idx |= 4;
        if ((attr & FileAttributes.Archive) != 0) idx |= 8;
        return idx;
    }

    /// <summary>Occurs when a property value changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
