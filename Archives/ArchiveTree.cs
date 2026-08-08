using CoderCommander.FileSystem;

namespace CoderCommander.Archives;

/// <summary>
/// Turns a flat <see cref="ArchiveDirectory"/> listing into folder-tree queries - the same
/// prefix-matching logic <c>ZipArchiveFileSystem</c> uses internally for its own
/// EnumerateAsync/EnumerateDeepAsync/GetFileInfoAsync, generalized so <see cref="ArchiveFileSystem"/>
/// (or any future format's panel adapter) doesn't have to re-derive folder structure itself.
/// </summary>
public static class ArchiveTree
{
    /// <summary>Immediate children of <paramref name="innerPath"/> - files and one entry per
    /// distinct subfolder, synthesizing folder entries for paths that only exist implicitly as a
    /// prefix of deeper entries (matches ZIP archives that never stored an explicit dir entry).</summary>
    public static IReadOnlyList<FileEntry> ListChildren(IReadOnlyList<ArchiveEntryRecord> entries, string archiveHostPath, string innerPath)
    {
        var prefix = NormalizePrefix(innerPath);
        var result = new List<FileEntry>();
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var trimmedName = TrimmedName(entry);
            if (!trimmedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = trimmedName[prefix.Length..];
            if (rest.Length == 0)
                continue;

            var slashIdx = rest.IndexOf('/');
            if (slashIdx >= 0)
            {
                var dirName = rest[..slashIdx];
                if (seenDirs.Add(dirName))
                    result.Add(new FileEntry(ArchivePath.MakePath(archiveHostPath, prefix + dirName), true, lastWriteTimeUtc: entry.LastWriteTimeUtc));
            }
            else if (entry.IsDirectory)
            {
                if (seenDirs.Add(rest))
                    result.Add(new FileEntry(ArchivePath.MakePath(archiveHostPath, prefix + rest), true, lastWriteTimeUtc: entry.LastWriteTimeUtc));
            }
            else
            {
                result.Add(new FileEntry(ArchivePath.MakePath(archiveHostPath, trimmedName), false, true, entry.Size, lastWriteTimeUtc: entry.LastWriteTimeUtc));
            }
        }

        return result;
    }

    /// <summary>Every descendant (files and folders) below <paramref name="innerPath"/>, flattened.</summary>
    public static IReadOnlyList<FileEntry> ListDescendants(IReadOnlyList<ArchiveEntryRecord> entries, string archiveHostPath, string innerPath)
    {
        var prefix = NormalizePrefix(innerPath);
        var result = new List<FileEntry>();

        foreach (var entry in entries)
        {
            var trimmedName = TrimmedName(entry);
            if (!trimmedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (trimmedName.Length == prefix.Length)
                continue;

            var fullPath = ArchivePath.MakePath(archiveHostPath, trimmedName);
            result.Add(entry.IsDirectory
                ? new FileEntry(fullPath, true, lastWriteTimeUtc: entry.LastWriteTimeUtc)
                : new FileEntry(fullPath, false, true, entry.Size, lastWriteTimeUtc: entry.LastWriteTimeUtc));
        }

        return result;
    }

    /// <summary>Exact entry at <paramref name="innerPath"/>, or null if none exists there directly
    /// (it may still exist implicitly as a folder - see <see cref="HasDescendants"/>).</summary>
    public static ArchiveEntryRecord? FindEntry(IReadOnlyList<ArchiveEntryRecord> entries, string innerPath)
    {
        var normalized = VfsPath.NormalizeInner(innerPath);
        if (normalized.Length == 0)
            return null;

        foreach (var entry in entries)
        {
            if (string.Equals(TrimmedName(entry), normalized, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        return null;
    }

    /// <summary>True if anything in the archive lives below <paramref name="innerPath"/>, even if
    /// no explicit directory entry for it exists.</summary>
    public static bool HasDescendants(IReadOnlyList<ArchiveEntryRecord> entries, string innerPath)
    {
        var normalized = VfsPath.NormalizeInner(innerPath);
        // At the archive root, "below" means "anything at all" - the general case below builds a
        // "<path>/" prefix to match against, but TrimmedName's entries never start with a bare
        // "/", so an empty normalized path used to build prefix "/" and this always returned
        // false at the root, even for a non-empty archive.
        if (normalized.Length == 0)
            return entries.Count > 0;

        var prefix = normalized + "/";
        return entries.Any(e => TrimmedName(e).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string TrimmedName(ArchiveEntryRecord entry)
    {
        var t = entry.FullName.Replace('\\', '/').Trim('/');
        // GNU tar and many other tools prefix entries with "./" (e.g. "./.claude/") - and some
        // archives double it up ("././file.txt"). Strip every leading "./", not just one, so
        // ListChildren doesn't extract "." (or a leftover "./"-prefixed name) as a directory name.
        while (t.StartsWith("./", StringComparison.Ordinal))
            t = t[2..];
        return t;
    }

    private static string NormalizePrefix(string innerPath)
    {
        var normalized = VfsPath.NormalizeInner(innerPath);
        return normalized.Length == 0 ? "" : normalized + "/";
    }
}
