using System.Runtime.CompilerServices;
using CoderCommander.Services;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace CoderCommander.Archives.SharpCompress;

/// <summary>Which SharpCompress entry point <see cref="SharpCompressReader"/> should use - kept
/// as a closed set here rather than exposed generically, since it's the one thing that varies
/// between the four read-only formats built on this reader.</summary>
public enum SharpCompressKind
{
    /// <summary>7z archive — uses <see cref="SevenZipArchive"/>.</summary>
    SevenZip,

    /// <summary>RAR archive — uses <see cref="RarArchive"/>.</summary>
    Rar,

    /// <summary>TAR.BZ2 archive — uses <see cref="ReaderFactory"/>.</summary>
    TarBz2,

    /// <summary>TAR.XZ archive — uses <see cref="ReaderFactory"/> (read-only, no writer support).</summary>
    TarXz
}

/// <summary>
/// The read side of the two files in <c>Archives/SharpCompress/</c> that reference SharpCompress
/// types directly (the other is <see cref="SharpCompressTarWriter"/>) - its API has shifted between
/// versions before, so containing it to this one folder means a future upgrade touches at most two
/// files. Backs 7z and RAR (via <see cref="IArchive.ExtractAllEntries"/>, recommended by
/// SharpCompress itself over per-entry random access for solid archives) and TAR.BZ2/TAR.XZ (via
/// <see cref="ReaderFactory.Open(Stream, ReaderOptions?)"/>, which auto-detects the outer
/// compression and the TAR container beneath it) uniformly through SharpCompress's forward-only
/// <see cref="IReader"/>. Read-only itself: TAR.BZ2's writer goes through
/// <see cref="SharpCompressTarWriter"/> instead; 7z/RAR/TAR.XZ have no writer at all.
/// </summary>
public sealed class SharpCompressReader : IArchiveReader
{
    /// <summary>How often <see cref="ScanAsync"/> yields the thread while walking entries - see
    /// the call site for why this isn't every single entry.</summary>
    private const int YieldEveryNEntries = 64;

    private readonly string _archivePath;
    private readonly SharpCompressKind _kind;

    /// <summary>Initializes a new reader for the archive at <paramref name="archivePath"/> using the specified <paramref name="kind"/>.</summary>
    public SharpCompressReader(string archivePath, SharpCompressKind kind)
    {
        _archivePath = archivePath;
        _kind = kind;
    }

    /// <summary>SharpCompress readers are forward-only; random access is not supported.</summary>
    public bool SupportsRandomAccess => false;

