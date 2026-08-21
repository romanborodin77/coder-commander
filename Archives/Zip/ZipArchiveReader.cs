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

        // ParseCentralDirectory can stop early on a truncated central directory while ZipArchive
        // applies its own, different tolerance rules - the two indices can desync. This is reached
        // from UnpackOperation outside any try, so an unchecked index here used to abort an entire
        // extraction with a raw ArgumentOutOfRangeException instead of failing just this one entry.
        // Every other index-into-Entries call site in this codebase (see
        // ZipArchiveFileSystem.FindEntry) already bounds-checks; this was the one gap.
        if (entry.Index >= 0 && entry.Index < _zip.Entries.Count)
            return _zip.Entries[entry.Index].Open();

        var byName = ZipArchiveFileSystem.FindEntry(_zip, _archivePath, entry.FullName)
            ?? throw new FileNotFoundException($"Entry not found in archive: {entry.FullName}");
        return byName.Open();
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
        Index = e.Index,
        IsEncrypted = e.IsEncrypted,
        IsLink = e.IsLink,
        Attributes = e.DosAttributes
    };

    /// <summary>Releases the underlying <see cref="ZipArchive"/> if it was opened.</summary>
    public void Dispose() => _zip?.Dispose();
}
