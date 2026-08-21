namespace CoderCommander.Archives;

/// <summary>
/// How an <see cref="IArchiveWriter"/> applies changes. Callers don't need to branch on this -
/// it only affects what <see cref="IArchiveWriter"/> does internally (e.g. whether
/// <see cref="IArchiveWriter.CommitAsync"/> is a no-op or an atomic file replace).
/// </summary>
public enum ArchiveWriteMode
{
    /// <summary>Building a brand new archive from nothing.</summary>
    CreateNew,
    /// <summary>Format's container supports adding/removing entries without rewriting the rest
    /// (ZIP's central directory).</summary>
    UpdateInPlace,
    /// <summary>Format has no in-place update; changes are staged and applied by rewriting the
    /// whole container through a temp file (see <see cref="RewritingArchiveWriter"/> in a later phase).</summary>
    RewriteThrough
}

/// <summary>
/// Writes to an archive: create, add, and delete entries, format-neutral. Implementations decide
/// how (or whether) each operation is possible internally; unsupported operations for a
/// read-only format simply aren't reachable because <see cref="ArchiveFormatRegistry"/> only
/// exposes writers for formats whose <see cref="ArchiveCapabilities"/> include the needed flag.
/// </summary>
public interface IArchiveWriter : IAsyncDisposable, IDisposable
{
    ArchiveWriteMode Mode { get; }

    void CreateDirectoryEntry(string entryName, DateTime lastWriteTimeUtc);

    Task WriteFileAsync(
        string entryName,
        Stream content,
        long size,
        DateTime lastWriteTimeUtc,
        ArchiveCompressionSpec compression,
        CancellationToken ct = default);

    /// <summary>Attempts to remove an entry. Returns false if the entry no longer exists, for
    /// writers that can check immediately (e.g. <see cref="Zip.ZipArchiveWriter"/>). A
    /// stage-then-rewrite writer like <see cref="RewritingArchiveWriter"/> defers existence
    /// verification to <see cref="CommitAsync"/> and always returns true here - no current caller
    /// depends on this return value, but don't assume false is a reliable "not found" signal
    /// across every implementation.</summary>
    bool TryDeleteEntry(ArchiveEntryRecord entry);

    /// <summary>Renames an existing entry to <paramref name="newName"/>, passing its content
    /// through unchanged (no decompress/recompress for a <see cref="RewritingArchiveWriter"/>-backed
    /// format; for ZIP, still one CPU-bound recompress, but within this session's one archive-wide
    /// I/O pass rather than a second one - see each implementation's own doc comment). Same
    /// existence-verification contract as <see cref="TryDeleteEntry"/>: a stage-then-rewrite writer
    /// defers verification to <see cref="CommitAsync"/> and always returns true here.</summary>
    bool TryRenameEntry(ArchiveEntryRecord entry, string newName);

    /// <summary>Flushes/finalizes all pending changes. Must be called before disposal for
    /// changes to be guaranteed durable.</summary>
    Task CommitAsync(CancellationToken ct = default);
}
