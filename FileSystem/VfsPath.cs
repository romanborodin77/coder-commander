namespace CoderCommander.FileSystem;

/// <summary>
/// Path arithmetic that understands both plain Windows paths and virtual archive paths
/// of the form <c>C:\dir\file.zip|inner/dir/name</c>.
/// <para>
/// <see cref="System.IO.Path"/> mangles the archive form (the <c>|</c> character is illegal
/// in Windows paths and <c>Path.Combine</c> would insert backslashes inside the archive part),
/// so every operation that may touch an archive routes through these helpers instead.
/// </para>
/// </summary>
public static class VfsPath
{
    /// <summary>True when the path points inside an archive.</summary>
    public static bool IsArchive(string path) =>
        !string.IsNullOrEmpty(path) && path.IndexOf(ArchivePath.Separator) >= 0;

    /// <summary>Host file of an archive path; the path itself when it is a plain path.</summary>
    public static string GetArchiveFile(string path) =>
        IsArchive(path) ? ArchivePath.SplitPath(path).archivePath : path;

    /// <summary>Inner, slash-separated part of an archive path without leading/trailing slashes.</summary>
    public static string GetInner(string path) =>
        IsArchive(path) ? NormalizeInner(ArchivePath.SplitPath(path).innerPath) : "";

    /// <summary>Collapses separators of an inner archive path to a bare <c>a/b/c</c> form.</summary>
    public static string NormalizeInner(string inner) =>
        string.IsNullOrEmpty(inner) ? "" : inner.Replace('\\', '/').Trim('/');

    /// <summary>Appends a relative path to a directory, keeping the flavour of <paramref name="basePath"/>.</summary>
    public static string Combine(string basePath, string relative)
    {
        if (string.IsNullOrEmpty(relative))
            return basePath;

        if (!IsArchive(basePath))
            return Path.Combine(basePath, relative.Replace('/', Path.DirectorySeparatorChar));

        var (archive, inner) = ArchivePath.SplitPath(basePath);
        var head = NormalizeInner(inner);
        var tail = NormalizeInner(relative);
        var joined = head.Length == 0 ? tail : head + "/" + tail;
        return ArchivePath.MakePath(archive, joined);
    }

    /// <summary>
    /// Path of <paramref name="fullPath"/> relative to <paramref name="basePath"/>.
    /// Returns the bare name when the two live in unrelated trees.
    /// </summary>
    public static string GetRelative(string basePath, string fullPath)
    {
        if (IsArchive(basePath) || IsArchive(fullPath))
        {
            var baseInner = GetInner(basePath);
            var fullInner = GetInner(fullPath);

            if (baseInner.Length == 0)
                return fullInner;

            if (fullInner.Length > baseInner.Length &&
                fullInner.StartsWith(baseInner, StringComparison.OrdinalIgnoreCase) &&
                fullInner[baseInner.Length] == '/')
                return fullInner[(baseInner.Length + 1)..];

            return GetName(fullPath);
        }

        try
        {
            var rel = Path.GetRelativePath(basePath, fullPath);
            return rel.StartsWith("..", StringComparison.Ordinal) ? GetName(fullPath) : rel;
        }
        catch (ArgumentException)
        {
            return GetName(fullPath);
        }
    }

    /// <summary>Parent directory, or an empty string when there is none.</summary>
    public static string GetParent(string path)
    {
        if (IsArchive(path))
        {
            var (archive, inner) = ArchivePath.SplitPath(path);
            var normalized = NormalizeInner(inner);
            if (normalized.Length == 0)
                return "";
            var cut = normalized.LastIndexOf('/');
            return ArchivePath.MakePath(archive, cut < 0 ? "" : normalized[..cut]);
        }

        return Path.GetDirectoryName(path) ?? "";
    }

    /// <summary>Last path component.</summary>
    public static string GetName(string path)
    {
        if (IsArchive(path))
        {
            var inner = GetInner(path);
            if (inner.Length == 0)
                return Path.GetFileName(GetArchiveFile(path));
            var cut = inner.LastIndexOf('/');
            return cut < 0 ? inner : inner[(cut + 1)..];
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>Replaces the last path component with <paramref name="newName"/>.</summary>
    public static string ChangeName(string path, string newName)
    {
        var parent = GetParent(path);
        return string.IsNullOrEmpty(parent) ? newName : Combine(parent, newName);
    }
}
