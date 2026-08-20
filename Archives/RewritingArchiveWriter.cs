using CoderCommander.Utils;

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
    // Keyed by (name, isDirectory) rather than just name: an archive can pathologically contain
    // both a file "foo" and a directory "foo/" at once, and normalizing away the trailing slash
    // used to collapse them into the same key, so deleting/touching one could make CopySurvivorsAsync
    // skip the other too.
    private readonly HashSet<(string Name, bool IsDirectory)> _touchedNames = new();
    private FileStream? _lock;
    private bool _committed;
    private bool _disposed;

    public RewritingArchiveWriter(string archivePath, IArchiveFormat format, Func<Stream, ISequentialArchiveWriter> createWriter)
    {
        _archivePath = archivePath;
        _format = format;
        _createWriter = createWriter;
        _stagingPath = TempFileNaming.NextTo(archivePath, "stage");

        // Hold an exclusive lock on the real archive from construction through the start of
        // CommitAsync (released there, before reading survivors - see CommitAsync for why):
        // without it, a second writer session for the same archive could commit its own changes
        // in the gap between "this session decided what to add/delete" (WriteFileAsync/
        // TryDeleteEntry calls) and "this session's commit reads the original" - whichever
        // commits last would silently discard the other's changes via its final File.Move. Only
        // archives that already exist need this; a brand-new archive has nothing to race over yet.
        _lock = File.Exists(archivePath) ? ArchiveFileRetry.OpenExclusiveWithRetry(archivePath) : null;

        try
        {
            _stagingStream = new FileStream(_stagingPath, FileMode.Create, FileAccess.Write, FileShare.None);
            _stagingWriter = createWriter(_stagingStream);
        }
        catch
        {
            // If staging stream or writer creation fails, release the exclusive lock and clean
            // up the partial staging file — without this, the archive stays locked (FileShare.None)
            // until GC finalizes the FileStream, blocking all other access.
            _stagingStream?.Dispose();
            _lock?.Dispose();
            try { File.Delete(_stagingPath); } catch { /* best-effort */ }
            throw;
        }
    }

    public ArchiveWriteMode Mode => ArchiveWriteMode.RewriteThrough;

    public void CreateDirectoryEntry(string entryName, DateTime lastWriteTimeUtc)
    {
        _touchedNames.Add(Key(entryName, isDirectory: true));
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
        _touchedNames.Add(Key(entryName, isDirectory: false));
        await _stagingWriter.WriteFileAsync(entryName, content, size, lastWriteTimeUtc, compression, ct).ConfigureAwait(false);
    }

    /// <summary>Always reports success: actual removal is realized at <see cref="CommitAsync"/> by
    /// simply not copying the touched name across, so existence isn't verified against the
    /// original until then.</summary>
    public bool TryDeleteEntry(ArchiveEntryRecord entry)
    {
        _touchedNames.Add(Key(entry.FullName, entry.IsDirectory));
        return true;
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_committed) return;
        _committed = true;

        _stagingWriter.Dispose();
        await _stagingStream.DisposeAsync().ConfigureAwait(false);

        // Release the exclusive lock before reading survivors, not after: _lock was opened with
        // FileShare.None, so _format.OpenRead(_archivePath) below - a SECOND handle to the same
        // path, from this same process - would otherwise deadlock against our own lock every
        // single time an archive already exists (verified: 5/5 reproductions, not a transient
        // flake - see ArchiveFileRetry.OpenReadWithRetry exhausting all retries against a lock
        // that was never actually going to release itself mid-CommitAsync). The lock's purpose
        // (block a second writer session from deciding what to add/delete while this session is
        // mid-decision, then silently losing that race via File.Move) is still served in full up
        // to this point; only the read-and-rewrite phase below is now unprotected, a narrower
        // version of the "instant between releasing the lock and the move completing" gap the
        // final File.Move already had to accept.
        _lock?.Dispose();
        _lock = null;

        var finalPath = TempFileNaming.NextTo(_archivePath, "rewrite");
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
                                item.Entry.LastWriteTimeUtc, DefaultCompression, ct).ConfigureAwait(false);
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
            if (_touchedNames.Contains(Key(item.Entry.FullName, item.Entry.IsDirectory)))
                continue;

            if (item.Entry.IsDirectory)
                finalWriter.WriteDirectory(item.Entry.FullName, item.Entry.LastWriteTimeUtc);
            else
                await finalWriter.WriteFileAsync(item.Entry.FullName, content, item.Entry.Size,
                    item.Entry.LastWriteTimeUtc, DefaultCompression, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Audit finding S2 (DEBUG.md §0.1): both write sites above used to hardcode
    /// <c>ArchiveCompressionSpec.Balanced</c> regardless of <see cref="_format"/>. Investigated
    /// before fixing rather than taken at face value - the seed finding's framing ("compression
    /// preset lost on add/delete") turned out not to describe real data loss: every
    /// <see cref="ISequentialArchiveWriter"/> this class wraps (<c>TarSequentialWriter</c>,
    /// <c>SharpCompressTarWriter</c>) ignores the per-call <c>compression</c> parameter outright -
    /// TAR-family compression is a whole-container decision made once when the writer/stream is
    /// constructed, not a per-entry one (see <c>TarGzArchiveFormat</c>'s own doc comment). So the
    /// hardcoded value never actually changed a single byte of output for TAR.GZ or TAR.BZ2, whose
    /// <see cref="IArchiveFormat.SupportedPresets"/> is <c>[Balanced]</c> anyway - the one real,
    /// if inert, mismatch was plain TAR, whose <c>SupportedPresets</c> is <c>[Store]</c>: the code
    /// said "Balanced" for a format that has no compression at all. Deriving the spec from
    /// <see cref="_format"/> instead removes the mismatch and stays correct automatically if a
    /// future format routed through this writer ever supports more than one preset.
    /// </summary>
    private ArchiveCompressionSpec DefaultCompression => new(_format.SupportedPresets[0]);

    // Case-sensitive on purpose: TAR (and TAR.GZ/TAR.BZ2, the only formats routed through this
    // writer) can legitimately contain both "README.txt" and "readme.txt" as distinct entries -
    // folding case here (as this used to do via ToUpperInvariant()) collapsed both onto the same
    // key, so touching/deleting one made CopySurvivorsAsync skip the OTHER, untouched one too,
    // silently dropping it from the rewritten archive.
    private static (string Name, bool IsDirectory) Key(string name, bool isDirectory) =>
        (name.Replace('\\', '/').Trim('/'), isDirectory);

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

        try { _lock?.Dispose(); } catch { /* best effort */ }
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
