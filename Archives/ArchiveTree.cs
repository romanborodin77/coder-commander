using CoderCommander.FileSystem;
using CoderCommander.Utils;

namespace CoderCommander.Archives;

/// <summary>
/// Turns a flat <see cref="ArchiveDirectory"/> listing into folder-tree queries - the same
/// prefix-matching logic <c>ZipArchiveFileSystem</c> uses internally for its own
/// EnumerateAsync/EnumerateDeepAsync/GetFileInfoAsync, generalized so <see cref="ArchiveFileSystem"/>
/// (or any future format's panel adapter) doesn't have to re-derive folder structure itself.
///
/// Every method here queries <see cref="ArchiveDirectory.Index"/> (a <see cref="PrefixTreeIndex{T}"/>
/// built once per snapshot) rather than scanning <see cref="ArchiveDirectory.NormalizedEntries"/>
/// linearly - the previous shape made every single navigation, existence check, or write inside a
/// large archive an O(n) walk of every entry the archive contains.
/// </summary>
public static class ArchiveTree
{
    /// <summary>Immediate children of <paramref name="innerPath"/> - files and one entry per
    /// distinct subfolder, synthesizing folder entries for paths that only exist implicitly as a
    /// prefix of deeper entries (matches ZIP archives that never stored an explicit dir entry).</summary>
    public static IReadOnlyList<FileEntry> ListChildren(ArchiveDirectory dir, string archiveHostPath, string innerPath)
    {
        var prefix = NormalizePrefix(innerPath);
        var node = dir.Index.Navigate(VfsPath.NormalizeInner(innerPath));
        if (node == null)
            return [];

        var result = new List<FileEntry>(node.Children.Count);
        foreach (var (name, child) in node.Children)
        {
            // A synthesized-only node (deeper entries pass through here) or an explicit directory
            // marker entry both mean "there is a folder here" - a node can be both this AND a file
            // (see the branch below) in a pathological archive with a file and a directory sharing
            // one name, which the linear-scan version this replaces also showed as two rows.
            if (child.Children.Count > 0 || child.Entry is { IsDirectory: true })
            {
                result.Add(new FileEntry(ArchivePath.MakePath(archiveHostPath, prefix + name), true,
                    lastWriteTimeUtc: child.LastWriteTimeUtc));
            }
            if (child.Entry is { IsDirectory: false } file)
            {
                result.Add(new FileEntry(ArchivePath.MakePath(archiveHostPath, prefix + name), false, true,
                    file.Size, lastWriteTimeUtc: file.LastWriteTimeUtc));
            }
        }

        return result;
    }

    /// <summary>Every descendant (files and folders) below <paramref name="innerPath"/>, flattened.
    /// Unlike <see cref="ListChildren"/>, this never synthesizes a folder row for an implicit
    /// directory - only entries that genuinely exist in the archive are returned, matching the
    /// linear scan this replaces.</summary>
    public static IReadOnlyList<FileEntry> ListDescendants(ArchiveDirectory dir, string archiveHostPath, string innerPath)
    {
        var normalized = VfsPath.NormalizeInner(innerPath);
        var node = dir.Index.Navigate(normalized);
        if (node == null)
            return [];

        var result = new List<FileEntry>();
        CollectDescendants(node, archiveHostPath, normalized, result);
        return result;
    }

    private static void CollectDescendants(PrefixTreeIndex<ArchiveEntryRecord>.Node node, string archiveHostPath, string normalizedPath, List<FileEntry> result)
    {
        foreach (var (name, child) in node.Children)
        {
            var childPath = normalizedPath.Length == 0 ? name : normalizedPath + "/" + name;
            if (child.Entry is { } entry)
            {
                var fullPath = ArchivePath.MakePath(archiveHostPath, childPath);
                result.Add(entry.IsDirectory
                    ? new FileEntry(fullPath, true, lastWriteTimeUtc: entry.LastWriteTimeUtc)
                    : new FileEntry(fullPath, false, true, entry.Size, lastWriteTimeUtc: entry.LastWriteTimeUtc));
            }
            CollectDescendants(child, archiveHostPath, childPath, result);
        }
    }

    /// <summary>Exact entry at <paramref name="innerPath"/>, or null if none exists there directly
    /// (it may still exist implicitly as a folder - see <see cref="HasDescendants"/>).</summary>
    public static ArchiveEntryRecord? FindEntry(ArchiveDirectory dir, string innerPath)
    {
        var normalized = VfsPath.NormalizeInner(innerPath);
        if (normalized.Length == 0)
            return null;

        return dir.Index.TryGetExact(normalized, out var entry) ? entry : null;
    }

    /// <summary>True if anything in the archive lives below <paramref name="innerPath"/>, even if
    /// no explicit directory entry for it exists.</summary>
    public static bool HasDescendants(ArchiveDirectory dir, string innerPath)
    {
        var normalized = VfsPath.NormalizeInner(innerPath);
        var node = dir.Index.Navigate(normalized);
        return node != null && node.Children.Count > 0;
    }

    private static string NormalizePrefix(string innerPath)
    {
        var normalized = VfsPath.NormalizeInner(innerPath);
        return normalized.Length == 0 ? "" : normalized + "/";
    }
}
