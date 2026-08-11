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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogService.Warning($"Archive not accessible: {_archivePath}: {ex.Message}");
                return new ArchiveDirectory(Array.Empty<ArchiveEntryRecord>(), isValid: false);
            }
        }, ct);
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
        switch (_kind)
        {
            case SharpCompressKind.SevenZip:
                var sevenZip = SevenZipArchive.OpenArchive(stream, new ReaderOptions());
                archive = sevenZip;
                return sevenZip.ExtractAllEntries();
            case SharpCompressKind.Rar:
                var rar = RarArchive.OpenArchive(stream, new ReaderOptions());
                archive = rar;
                return rar.ExtractAllEntries();
            case SharpCompressKind.TarBz2:
            case SharpCompressKind.TarXz:
                archive = null;
                return ReaderFactory.OpenReader(stream, new ReaderOptions());
            default:
                throw new NotSupportedException($"Unhandled SharpCompress kind: {_kind}");
        }
    }

    /// <summary>Converts a SharpCompress <see cref="IEntry"/> to an <see cref="ArchiveEntryRecord"/>, stripping "./" prefixes.</summary>
    private static ArchiveEntryRecord ToRecord(IEntry entry, int index)
    {
        var name = (entry.Key ?? "").Replace('\\', '/');
        // Strip "./" prefix (e.g. from GNU tar or similar tools)
        if (name.StartsWith("./", StringComparison.Ordinal))
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
