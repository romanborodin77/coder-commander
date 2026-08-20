using CoderCommander.Utils;

namespace CoderCommander.Archives;

/// <summary>
/// Format-neutral description of one entry inside an archive - the common shape every
/// <see cref="IArchiveReader"/>/<see cref="IArchiveWriter"/> implementation produces and consumes,
/// regardless of container format.
/// </summary>
public sealed record ArchiveEntryRecord
{
    public required string FullName { get; init; }
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    /// <summary>Compressed/on-disk size. 0 when the format doesn't report it separately.</summary>
    public long PackedSize { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }

    /// <summary>
    /// Stable ordinal within the archive - the addressing token <see cref="IArchiveReader.OpenEntry"/>
    /// and delete operations use, since some formats' entry names don't round-trip byte-for-byte
    /// through the encoding the directory listing decoded them with (e.g. legacy code-page ZIP names).
    /// </summary>
    public int Index { get; init; }

    public bool IsEncrypted { get; init; }

    /// <summary>True for a symbolic or hard link entry. Readers that can't materialize the link
    /// target as real file content (all of them, currently) leave <see cref="IArchiveReader.ScanAsync"/>'s
    /// content stream empty for these - extraction skips them explicitly instead of silently
    /// writing a 0-byte file in the link's place.</summary>
    public bool IsLink { get; init; }
}

/// <summary>Immutable snapshot of an archive's directory listing.</summary>
public sealed class ArchiveDirectory
{
    public static readonly ArchiveDirectory Empty = new(Array.Empty<ArchiveEntryRecord>(), isValid: false);

    public IReadOnlyList<ArchiveEntryRecord> Entries { get; }

    /// <summary>False when the container could not be read (locked, corrupt, truncated) - callers
    /// distinguish this from a genuinely empty archive.</summary>
    public bool IsValid { get; }

    private readonly Lazy<(ArchiveEntryRecord, string)[]> _normalizedEntries;

    /// <summary>Pre-computed normalized names (backslash→forward, trimmed, "./" stripped) paired
    /// with their source records. Lazy: a caller that only ever reads <see cref="Entries"/> (most
    /// of Operations/PackOperation.cs's and UnpackOperation.cs's own already-optimized paths, for
    /// instance) shouldn't pay for a normalized-name array it never looks at, and this array alone
    /// was a meaningful fraction of the per-archive memory footprint the directory cache retains.</summary>
    public IReadOnlyList<(ArchiveEntryRecord Entry, string NormalizedName)> NormalizedEntries => _normalizedEntries.Value;

    private readonly Lazy<PrefixTreeIndex<ArchiveEntryRecord>> _index;

    /// <summary>
    /// '/'-segmented prefix tree over <see cref="NormalizedEntries"/> - what turns
    /// <see cref="ArchiveTree"/>'s listing/exact-lookup/has-descendants queries from an O(n) scan
    /// of every entry in the archive into O(children)/O(1)/O(1). Built once, lazily, on first
    /// query against this snapshot (the same snapshot is reused across every navigation inside the
    /// same archive at the same file stamp, via <see cref="ArchiveDirectoryCache"/>).
    /// </summary>
    internal PrefixTreeIndex<ArchiveEntryRecord> Index => _index.Value;

    public ArchiveDirectory(IReadOnlyList<ArchiveEntryRecord> entries, bool isValid)
    {
        Entries = entries;
        IsValid = isValid;
        _normalizedEntries = new Lazy<(ArchiveEntryRecord, string)[]>(() => BuildNormalized(entries));
        _index = new Lazy<PrefixTreeIndex<ArchiveEntryRecord>>(
            () => new PrefixTreeIndex<ArchiveEntryRecord>(NormalizedEntries, e => e.LastWriteTimeUtc));
    }

    private static (ArchiveEntryRecord, string)[] BuildNormalized(IReadOnlyList<ArchiveEntryRecord> entries)
    {
        var result = new (ArchiveEntryRecord, string)[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var t = e.FullName.Contains('\\', StringComparison.Ordinal) ? e.FullName.Replace('\\', '/') : e.FullName;
            t = t.Trim('/');
            while (t.StartsWith("./", StringComparison.Ordinal))
                t = t[2..];
            result[i] = (e, t);
        }
        return result;
    }
}
