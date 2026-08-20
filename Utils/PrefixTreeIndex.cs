namespace CoderCommander.Utils;

/// <summary>
/// A '/'-segmented prefix tree built once from a flat "normalized path → payload" entry list, so
/// repeated child-listing / exact-lookup / has-descendants queries against the same snapshot are
/// O(children) / O(1) / O(1) instead of a fresh O(n) scan of the whole flat list on every call.
///
/// Shared by <c>FileSystem.ZipArchiveFileSystem</c> (its own <c>ZipEntryRecord</c>) and
/// <c>Archives.ArchiveDirectory</c> (<c>Archives.ArchiveEntryRecord</c>) - the two independent
/// flat archive-entry-list representations in this codebase. Both used to re-scan every entry
/// (with a fresh normalized-name string comparison per entry) on every single panel navigation,
/// which is what made browsing a large archive (hundreds of thousands of entries) freeze the UI
/// for seconds at a time on each folder click. Not archive-specific itself - it only needs a
/// normalized '/'-separated path and a last-write timestamp per entry, so it has no dependency on
/// either archive-entry type.
/// </summary>
internal sealed class PrefixTreeIndex<T> where T : class
{
    public sealed class Node
    {
        internal readonly Dictionary<string, Node> ChildrenByName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The exact entry at this node's own path, if the source list contains one (a
        /// real file or an explicit directory-marker entry) - null for a node that exists only
        /// because a deeper entry's path passes through it (an implicit/synthesized folder, e.g.
        /// a ZIP that never stored an explicit directory entry for it).</summary>
        public T? Entry { get; internal set; }

        /// <summary>Best-known timestamp for this path: the entry's own if <see cref="Entry"/> is
        /// set, otherwise the timestamp of whichever entry first caused this node to be
        /// synthesized (arbitrary but stable for one snapshot - matches the linear-scan behavior
        /// this replaces, which used "whichever entry happened to be seen first").</summary>
        public DateTime LastWriteTimeUtc { get; internal set; }

        internal bool TimestampIsAuthoritative;

        /// <summary>Immediate children, keyed by their own path segment (ordinal-insensitive).</summary>
        public IReadOnlyDictionary<string, Node> Children => ChildrenByName;
    }

    /// <summary>The archive root. Its own <see cref="Node.Entry"/> is always null (no entry can
    /// have an empty normalized path).</summary>
    public Node Root { get; } = new();

    private readonly Dictionary<string, T> _byExactName;

    /// <summary>Builds from an already-normalized "(entry, normalizedName)" list - both callers
    /// (<c>ArchiveDirectory.NormalizedEntries</c>, and <c>ZipArchiveFileSystem</c>'s equivalent)
    /// already have one of these for their own other purposes, so this avoids re-deriving the
    /// normalized name a second time here.</summary>
    public PrefixTreeIndex(
        IReadOnlyList<(T Entry, string NormalizedName)> normalizedEntries,
        Func<T, DateTime> lastWriteTimeUtc)
    {
        _byExactName = new Dictionary<string, T>(normalizedEntries.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var (e, name) in normalizedEntries)
        {
            if (name.Length == 0) continue; // a literal "/" root entry, if any - nothing to index

            // First entry wins on a duplicate normalized name, matching the linear-scan-with-
            // early-return this replaces (FindEntry used to return the first match it found).
            _byExactName.TryAdd(name, e);

            var node = Root;
            var start = 0;
            while (true)
            {
                var slash = name.IndexOf('/', start);
                var isLast = slash < 0;
                var segment = isLast ? name[start..] : name[start..slash];

                if (!node.ChildrenByName.TryGetValue(segment, out var child))
                {
                    child = new Node();
                    node.ChildrenByName[segment] = child;
                }
                node = child;

                if (isLast)
                {
                    // The exact entry for this full path - its own timestamp is authoritative,
                    // overriding whatever a deeper entry may have synthesized here earlier.
                    node.Entry = e;
                    node.LastWriteTimeUtc = lastWriteTimeUtc(e);
                    node.TimestampIsAuthoritative = true;
                    break;
                }

                if (!node.TimestampIsAuthoritative && node.LastWriteTimeUtc == default)
                    node.LastWriteTimeUtc = lastWriteTimeUtc(e);

                start = slash + 1;
            }
        }
    }

    /// <summary>Exact entry at <paramref name="normalizedPath"/> - O(1).</summary>
    public bool TryGetExact(string normalizedPath, out T? entry) => _byExactName.TryGetValue(normalizedPath, out entry);

    /// <summary>Navigates to the node at <paramref name="normalizedPath"/> (empty for root), or
    /// null if nothing in the archive lives at or below that path.</summary>
    public Node? Navigate(string normalizedPath)
    {
        if (normalizedPath.Length == 0) return Root;

        var node = Root;
        var start = 0;
        while (true)
        {
            var slash = normalizedPath.IndexOf('/', start);
            var isLast = slash < 0;
            var segment = isLast ? normalizedPath[start..] : normalizedPath[start..slash];

            if (!node.ChildrenByName.TryGetValue(segment, out var next))
                return null;
            node = next;
            if (isLast) return node;
            start = slash + 1;
        }
    }
}
