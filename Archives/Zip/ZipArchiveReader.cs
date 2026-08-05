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

    /// <summary>Initializes a new reader for the ZIP archive at <paramref name="archivePath"/>.</summary>
    public ZipArchiveReader(string archivePath)
    {
        _archivePath = archivePath;
    }

    /// <summary>ZIP archives support random-access entry opening via the central directory.</summary>
    public bool SupportsRandomAccess => true;

    /// <summary>Reads the central directory of the ZIP archive and returns it as an <see cref="ArchiveDirectory"/>.</summary>
    public Task<ArchiveDirectory> ReadDirectoryAsync(CancellationToken ct = default)
    {
        var dir = ZipArchiveFileSystem.ReadDirectory(_archivePath);
        var isValid = !ReferenceEquals(dir, ZipArchiveFileSystem.ZipDirectory.Empty);
        var entries = dir.Entries.Select(ToRecord).ToList();
        return Task.FromResult(new ArchiveDirectory(entries, isValid));
    }

    /// <summary>Opens the entry at the given <paramref name="entry"/> index for reading.</summary>
    public Stream OpenEntry(ArchiveEntryRecord entry)
    {
        _zip ??= ZipFile.OpenRead(_archivePath);
        return _zip.Entries[entry.Index].Open();
    }

    /// <summary>Scans all entries sequentially, yielding each as an <see cref="ArchiveEntryStream"/>.</summary>
    public async IAsyncEnumerable<ArchiveEntryStream> ScanAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        foreach (var entry in dir.Entries)
        {
            ct.ThrowIfCancellationRequested();
            yield return new ArchiveEntryStream(entry, OpenEntry(entry));
        }
    }

    /// <summary>Converts a <see cref="ZipArchiveFileSystem.ZipEntryRecord"/> to an <see cref="ArchiveEntryRecord"/>.</summary>
    private static ArchiveEntryRecord ToRecord(ZipArchiveFileSystem.ZipEntryRecord e) => new()
    {
        FullName = e.FullName,
        IsDirectory = e.IsDirectory,
        Size = e.Size,
        PackedSize = e.CompressedSize,
        LastWriteTimeUtc = e.LastWriteTimeUtc,
        Index = e.Index
    };

    /// <summary>Releases the underlying <see cref="ZipArchive"/> if it was opened.</summary>
    public void Dispose() => _zip?.Dispose();
}