    /// <summary>
    /// Reads the directory listing by scanning all entries on a thread pool thread.
    /// Returns an invalid directory if the file does not exist or is inaccessible.
    /// </summary>
    public Task<ArchiveDirectory> ReadDirectoryAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_archivePath))
            return Task.FromResult(new ArchiveDirectory(Array.Empty<ArchiveEntryRecord>(), isValid: false));

        // SharpCompress has no async I/O of its own; push the fully synchronous scan onto the
        // thread pool so callers awaiting this don't block their own thread on it.
        return Task.Run(() =>
        {
            try
            {
                return _kind is SharpCompressKind.SevenZip or SharpCompressKind.Rar
                    ? ReadDirectoryViaHeaderIndex(ct)
                    : ReadDirectoryViaSequentialScan(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogService.Warning($"Archive not accessible: {_archivePath}: {ex.Message}");
                return new ArchiveDirectory(Array.Empty<ArchiveEntryRecord>(), isValid: false);
            }
        }, ct);
    }

    /// <summary>
    /// 7z/RAR listing path: reads <see cref="IArchive.Entries"/> - metadata parsed from the
    /// archive's own header/central-directory-equivalent structure - rather than going through
    /// <see cref="OpenReader"/>'s forward-only <see cref="ExtractAllEntries"/> pipeline this reader
    /// otherwise uses uniformly for both listing and extraction. For a solid 7z/RAR, advancing
    /// <c>ExtractAllEntries()</c>'s <see cref="IReader.MoveToNextEntry"/> entry-by-entry to reach
    /// entry N forces LZMA/RAR decoding of every preceding entry's data too (they share one
    /// compressed block) - purely to read names for a panel listing that touches no content at
    /// all. <see cref="IArchive.Entries"/> never enters that pipeline.
    ///
    /// Entry order and index assignment are verified to match <see cref="ExtractAllEntries"/>'s
    /// own iteration order for 7z (both are exposed by SharpCompress's shared
    /// <c>AbstractArchive&lt;TEntry,TVolume&gt;</c> base, backed by the same parsed header) - this
    /// matters because <see cref="ArchiveEntryRecord.Index"/> assigned here is the same addressing
    /// token <see cref="ScanAsync"/>'s own sequential pass uses later to find "the Nth entry" for
    /// extraction.
    /// </summary>
    private ArchiveDirectory ReadDirectoryViaHeaderIndex(CancellationToken ct)
    {
        var options = new ReaderOptions { LeaveStreamOpen = true };
        using var fileStream = ArchiveFileRetry.OpenReadWithRetry(_archivePath);
        using IArchive archive = _kind == SharpCompressKind.SevenZip
            ? SevenZipArchive.OpenArchive(fileStream, options)
            : RarArchive.OpenArchive(fileStream, options);

        var entries = new List<ArchiveEntryRecord>();
        var index = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            entries.Add(ToRecord(entry, index++));
        }

        return new ArchiveDirectory(entries, isValid: true);
    }

    /// <summary>TAR.BZ2/TAR.XZ listing path: these have no header/central-directory structure to
    /// read independently of content - a TAR's own "header" is per-entry and embedded sequentially
    /// in the stream, so listing genuinely requires walking it via <see cref="OpenReader"/>'s
    /// forward-only reader, same as extraction does. Unlike 7z/RAR, there is no cheaper path.</summary>
    private ArchiveDirectory ReadDirectoryViaSequentialScan(CancellationToken ct)
    {
        var entries = new List<ArchiveEntryRecord>();
        using var fileStream = ArchiveFileRetry.OpenReadWithRetry(_archivePath);
        using var reader = OpenReader(fileStream, out var archive);
        try
        {
            var index = 0;
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                entries.Add(ToRecord(reader.Entry, index++));
            }
        }
        finally
        {
            archive?.Dispose();
        }

        return new ArchiveDirectory(entries, isValid: true);
    }

    /// <summary>Throws <see cref="NotSupportedException"/> — SharpCompress forward-only readers cannot open individual entries by index.</summary>
    public Stream OpenEntry(ArchiveEntryRecord entry) =>
        throw new NotSupportedException($"\"{_kind}\" archives do not support random-access entry opening; use ScanAsync instead.");

    /// <summary>Scans all entries sequentially via SharpCompress's <see cref="IReader"/>, yielding each as an <see cref="ArchiveEntryStream"/>.</summary>
    public async IAsyncEnumerable<ArchiveEntryStream> ScanAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var fileStream = ArchiveFileRetry.OpenReadWithRetry(_archivePath);
        IReader reader;
        IDisposable? archive;
        try
        {
            reader = OpenReader(fileStream, out archive);
        }
        catch
        {
            // OpenReader failed before returning anything to dispose in the finally below (e.g. a
            // corrupt or misdetected 7z/RAR) - without this, fileStream leaks on every such failure.
            fileStream.Dispose();
            throw;
        }

        try
        {
            var index = 0;
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();

                var record = ToRecord(reader.Entry, index++);
                // Encrypted entries fail with a raw crypto exception the moment their stream is
                // touched; links have no real file content to stream at all (only a target path).
                // Skip opening either here so extraction sees IsEncrypted/IsLink and can reject
                // cleanly instead (see UnpackOperation.ProcessRecordAsync).
                var content = record.IsDirectory || record.IsEncrypted || record.IsLink
                    ? Stream.Null
                    : new NonDisposingStream(reader.OpenEntryStream());

                yield return new ArchiveEntryStream(record, content);

                // SharpCompress is fully synchronous; yield the thread periodically (not on every
                // single entry - audit finding S8, DEBUG.md §0.1) so cancellation and the
                // caller's own async work get a fair chance to run. Every-entry yielding meant an
                // archive at UnpackLimits.MaxEntries (200,000) forced 200,000 thread-pool
                // suspend/resume round trips for no benefit over checking in every YieldEveryNEntries -
                // the fairness this exists for doesn't need finer granularity than that.
                if (index % YieldEveryNEntries == 0)
                    await Task.Yield();
            }
        }
        finally
        {
            reader.Dispose();
            archive?.Dispose();
            fileStream.Dispose();
        }
    }

    /// <summary>No persistent state to release; all resources are scoped to individual method calls.</summary>
    public void Dispose() { }

    /// <summary>
    /// Opens a SharpCompress <see cref="IReader"/> for the given <paramref name="stream"/> according to <see cref="_kind"/>.
    /// The optional <paramref name="archive"/> disposable (for archive-level resources) is returned via out parameter.
    /// </summary>
    private IReader OpenReader(Stream stream, out IDisposable? archive)
    {
        // LeaveStreamOpen = true prevents the reader/decompressor from disposing the underlying
        // stream on Dispose — the caller's finally block owns that cleanup. Without this, the
        // decompressor (GZipStream for TAR.BZ2, BZip2Stream, etc.) closes the file stream when
        // it is disposed, and then the finally block disposes it a second time (double-dispose).
        var options = new ReaderOptions { LeaveStreamOpen = true };
        switch (_kind)
        {
            case SharpCompressKind.SevenZip:
                var sevenZip = SevenZipArchive.OpenArchive(stream, options);
                archive = sevenZip;
                return sevenZip.ExtractAllEntries();
            case SharpCompressKind.Rar:
                var rar = RarArchive.OpenArchive(stream, options);
                archive = rar;
                return rar.ExtractAllEntries();
            case SharpCompressKind.TarBz2:
            case SharpCompressKind.TarXz:
                archive = null;
                return ReaderFactory.OpenReader(stream, options);
            default:
                throw new NotSupportedException($"Unhandled SharpCompress kind: {_kind}");
        }
    }

    /// <summary>Converts a SharpCompress <see cref="IEntry"/> to an <see cref="ArchiveEntryRecord"/>, stripping "./" prefixes.</summary>
    private static ArchiveEntryRecord ToRecord(IEntry entry, int index)
    {
        var name = (entry.Key ?? "").Replace('\\', '/');
        // Strip "./" prefix (e.g. from GNU tar or similar tools) — loop to handle doubled "././"
        while (name.StartsWith("./", StringComparison.Ordinal))
            name = name[2..];

        return new ArchiveEntryRecord
        {
            FullName = entry.IsDirectory ? name.TrimEnd('/') + "/" : name,
            IsDirectory = entry.IsDirectory,
            Size = entry.IsDirectory ? 0 : entry.Size,
            PackedSize = entry.CompressedSize,
            LastWriteTimeUtc = entry.LastModifiedTime?.ToUniversalTime() ?? default,
            Index = index,
            IsEncrypted = entry.IsEncrypted,
            // LinkTarget is non-null for symbolic/hard link entries (SharpCompress's one signal
            // for this across formats - there's no separate IsSymLink/IsHardLink flag).
            IsLink = !entry.IsDirectory && entry.LinkTarget != null
        };
    }
}
