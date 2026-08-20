using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using CoderCommander.Services;

namespace CoderCommander.Archives.Tar;

/// <summary>
/// Reads TAR/TAR.GZ entries via <see cref="TarReader"/>, optionally gzip-decompressing first.
/// TAR has no central directory, so <see cref="OpenEntry"/> (random access by index) isn't
/// possible - everything goes through a single forward pass, either as a full
/// <see cref="ReadDirectoryAsync"/> scan (metadata only, content discarded) or as
/// <see cref="ScanAsync"/> (content included). For TAR.GZ specifically this means even a plain
/// directory listing decompresses the entire file, since gzip streams can't seek - an inherent
/// cost of the format, not a bug.
/// </summary>
public sealed class TarArchiveReader : IArchiveReader
{
    private readonly string _archivePath;
    private readonly bool _gzip;

    public TarArchiveReader(string archivePath, bool gzip)
    {
        _archivePath = archivePath;
        _gzip = gzip;
    }

    public bool SupportsRandomAccess => false;

    public async Task<ArchiveDirectory> ReadDirectoryAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_archivePath))
            return new ArchiveDirectory(Array.Empty<ArchiveEntryRecord>(), isValid: false);

        var entries = new List<ArchiveEntryRecord>();
        try
        {
            using var fileStream = ArchiveFileRetry.OpenReadWithRetry(_archivePath);
            using Stream? gzipStream = _gzip ? new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: true) : null;
            using var reader = new TarReader(gzipStream ?? (Stream)fileStream, leaveOpen: true);

            var index = 0;
            TarEntry? entry;
            while ((entry = await reader.GetNextEntryAsync(copyData: false, ct).ConfigureAwait(false)) != null)
            {
                ct.ThrowIfCancellationRequested();
                entries.Add(ToRecord(entry, index++));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Warning($"Archive not accessible: {_archivePath}: {ex.Message}");
            return new ArchiveDirectory(Array.Empty<ArchiveEntryRecord>(), isValid: false);
        }

        return new ArchiveDirectory(entries, isValid: true);
    }

    public Stream OpenEntry(ArchiveEntryRecord entry) =>
        throw new NotSupportedException("TAR/TAR.GZ do not support random-access entry opening; use ScanAsync instead.");

    public async IAsyncEnumerable<ArchiveEntryStream> ScanAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var fileStream = ArchiveFileRetry.OpenReadWithRetry(_archivePath);
        Stream? source = null;
        TarReader reader;
        try
        {
            source = _gzip ? new GZipStream(fileStream, CompressionMode.Decompress) : fileStream;
            reader = new TarReader(source, leaveOpen: false);
        }
        catch
        {
            // Dispose `source` (which may be a GZipStream wrapping fileStream) rather than just
            // fileStream — GZipStream's finalizer would otherwise linger until GC, and explicitly
            // disposing it also disposes the underlying fileStream (leaveOpen: false).
            source?.Dispose();
            throw;
        }

        var index = 0;
        TarEntry? entry;
        try
        {
            // copyData: true when the underlying stream can't seek (the gzip case - GZipStream.CanSeek
            // is always false). TarReader's own documented contract for GetNextEntryAsync's copyData
            // parameter: with copyData:false over an unseekable stream, "the user has the
            // responsibility of reading and processing the DataStream immediately after calling this
            // method" - but this iterator yields the entry and returns control to the caller
            // (RewritingArchiveWriter.CopySurvivorsAsync's `await foreach`), which does further async
            // work (writing the *previous* entry to the output archive, or draining/skipping it via
            // NonDisposingStream) before resuming this iterator to ask for the next entry. That gap is
            // exactly what violated the contract: TarReader could no longer tell how much of the
            // previous entry had actually been consumed by the time GetNextEntryAsync was called
            // again, and threw EndOfStreamException trying to parse leftover entry bytes as the next
            // header. copyData:true buffers each entry into its own private MemoryStream up front,
            // which survives that gap by construction - exactly the case the BCL added the flag for.
            // Reproduced and verified against a real .tar.gz: DeleteAsync on either format (plain TAR
            // or TAR.GZ) worked without this fix on TAR, but TAR.GZ failed deleting ANY entry (not
            // just a specific position) until this changed.
            while ((entry = await reader.GetNextEntryAsync(copyData: !source.CanSeek, ct).ConfigureAwait(false)) != null)
            {
                ct.ThrowIfCancellationRequested();
                var record = ToRecord(entry, index++);
                // TarReader inspects the previous entry's DataStream when asked for the next one (to
                // know how much unread data to skip), so it must stay alive until then - see
                // NonDisposingStream's doc comment for why every consumer's `using`/Dispose() must not
                // be what tears it down. Harmless (if redundant) now that a gzip-sourced DataStream is
                // an independent, already-fully-buffered MemoryStream rather than a live view into the
                // archive stream - draining an already-complete buffer on dispose is a no-op.
                var content = entry.DataStream is { } dataStream ? new NonDisposingStream(dataStream) : Stream.Null;
                yield return new ArchiveEntryStream(record, content);
            }
        }
        finally
        {
            reader.Dispose();
        }
    }

    /// <summary>No state to release: each call above opens and closes its own file handle.</summary>
    public void Dispose() { }

    private static ArchiveEntryRecord ToRecord(TarEntry entry, int index)
    {
        var name = entry.Name.Replace('\\', '/');
        // GNU tar and many other tools prefix entries with "./" (e.g. "./.claude/").
        // Some double it up ("././file.txt"). Strip every leading "./", not just one,
        // so downstream code sees clean names.
        while (name.StartsWith("./", StringComparison.Ordinal))
            name = name[2..];

        var isDirectory = entry.EntryType == TarEntryType.Directory || name.EndsWith('/');
        var isLink = entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink;

        return new ArchiveEntryRecord
        {
            FullName = isDirectory ? name.TrimEnd('/') + "/" : name,
            IsDirectory = isDirectory,
            Size = isDirectory ? 0 : entry.Length,
            PackedSize = 0,
            LastWriteTimeUtc = entry.ModificationTime.UtcDateTime,
            Index = index,
            IsLink = isLink
        };
    }
}
