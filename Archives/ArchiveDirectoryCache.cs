namespace CoderCommander.Archives;

/// <summary>
/// Generic file-stamp-keyed cache for archive directory listings, mirroring the pattern
/// <c>ZipArchiveFileSystem</c> uses internally: keyed by (length, last-write-ticks) so any
/// external modification of the archive invalidates the cache automatically. Particularly useful
/// for sequential formats (TAR.GZ) where rebuilding the listing means decompressing the whole file.
///
/// <para><b>Bounded, LRU-evicted (audit finding S4).</b> A single <c>static readonly</c> instance
/// (<see cref="ArchiveFileSystem"/>) lives for the whole process - before this, entries were added
/// on every distinct archive path ever browsed and never removed except by an explicit
/// <see cref="Forget"/> call from a write path. A stale entry (its archive since deleted, moved, or
/// simply never revisited) is not a correctness problem - the stamp check means it can never be
/// served as if current - but it sat in the dictionary forever regardless, holding its full
/// <see cref="ArchiveDirectory"/> (every entry of a possibly large archive) for no reason. A
/// session that browses many distinct archives (a large source tree with archived releases, a
/// downloads folder, an automated workflow) grew this without bound for the life of the process.
/// </para>
/// </summary>
public sealed class ArchiveDirectoryCache
{
    /// <summary>Distinct archive paths kept at once, evicting the least-recently-used past this.
    /// Generous for how many archives a session plausibly has open or recently browsed
    /// simultaneously, while still bounding worst-case memory for a long session that touches many
    /// different archives one after another.</summary>
    public const int MaxEntries = 64;

    private readonly record struct Stamp(long Length, long Ticks);

    private readonly Dictionary<string, (Stamp Stamp, ArchiveDirectory Directory)> _cache = new(StringComparer.OrdinalIgnoreCase);

    // Most-recently-used at the front (First); least-recently-used at the back (Last), evicted
    // first when the cache is over MaxEntries. A parallel node-lookup dictionary is what makes
    // "move this key to the front on a hit" O(1) instead of an O(n) linear scan of the list.
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();

    /// <summary>
    /// Returns a cached <see cref="ArchiveDirectory"/> for <paramref name="archivePath"/> if the file stamp
    /// (length + last-write time) matches, or invokes <paramref name="read"/> to build a fresh listing.
    /// Returns an invalid directory if the file does not exist or cannot be accessed.
    /// </summary>
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
            {
                Touch(archivePath);
                return cached.Directory;
            }
        }

        var directory = await Task.Run(() => read(ct), ct).ConfigureAwait(false);

        lock (_lock)
        {
            _cache[archivePath] = (stamp, directory);
            Touch(archivePath);
            EvictLeastRecentlyUsedIfOverCapacity();
        }

        return directory;
    }

    /// <summary>Removes the cached directory listing for <paramref name="archivePath"/>, forcing a fresh read on the next access.</summary>
    public void Forget(string archivePath)
    {
        lock (_lock)
        {
            _cache.Remove(archivePath);
            if (_lruNodes.Remove(archivePath, out var node))
                _lruOrder.Remove(node);
        }
    }

    /// <summary>Moves <paramref name="archivePath"/> to the most-recently-used end. Must be called
    /// with <see cref="_lock"/> already held.</summary>
    private void Touch(string archivePath)
    {
        if (_lruNodes.TryGetValue(archivePath, out var existing))
            _lruOrder.Remove(existing);
        _lruNodes[archivePath] = _lruOrder.AddFirst(archivePath);
    }

    /// <summary>Must be called with <see cref="_lock"/> already held.</summary>
    private void EvictLeastRecentlyUsedIfOverCapacity()
    {
        while (_cache.Count > MaxEntries && _lruOrder.Last is { } lru)
        {
            _cache.Remove(lru.Value);
            _lruNodes.Remove(lru.Value);
            _lruOrder.RemoveLast();
        }
    }
}
