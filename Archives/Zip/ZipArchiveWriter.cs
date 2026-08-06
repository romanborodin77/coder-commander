using System.IO.Compression;
using CoderCommander.FileSystem;
using CoderCommander.Utils;

namespace CoderCommander.Archives.Zip;

/// <summary>
/// Adapts <see cref="ZipArchiveFileSystem.OpenForUpdate"/> (which already handles exclusive-open
/// retry and legacy code-page write-back) to <see cref="IArchiveWriter"/>. ZIP's central directory
/// supports true in-place add/delete, so <see cref="Mode"/> is <see cref="ArchiveWriteMode.UpdateInPlace"/>
/// and <see cref="CommitAsync"/> has nothing to do beyond what <see cref="Dispose"/> already does.
/// </summary>
public sealed class ZipArchiveWriter : IArchiveWriter
{
    private readonly string _archivePath;
    private readonly ZipArchiveFileSystem.ZipUpdateSession _session;
    private ZipArchive _zip => _session.Archive;

    public ZipArchiveWriter(string archivePath, ArchiveWriteOptions options)
    {
        _archivePath = archivePath;
        _session = ZipArchiveFileSystem.OpenForUpdate(archivePath, options.PlannedEntryNames);
    }

    public ArchiveWriteMode Mode => ArchiveWriteMode.UpdateInPlace;

    public void CreateDirectoryEntry(string entryName, DateTime lastWriteTimeUtc)
    {
        var name = entryName.Replace('\\', '/').TrimEnd('/') + "/";
        if (ZipArchiveFileSystem.FindEntry(_zip, _archivePath, name) != null)
            return;

        var entry = _zip.CreateEntry(name);
        entry.LastWriteTime = ToEntryTimestamp(lastWriteTimeUtc);
    }

    public async Task WriteFileAsync(
        string entryName,
        Stream content,
        long size,
        DateTime lastWriteTimeUtc,
        ArchiveCompressionSpec compression,
        CancellationToken ct = default)
    {
        var name = entryName.Replace('\\', '/');
        var entry = _zip.CreateEntry(name, ToCompressionLevel(compression));
        entry.LastWriteTime = ToEntryTimestamp(lastWriteTimeUtc);

        var bufferSize = BufferSizing.ForSize(size);
        using var dst = entry.Open();
        await content.CopyToAsync(dst, bufferSize, ct).ConfigureAwait(false);
    }

    public bool TryDeleteEntry(ArchiveEntryRecord entry)
    {
        var target = ZipArchiveFileSystem.FindEntry(_zip, _archivePath, entry.FullName);
        if (target == null)
            return false;

        target.Delete();
        return true;
    }

    public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;

    private static CompressionLevel ToCompressionLevel(ArchiveCompressionSpec spec) => spec.Preset switch
    {
        CompressionPreset.Store => CompressionLevel.NoCompression,
        CompressionPreset.Fastest => CompressionLevel.Fastest,
        CompressionPreset.Maximum => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal
    };

    /// <summary>ZIP stores DOS timestamps, which only cover 1980-2107.</summary>
    private static DateTimeOffset ToEntryTimestamp(DateTime utc)
    {
        var local = utc == default ? DateTime.Now : utc.ToLocalTime();
        if (local.Year < 1980) local = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Local);
        if (local.Year > 2107) local = new DateTime(2107, 12, 31, 23, 59, 58, DateTimeKind.Local);
        return new DateTimeOffset(local);
    }

    public void Dispose()
    {
        _session.Dispose(); // flushes to the temp copy, then atomically replaces the original
        ZipArchiveFileSystem.Forget(_archivePath);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
