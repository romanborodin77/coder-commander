namespace CoderCommander.Archives;

/// <summary>
/// Generic <see cref="IArchiveWriter"/> for container formats that can't update in place (TAR,
/// TAR.GZ) - new/changed entries stream into a staging file via a format-supplied
/// <see cref="ISequentialArchiveWriter"/> as they arrive, and <see cref="CommitAsync"/> produces the
/// real result by copying every surviving original entry (via <see cref="IArchiveFormat.OpenRead"/>,
/// skipping anything touched this session) followed by the staged entries into a fresh file, then
/// atomically replacing the original. The original is only ever read, never written to directly,
/// so a failure at any point before the final <see cref="File.Move(string, string, bool)"/> leaves
/// it completely untouched.
/// </summary>
public sealed class RewritingArchiveWriter : IArchiveWriter
{
    private readonly string _archivePath;
    private readonly IArchiveFormat _format;
    private readonly Func<Stream, ISequentialArchiveWriter> _createWriter;
    private readonly string _stagingPath;
    private readonly FileStream _stagingStream;
    private readonly ISequentialArchiveWriter _stagingWriter;
    private readonly HashSet<string> _touchedNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _committed;
    private bool _disposed;

    public RewritingArchiveWriter(string archivePath, IArchiveFormat format, Func<Stream, ISequentialArchiveWriter> createWriter)
    {
        _archivePath = archivePath;
        _format = format;
        _createWriter = createWriter;
        _stagingPath = archivePath + ".stage-" + Guid.NewGuid().ToString("N") + ".tmp";
        _stagingStream = new FileStream(_stagingPath, FileMode.Create, FileAccess.Write, FileShare.None);
        _stagingWriter = createWriter(_stagingStream);
    }

    public ArchiveWriteMode Mode => ArchiveWriteMode.RewriteThrough;

    public void CreateDirectoryEntry(string entryName, DateTime lastWriteTimeUtc)
    {
        _touchedNames.Add(Normalize(entryName));
        _stagingWriter.WriteDirectory(entryName, lastWriteTimeUtc);
    }

    public async Task WriteFileAsync(
        string entryName,
        Stream content,
        long size,
        DateTime lastWriteTimeUtc,
        ArchiveCompressionSpec compression,
        CancellationToken ct = default)
    {
        _touchedNames.Add(Normalize(entryName));
        await _stagingWriter.WriteFileAsync(entryName, content, size, lastWriteTimeUtc, compression, ct).ConfigureAwait(false);
    }

    /// <summary>Always reports success: actual removal is realized at <see cref="CommitAsync"/> by
    /// simply not copying the touched name across, so existence isn't verified against the
    /// original until then.</summary>
    public bool TryDeleteEntry(ArchiveEntryRecord entry)
    {
        _touchedNames.Add(Normalize(entry.FullName));
        return true;
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_committed) return;
        _committed = true;

        _stagingWriter.Dispose();
        await _stagingStream.DisposeAsync().ConfigureAwait(false);

        var finalPath = _archivePath + ".rewrite-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var finalStream = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var finalWriter = _createWriter(finalStream))
                {
                    if (File.Exists(_archivePath))
                    {
                        using var originalReader = _format.OpenRead(_archivePath);
                        await CopySurvivorsAsync(originalReader, finalWriter, ct).ConfigureAwait(false);
                    }

                    using var stagingReader = _format.OpenRead(_stagingPath);
                    await foreach (var item in stagingReader.ScanAsync(ct).ConfigureAwait(false))
                    {
                        using var content = item.Content;
                        if (item.Entry.IsDirectory)
                            finalWriter.WriteDirectory(item.Entry.FullName, item.Entry.LastWriteTimeUtc);
                        else
                            await finalWriter.WriteFileAsync(item.Entry.FullName, content, item.Entry.Size,
                                item.Entry.LastWriteTimeUtc, ArchiveCompressionSpec.Balanced, ct).ConfigureAwait(false);
                    }
                }
            }

            File.Move(finalPath, _archivePath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(finalPath);
            TryDeleteFile(_stagingPath);
        }
    }

    private async Task CopySurvivorsAsync(IArchiveReader originalReader, ISequentialArchiveWriter finalWriter, CancellationToken ct)
    {
        await foreach (var item in originalReader.ScanAsync(ct).ConfigureAwait(false))
        {
            using var content = item.Content;
            if (_touchedNames.Contains(Normalize(item.Entry.FullName)))
                continue;

            if (item.Entry.IsDirectory)
                finalWriter.WriteDirectory(item.Entry.FullName, item.Entry.LastWriteTimeUtc);
            else
                await finalWriter.WriteFileAsync(item.Entry.FullName, content, item.Entry.Size,
                    item.Entry.LastWriteTimeUtc, ArchiveCompressionSpec.Balanced, ct).ConfigureAwait(false);
        }
    }

    private static string Normalize(string name) => name.Replace('\\', '/').Trim('/');

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort cleanup */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_committed)
        {
            try { _stagingWriter.Dispose(); } catch { /* best effort */ }
            try { _stagingStream.Dispose(); } catch { /* best effort */ }
        }

        TryDeleteFile(_stagingPath);
    }

    /// <summary>
    /// Deliberately does NOT auto-commit when <see cref="CommitAsync"/> wasn't called explicitly:
    /// disposal happens on both the normal path and when an exception unwinds an <c>await using</c>
    /// block, and there is no way to tell those two apart from in here. Auto-committing would turn
    /// "pack failed partway through" into "silently commit whatever had been staged so far" -
    /// exactly the corruption this writer exists to prevent. An uncommitted session's staged
    /// entries are simply discarded, leaving the original archive untouched.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
