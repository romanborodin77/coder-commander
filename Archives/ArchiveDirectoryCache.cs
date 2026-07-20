namespace CoderCommander.Archives;

/// <summary>
/// Generic file-stamp-keyed cache for archive directory listings, mirroring the pattern
/// <c>ZipArchiveFileSystem</c> uses internally: keyed by (length, last-write-ticks) so any
/// external modification of the archive invalidates the cache automatically. Particularly useful
/// for sequential formats (TAR.GZ) where rebuilding the listing means decompressing the whole file.
/// </summary>
public sealed class ArchiveDirectoryCache
{
    private readonly record struct Stamp(long Length, long Ticks);

    private readonly Dictionary<string, (Stamp Stamp, ArchiveDirectory Directory)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public async Task<ArchiveDirectory> GetOrReadAsync(
        string archivePath,
        Func<CancellationToken, Task<ArchiveDirectory>> read,
        CancellationToken ct = default)
    {
        Stamp stamp;
        try
        {
            var info = new FileInfo(archivePath);
            if (!info.Exists)
                return new ArchiveDirectory(Array.Empty<ArchiveEntryRecord>(), isValid: false);
            stamp = new Stamp(info.Length, info.LastWriteTimeUtc.Ticks);
        }
        catch (Exception)
        {
            return new ArchiveDirectory(Array.Empty<ArchiveEntryRecord>(), isValid: false);
        }

        lock (_lock)
        {
            if (_cache.TryGetValue(archivePath, out var cached) && cached.Stamp == stamp)
                return cached.Directory;
        }

        var directory = await read(ct).ConfigureAwait(false);

        lock (_lock)
        {
            _cache[archivePath] = (stamp, directory);
        }

        return directory;
    }

    public void Forget(string archivePath)
    {
        lock (_lock)
        {
            _cache.Remove(archivePath);
        }
    }
}
