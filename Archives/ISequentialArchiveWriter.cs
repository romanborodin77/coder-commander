namespace CoderCommander.Archives;

/// <summary>
/// Minimal per-entry write primitive a sequential container format (TAR, TAR.GZ) supplies to
/// <see cref="RewritingArchiveWriter"/> - both "copy an existing entry across unchanged" and
/// "write a brand new entry" go through this same shape, over a plain output <see cref="Stream"/>
/// rather than a file path, so the same primitive works for both the staging file and the final
/// rewritten archive.
/// </summary>
public interface ISequentialArchiveWriter : IDisposable
{
    void WriteDirectory(string entryName, DateTime lastWriteTimeUtc);

    /// <summary>
    /// Formats whose compression applies to the whole container rather than per entry (e.g.
    /// gzip-over-tar) may ignore <paramref name="compression"/> beyond the level the container was
    /// opened with.
    /// </summary>
    Task WriteFileAsync(string entryName, Stream content, long size, DateTime lastWriteTimeUtc,
        ArchiveCompressionSpec compression, CancellationToken ct);
}
