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
/// <para><b>Single-flight (audit finding G043).</b> The cache stores the in-flight
/// <see cref="Task{TResult}"/> itself, published under the lock <em>before</em> the read starts -
/// two panels opening the same never-before-cached 3 GB <c>.tar.gz</c> at the same moment used to
/// each independently decompress the whole thing, because the old version released the lock before
/// awaiting. Now the second caller awaits the first caller's already-running task instead of
/// starting a redundant one. The file stamp is re-checked once the read completes: taking it only
/// once, before the read, meant an archive rewritten by another process mid-parse got cached
/// forever under a stamp that no longer matched its own content, since nothing ever invalidated it
/// afterwards - the very next access would keep serving that snapshot indefinitely. A mismatch
/// after the read drops the entry instead of caching it, so the next access re-reads for real.
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

    private readonly Dictionary<string, (Stamp Stamp, Task<ArchiveDirectory> DirectoryTask)> _cache = new(StringComparer.OrdinalIgnoreCase);

    // Most-recently-used at the front (First); least-recently-used at the back (Last), evicted
    // first when the cache is over MaxEntries. A parallel node-lookup dictionary is what makes
    // "move this key to the front on a hit" O(1) instead of an O(n) linear scan of the list.
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();

    private static Stamp? TryStat(string archivePath)
    {
        try
        {
            var info = new FileInfo(archivePath);
            return info.Exists ? new Stamp(info.Length, info.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

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
        var preStamp = TryStat(archivePath);
        if (preStamp is not { } stamp)
            return new ArchiveDirectory(Array.Empty<ArchiveEntryRecord>(), isValid: false);

        Task<ArchiveDirectory> task;
        var startedHere = false;

        lock (_lock)
        {
            if (_cache.TryGetValue(archivePath, out var cached) && cached.Stamp == stamp)
            {
                Touch(archivePath);
                task = cached.DirectoryTask;
            }
            else
            {
                // Publish the in-flight task under the lock, before the read actually starts, so a
                // concurrent caller for the same archive+stamp joins this same task instead of
                // launching its own redundant parse/decompression.
                task = Task.Run(() => read(ct), ct);
                _cache[archivePath] = (stamp, task);
                Touch(archivePath);
                EvictLeastRecentlyUsedIfOverCapacity();
                startedHere = true;
            }
        }

        try
        {
            var directory = await task.ConfigureAwait(false);

            if (startedHere)
            {
                // Re-stat after the read: if the archive changed while it was being parsed, the
                // snapshot just built no longer matches the file's current content and must not be
                // left cached under the pre-read stamp forever.
                var postStamp = TryStat(archivePath);
                if (postStamp != preStamp)
                    RemoveIfCurrent(archivePath, task);
            }

            return directory;
        }
        catch
        {
            if (startedHere)
                RemoveIfCurrent(archivePath, task);
            throw;
        }
    }

    /// <summary>Removes <paramref name="archivePath"/> from the cache only if it still points at
    /// <paramref name="expected"/> - guards against dropping a newer entry a subsequent write-path
    /// <see cref="Forget"/> or later read already replaced it with.</summary>
    private void RemoveIfCurrent(string archivePath, Task<ArchiveDirectory> expected)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(archivePath, out var current) && current.DirectoryTask == expected)
            {
                _cache.Remove(archivePath);
                if (_lruNodes.Remove(archivePath, out var node))
                    _lruOrder.Remove(node);
            }
        }
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
