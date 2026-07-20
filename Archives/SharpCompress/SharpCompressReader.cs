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
public enum SharpCompressKind { SevenZip, Rar, TarBz2, TarXz }

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
    private readonly string _archivePath;
    private readonly SharpCompressKind _kind;

    public SharpCompressReader(string archivePath, SharpCompressKind kind)
    {
        _archivePath = archivePath;
        _kind = kind;
    }

    public bool SupportsRandomAccess => false;

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

    public Stream OpenEntry(ArchiveEntryRecord entry) =>
        throw new NotSupportedException($"\"{_kind}\" archives do not support random-access entry opening; use ScanAsync instead.");

    public async IAsyncEnumerable<ArchiveEntryStream> ScanAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var fileStream = ArchiveFileRetry.OpenReadWithRetry(_archivePath);
        var reader = OpenReader(fileStream, out var archive);

        try
        {
            var index = 0;
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();

                var record = ToRecord(reader.Entry, index++);
                // Encrypted entries fail with a raw crypto exception the moment their stream is
                // touched; skip opening it here so extraction sees IsEncrypted and can reject
                // cleanly instead (see UnpackOperation.ProcessRecordAsync).
                var content = record.IsDirectory || record.IsEncrypted
                    ? Stream.Null
                    : new NonDisposingStream(reader.OpenEntryStream());

                yield return new ArchiveEntryStream(record, content);

                // SharpCompress is fully synchronous; yield the thread periodically so
                // cancellation and the caller's own async work get a fair chance to run.
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

    public void Dispose() { }

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

    private static ArchiveEntryRecord ToRecord(IEntry entry, int index)
    {
        var name = (entry.Key ?? "").Replace('\\', '/');
        // Strip "./" prefix (e.g. from GNU tar or similar tools)
        if (name.StartsWith("./"))
            name = name[2..];

        return new ArchiveEntryRecord
        {
            FullName = entry.IsDirectory ? name.TrimEnd('/') + "/" : name,
            IsDirectory = entry.IsDirectory,
            Size = entry.IsDirectory ? 0 : entry.Size,
            PackedSize = entry.CompressedSize,
            LastWriteTimeUtc = entry.LastModifiedTime?.ToUniversalTime() ?? default,
            Index = index,
            IsEncrypted = entry.IsEncrypted
        };
    }
}
