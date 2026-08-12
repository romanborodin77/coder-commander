namespace CoderCommander.FileSystem;

/// <summary>
/// Path arithmetic for every path flavour the app has: plain Windows paths, virtual archive paths
/// of the form <c>C:\dir\file.zip|inner/dir/name</c>, and remote paths of the form
/// <c>dav://host/dir/name</c>.
/// <para>
/// <see cref="System.IO.Path"/> mangles both virtual forms - <c>|</c> is illegal in a Windows path,
/// <c>Path.Combine</c> would insert backslashes inside the archive part, and
/// <c>Path.GetPathRoot("dav://host/x")</c> answers with an empty string - so every operation that
/// may touch either routes through these helpers instead.
/// </para>
/// <para>
/// The three flavours are mutually exclusive by construction: <see cref="RemotePath"/> rejects
/// <c>|</c> in a remote path outright, so a remote path can never be read as an archive one. That
/// is why <see cref="IsArchive"/> can stay a bare <c>|</c> test.
/// </para>
/// </summary>
public static class VfsPath
{
    /// <summary>True when the path points inside an archive.</summary>
    public static bool IsArchive(string path) =>
        !string.IsNullOrEmpty(path) && path.Contains(ArchivePath.Separator, StringComparison.Ordinal);

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

        if (RemotePath.IsRemote(basePath))
            return RemotePath.Combine(basePath, relative);

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
        if (RemotePath.IsRemote(basePath) || RemotePath.IsRemote(fullPath))
        {
            // A remote path and a local one - or two different connections - are unrelated trees by
            // definition, and "unrelated" means the bare name, exactly as it does for two paths on
            // different drives. Reducing them through PathOf instead would silently treat the whole
            // of one as relative to the other and nest the result several levels too deep.
            if (!RemotePath.IsRemote(basePath) || !RemotePath.IsRemote(fullPath) ||
                !string.Equals(RemotePath.GetRoot(basePath), RemotePath.GetRoot(fullPath), StringComparison.OrdinalIgnoreCase))
                return GetName(fullPath);

            return RelativeInner(RemotePath.PathOf(basePath), RemotePath.PathOf(fullPath), fullPath);
        }

        if (IsArchive(basePath) || IsArchive(fullPath))
            return RelativeInner(GetInner(basePath), GetInner(fullPath), fullPath);

        try
        {
            var rel = Path.GetRelativePath(basePath, fullPath);
            // Path.GetRelativePath signals "unrelated trees" two different ways depending on
            // *how* unrelated the paths are: a "../" prefix when they share a root but diverge
            // partway down, or - for a pair on different drives entirely (e.g. "C:\a" vs
            // "D:\b\c.txt") - the second path returned completely unchanged, rooted, with no
            // "../" at all. Only checking the first form let a cross-volume fullPath leak through
            // as a full rooted path instead of falling back to the bare name like every other
            // "unrelated trees" case here.
            return rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)
                ? GetName(fullPath)
                : rel;
        }
        catch (ArgumentException)
        {
            return GetName(fullPath);
        }
    }

    /// <summary>
    /// Shared tail of <see cref="GetRelative"/> for the two slash-separated flavours: both reduce
    /// to "strip the base prefix off the full inner path", differing only in how the inner path is
    /// extracted. Falls back to the bare name when the two are not in the same subtree, which is
    /// the same "unrelated trees" answer the plain-path branch gives.
    /// </summary>
    private static string RelativeInner(string baseInner, string fullInner, string fullPath)
    {
        if (baseInner.Length == 0)
            return fullInner;

        if (fullInner.Length > baseInner.Length &&
            fullInner.StartsWith(baseInner, StringComparison.OrdinalIgnoreCase) &&
            fullInner[baseInner.Length] == '/')
            return fullInner[(baseInner.Length + 1)..];

        return GetName(fullPath);
    }

    /// <summary>Parent directory, or an empty string when there is none.</summary>
    public static string GetParent(string path)
    {
        if (RemotePath.IsRemote(path))
        {
            // At the connection root there is no parent inside this filesystem - the same answer
            // the archive branch gives at an archive's own root. Leaving a remote filesystem is a
            // navigation decision, not path arithmetic.
            var parent = RemotePath.GetParent(path);
            return string.Equals(parent, path, StringComparison.Ordinal) ? "" : parent;
        }

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
        if (RemotePath.IsRemote(path))
            return RemotePath.GetName(path);

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

    /// <summary>
    /// Replaces the last path component with <paramref name="newName"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="newName"/> fails <see cref="RemotePath.IsSafeEntryName"/> - a path separator,
    /// <c>.</c>/<c>..</c>, an NTFS alternate-data-stream colon, a reserved DOS device name
    /// (<c>CON</c>/<c>COM1</c>/...), a trailing dot/space, or a display-spoofing character. This is
    /// the one place every overwrite-conflict "rename" flow (Copy/Move/Pack/Unpack) funnels through,
    /// so it's the single choke point that stops a caller-supplied name (e.g. from an
    /// <see cref="Operations.OverwriteResolveHandler"/>) from escaping the target directory or
    /// smuggling an ADS/device name onto local disk - callers were otherwise trusting the UI layer
    /// never to hand back something like <c>..\..\evil.exe</c> or <c>readme.txt:payload.exe</c>,
    /// with no check at the operation level itself.
    /// </exception>
    public static string ChangeName(string path, string newName)
    {
        if (!RemotePath.IsSafeEntryName(newName))
            throw new ArgumentException($"Invalid entry name: \"{newName}\"", nameof(newName));

        var parent = GetParent(path);
        return string.IsNullOrEmpty(parent) ? newName : Combine(parent, newName);
    }
}
