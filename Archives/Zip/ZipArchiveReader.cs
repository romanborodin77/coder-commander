using System.IO.Compression;
using System.Runtime.CompilerServices;
using CoderCommander.FileSystem;

namespace CoderCommander.Archives.Zip;

/// <summary>
/// Adapts <see cref="ZipArchiveFileSystem"/>'s existing central-directory parsing
/// (<see cref="ZipArchiveFileSystem.ReadDirectory"/>) and entry addressing (by
/// <see cref="ZipArchiveFileSystem.ZipEntryRecord.Index"/>, same as
/// <c>UnpackOperation</c> already does) to <see cref="IArchiveReader"/>. Holds one
/// <see cref="ZipArchive"/> open for the reader's lifetime so repeated <see cref="OpenEntry"/>
/// calls are cheap.
/// </summary>
public sealed class ZipArchiveReader : IArchiveReader
{
    private readonly string _archivePath;
    private ZipArchive? _zip;

    public ZipArchiveReader(string archivePath)
    {
        _archivePath = archivePath;
    }

    public bool SupportsRandomAccess => true;

    public Task<ArchiveDirectory> ReadDirectoryAsync(CancellationToken ct = default)
    {
        var dir = ZipArchiveFileSystem.ReadDirectory(_archivePath);
        var isValid = !ReferenceEquals(dir, ZipArchiveFileSystem.ZipDirectory.Empty);
        var entries = dir.Entries.Select(ToRecord).ToList();
        return Task.FromResult(new ArchiveDirectory(entries, isValid));
    }

    public Stream OpenEntry(ArchiveEntryRecord entry)
    {
        _zip ??= ZipFile.OpenRead(_archivePath);
        return _zip.Entries[entry.Index].Open();
    }

    public async IAsyncEnumerable<ArchiveEntryStream> ScanAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        foreach (var entry in dir.Entries)
        {
            ct.ThrowIfCancellationRequested();
            yield return new ArchiveEntryStream(entry, OpenEntry(entry));
        }
    }

    private static ArchiveEntryRecord ToRecord(ZipArchiveFileSystem.ZipEntryRecord e) => new()
    {
        FullName = e.FullName,
        IsDirectory = e.IsDirectory,
        Size = e.Size,
        PackedSize = e.CompressedSize,
        LastWriteTimeUtc = e.LastWriteTimeUtc,
        Index = e.Index
    };

    public void Dispose() => _zip?.Dispose();
}
