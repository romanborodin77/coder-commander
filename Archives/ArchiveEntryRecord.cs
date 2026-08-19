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

    /// <summary>Pre-computed normalized names (backslash→forward, trimmed, "./" stripped) paired
    /// with their source records, so <see cref="ArchiveTree"/> doesn't re-normalize every entry on
    /// every listing, search, or existence check — critical for archives with 50k+ entries.</summary>
    public IReadOnlyList<(ArchiveEntryRecord Entry, string NormalizedName)> NormalizedEntries { get; }

    public ArchiveDirectory(IReadOnlyList<ArchiveEntryRecord> entries, bool isValid)
    {
        Entries = entries;
        IsValid = isValid;
        NormalizedEntries = BuildNormalized(entries);
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
