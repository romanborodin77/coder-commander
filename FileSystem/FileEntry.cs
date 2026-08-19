namespace CoderCommander.FileSystem;

#pragma warning disable CA1308 // Lowercase extension is an API contract: callers rely on dot-prefixed lowercase
/// <summary>
//// Immutable metadata record — no UI dependencies.
/// </summary>
public sealed class FileEntry
{
    public string FullPath { get; }
    public string Name { get; }
    public bool IsDirectory { get; }
    public bool Exists { get; }
    public long Size { get; }
    public FileAttributes Attributes { get; }
    public DateTime CreatedTimeUtc { get; }
    public DateTime LastWriteTimeUtc { get; }
    public DateTime LastAccessTimeUtc { get; }

    public DateTime LastWriteTime => LastWriteTimeUtc.ToLocalTime();
    public DateTime CreatedTime => CreatedTimeUtc.ToLocalTime();

    /// <summary>
    /// Extension in lowercase (empty for directories). A leading dot with no other dot in the
    /// name — e.g. ".gitignore", ".bashrc" — is a dotfile convention, not an extension
    /// separator, so those report no extension instead of treating the whole name as one
    /// (Path.GetExtension(".gitignore") returns ".gitignore", which is wrong for our purposes).
    /// </summary>
    public string Extension => IsDirectory ? "" : GetExtension(Name);

    /// <summary>
    /// Extension of a file name or path, lowercase and dot-inclusive (e.g. ".txt"), applying the
    /// same dotfile rule as <see cref="Extension"/>: a name that only has a dot at position 0
    /// (".gitignore", ".bashrc") reports no extension. Safe to pass either a bare name or a full
    /// path — the directory portion, if any, is stripped first.
    /// </summary>
    public static string GetExtension(string pathOrName)
    {
        var fileName = Path.GetFileName(pathOrName);
        var lastDot = fileName.LastIndexOf('.');
        return lastDot > 0 ? fileName[lastDot..].ToLowerInvariant() : "";
    }

    public bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;
    public bool IsSystem => (Attributes & FileAttributes.System) != 0;
    public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) != 0;

    public FileEntry(
        string fullPath,
        bool isDirectory,
        bool exists = true,
        long size = 0,
        FileAttributes attributes = default,
        DateTime createdTimeUtc = default,
        DateTime lastWriteTimeUtc = default,
        DateTime lastAccessTimeUtc = default)
    {
        FullPath = fullPath;
        string name;
        if (ArchivePath.IsArchivePath(fullPath))
        {
            var (_, innerPath) = ArchivePath.SplitPath(fullPath);
            innerPath = innerPath.Replace('\\', '/').Trim('/');
            var lastSlash = innerPath.LastIndexOf('/');
            name = lastSlash >= 0 ? innerPath[(lastSlash + 1)..] : innerPath;
        }
        else
        {
            name = isDirectory
                ? Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : Path.GetFileName(fullPath);
        }
        Name = string.IsNullOrEmpty(name) ? fullPath : name;
        IsDirectory = isDirectory;
        Exists = exists;
        Size = isDirectory ? 0 : size;
        Attributes = attributes;
        CreatedTimeUtc = createdTimeUtc;
        LastWriteTimeUtc = lastWriteTimeUtc;
        LastAccessTimeUtc = lastAccessTimeUtc;
    }

    public static FileEntry FromFileSystemInfo(string path, FileSystemInfo fsi)
    {
        return new FileEntry(
            fullPath: path,
            isDirectory: (fsi.Attributes & FileAttributes.Directory) != 0,
            exists: fsi.Exists,
            size: fsi is FileInfo fi ? fi.Length : 0,
            attributes: fsi.Attributes,
            createdTimeUtc: fsi.CreationTimeUtc,
            lastWriteTimeUtc: fsi.LastWriteTimeUtc,
            lastAccessTimeUtc: fsi.LastAccessTimeUtc);
    }

    public override string ToString() => Name;
}
