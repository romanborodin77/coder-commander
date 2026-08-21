using System.IO.Compression;
using CoderCommander.FileSystem;
using CoderCommander.Utils;

namespace CoderCommander.Archives.Zip;

/// <summary>
/// Adapts <see cref="ZipArchiveFileSystem.OpenForUpdate"/> (which already handles exclusive-open
/// retry and legacy code-page write-back) to <see cref="IArchiveWriter"/>. ZIP's central directory
/// supports true in-place add/delete, so <see cref="Mode"/> is <see cref="ArchiveWriteMode.UpdateInPlace"/> -
/// but <see cref="CommitAsync"/> still matters: it's what tells the underlying
/// <see cref="ZipArchiveFileSystem.ZipUpdateSession"/> to actually replace the real archive on
/// <see cref="Dispose"/>, rather than discarding the staged changes (see the session's own doc
/// comment for why that distinction exists).
/// </summary>
public sealed class ZipArchiveWriter : IArchiveWriter
{
    private readonly string _archivePath;
    private readonly ZipArchiveFileSystem.ZipUpdateSession _session;
    private readonly Dictionary<int, ZipArchiveEntry> _byOriginalIndex;
    private ZipArchive _zip => _session.Archive;

    public ZipArchiveWriter(string archivePath, ArchiveWriteOptions options)
    {
        _archivePath = archivePath;
        _session = ZipArchiveFileSystem.OpenForUpdate(archivePath, options.PlannedEntryNames);

        // Snapshot index -> entry once, before any add/delete this session makes. The session's
        // temp copy is a byte-for-byte copy of the original archive (see ZipUpdateSession.Open),
        // so at this point _zip.Entries is in the same order as our own central-directory scan and
        // entry.Index (ArchiveEntryRecord.Index, the addressing token that exists precisely because
        // legacy code-page names don't round-trip) lines up. Held as object references rather than
        // re-derived indices so later deletes in this same session - which shrink and reindex
        // ZipArchive's own Entries collection - can't desync a later TryDeleteEntry call.
        //
        // ZipArchive.Entries throws NotSupportedException in Create mode (a brand-new archive,
        // which ZipUpdateSession.Open enters whenever the real file doesn't exist yet - the normal
        // case for Pack into a new .zip) - there's nothing to snapshot for an archive with no
        // entries yet, so this is skipped entirely rather than guarded per-access.
        _byOriginalIndex = _zip.Mode == ZipArchiveMode.Create
            ? new Dictionary<int, ZipArchiveEntry>()
            : new Dictionary<int, ZipArchiveEntry>(_zip.Entries.Count);
        if (_zip.Mode != ZipArchiveMode.Create)
            for (var i = 0; i < _zip.Entries.Count; i++)
                _byOriginalIndex[i] = _zip.Entries[i];
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
        // Address by Index first - the token that exists specifically because some formats'
        // entry names don't round-trip byte-for-byte through the encoding the directory listing
        // decoded them with (legacy code-page ZIP names), and the only way to tell two
        // identically-named entries apart (ZIP permits duplicate names; resolving by name alone
        // always found the first one, so deleting the second silently deleted the wrong entry).
        // Removed from the map on use so a duplicate delete request for the same index correctly
        // reports "already gone" instead of calling Delete() twice on the same ZipArchiveEntry.
        if (_byOriginalIndex.Remove(entry.Index, out var byIndex))
        {
            byIndex.Delete();
            return true;
        }

        var target = ZipArchiveFileSystem.FindEntry(_zip, _archivePath, entry.FullName);
        if (target == null)
            return false;

        target.Delete();
        return true;
    }

    /// <summary>Same "create new entry, copy the decompressed bytes across, delete the old one"
    /// shape as <see cref="FileSystem.ZipArchiveFileSystem"/>'s own in-session rename (see that
    /// class's <c>MoveAsync</c> doc comment for why <see cref="System.IO.Compression"/> offers no
    /// raw-compressed-bytes shortcut) - but within THIS writer's already-open session, so a caller
    /// batching several renames alongside adds/deletes still commits everything in one pass rather
    /// than opening a second session per rename.</summary>
    public bool TryRenameEntry(ArchiveEntryRecord entry, string newName)
    {
        var source = _byOriginalIndex.Remove(entry.Index, out var byIndex)
            ? byIndex
            : ZipArchiveFileSystem.FindEntry(_zip, _archivePath, entry.FullName);
        if (source == null)
            return false;

        var name = entry.IsDirectory ? newName.Replace('\\', '/').TrimEnd('/') + "/" : newName.Replace('\\', '/');
        var newEntry = _zip.CreateEntry(name, entry.IsDirectory ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
        newEntry.LastWriteTime = source.LastWriteTime;

        if (!entry.IsDirectory)
        {
            using var src = source.Open();
            using var dst = newEntry.Open();
            src.CopyTo(dst);
        }

        source.Delete();
        return true;
    }

    /// <summary>Marks the session ready to replace the real archive on <see cref="Dispose"/> -
    /// without this, an exception unwinding the caller's `await using (var writer = ...)` block
    /// partway through a write used to still commit whatever had been staged so far, silently
    /// replacing the user's original archive with a truncated one (see
    /// <see cref="ZipArchiveFileSystem.ZipUpdateSession.Commit"/>'s doc comment).</summary>
    public Task CommitAsync(CancellationToken ct = default)
    {
        _session.Commit();
        return Task.CompletedTask;
    }

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
