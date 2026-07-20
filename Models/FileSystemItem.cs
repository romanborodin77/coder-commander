using CoderCommander.FileSystem;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CoderCommander.Models;

/// <summary>
//// Wraps a FileEntry with selection state and display-formatted strings.
/// </summary>
public sealed class FileSystemItem : INotifyPropertyChanged
{
    public FileEntry Entry { get; }

    public string FullPath => Entry.FullPath;
    public string Name => Entry.Name;
    public bool IsDirectory => Entry.IsDirectory;
    public bool IsParent { get; }
    public long Size => Entry.Size;
    public DateTime Modified => Entry.LastWriteTime;
    public DateTime Created => Entry.CreatedTime;
    public FileAttributes Attributes => Entry.Attributes;
    public string Extension => Entry.Extension;
    public bool IsHidden => Entry.IsHidden;
    public bool IsSystem => Entry.IsSystem;
    public bool IsReadOnly => Entry.IsReadOnly;

    public string SizeDisplay { get; }
    public string ModifiedDisplay { get; }
    public string AttributesDisplay { get; }

    /// <summary>Name without extension (e.g. "report" from "report.txt").</summary>
    public string NameWithoutExtension { get; }

    /// <summary>Extension without leading dot for Type column (e.g. "txt"). Empty for directories.</summary>
    public string TypeDisplay { get; }

    /// <summary>Custom display name (for Flat View — relative path).</summary>
    public string? DisplayName { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

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

    private static string FormatSize(long bytes)
    {
        if (bytes < 0) return "--";
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{s:0.##} {u[i]}";
    }

    private static string FormatAttributes(FileAttributes attr)
    {
        var sb = new System.Text.StringBuilder(5);
        if ((attr & FileAttributes.ReadOnly) != 0) sb.Append('R');
        if ((attr & FileAttributes.Hidden) != 0) sb.Append('H');
        if ((attr & FileAttributes.System) != 0) sb.Append('S');
        if ((attr & FileAttributes.Archive) != 0) sb.Append('A');
        return sb.ToString();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
