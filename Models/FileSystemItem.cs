using CoderCommander.FileSystem;
using System.ComponentModel;
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

    /// <summary>Size in bytes (0 for directories).</summary>
    public long Size => Entry.Size;

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
    public string SizeDisplay { get; }

    /// <summary>Last write time formatted as "yyyy-MM-dd HH:mm".</summary>
    public string ModifiedDisplay { get; }

    /// <summary>Attribute flags as a short string (e.g. "RHA").</summary>
    public string AttributesDisplay { get; }

    /// <summary>Name without extension (e.g. "report" from "report.txt").</summary>
    public string NameWithoutExtension { get; }

    /// <summary>Extension without leading dot for Type column (e.g. "txt"). Empty for directories.</summary>
    public string TypeDisplay { get; }

    /// <summary>Custom display name (for Flat View — relative path).</summary>
    public string? DisplayName { get; init; }

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
        SizeDisplay = isParent ? "" : (entry.IsDirectory ? "<DIR>" : FormatSize(entry.Size));
        ModifiedDisplay = isParent ? "" : entry.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
        AttributesDisplay = isParent ? "" : FormatAttributes(entry.Attributes);
        if (isParent)
        {
            NameWithoutExtension = "";
            TypeDisplay = "";
        }
        else
        {
            var ext = entry.Extension;
            NameWithoutExtension = string.IsNullOrEmpty(ext)
                ? entry.Name
                : entry.Name[..^ext.Length];
            TypeDisplay = ext.Length > 0 ? ext[1..] : "";
        }
    }

    /// <summary>Creates the ".." parent navigation entry.</summary>
    public static FileSystemItem CreateParent(string currentDir)
    {
        string parent;
        if (FileSystem.ZipArchiveFileSystem.IsArchivePath(currentDir))
        {
            var (archivePath, innerPath) = FileSystem.ZipArchiveFileSystem.SplitPath(currentDir);
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
                parent = FileSystem.ZipArchiveFileSystem.MakePath(archivePath, parentInner);
            }
        }
        else
        {
            parent = Path.GetFullPath(Path.Combine(currentDir, ".."));
        }
        var entry = new FileEntry(parent, true);
        return new FileSystemItem(entry, isParent: true) { DisplayName = ".." };
    }

    /// <summary>Formats a byte count into a human-readable string (e.g. "1.5 KB").</summary>
    private static string FormatSize(long bytes)
    {
        if (bytes < 0) return "--";
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{s:0.##} {u[i]}";
    }

    /// <summary>Formats file attributes into a short string (e.g. "RHA").</summary>
    private static string FormatAttributes(FileAttributes attr)
    {
        var sb = new System.Text.StringBuilder(5);
        if ((attr & FileAttributes.ReadOnly) != 0) sb.Append('R');
        if ((attr & FileAttributes.Hidden) != 0) sb.Append('H');
        if ((attr & FileAttributes.System) != 0) sb.Append('S');
        if ((attr & FileAttributes.Archive) != 0) sb.Append('A');
        return sb.ToString();
    }

    /// <summary>Occurs when a property value changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
