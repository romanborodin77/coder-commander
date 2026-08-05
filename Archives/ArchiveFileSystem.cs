using CoderCommander.FileSystem;

namespace CoderCommander.Archives;

/// <summary>
/// Generic <see cref="IFileSystem"/> over any <see cref="IArchiveFormat"/> - the format-neutral
/// counterpart to <see cref="ZipArchiveFileSystem"/>, used by formats (TAR/TAR.GZ) that don't have
/// their own hand-written panel adapter. Directory listings are cached per archive file (see
/// <see cref="ArchiveDirectoryCache"/>), which matters most for sequential formats where rebuilding
/// the listing means decompressing the whole file. Every mutation goes through
/// <see cref="IArchiveFormat.OpenWrite"/>, so for rewrite-through formats each individual panel
/// operation (create folder, delete, drop a file in) rewrites the whole container - correct, if not
/// the fastest path for many small edits; bulk transfers go through
/// <see cref="CoderCommander.Operations.PackOperation"/>/<see cref="CoderCommander.Operations.UnpackOperation"/>
/// instead, which only open the writer once per operation.
/// </summary>
public sealed class ArchiveFileSystem : IFileSystem
{
    private static readonly ArchiveDirectoryCache Cache = new();

    private readonly IArchiveFormat _format;
    private readonly string _archivePath;

    /// <summary>
    /// Initializes a new instance backed by <paramref name="format"/> for the archive at <paramref name="archivePath"/>.
    /// </summary>
    public ArchiveFileSystem(IArchiveFormat format, string archivePath)
    {
        _format = format;
        _archivePath = archivePath;
    }

    /// <summary>Human-readable name shown in the panel title, e.g. <c>"ZIP"</c> or <c>"TAR.GZ"</c>.</summary>
    public string Name => _format.Id.ToUpperInvariant();

    /// <summary>Invalidates the cached directory listing for the given archive path, forcing a fresh read on the next access.</summary>
    public static void Forget(string archivePath) => Cache.Forget(archivePath);

    /// <summary>Reads the archive directory through the shared <see cref="ArchiveDirectoryCache"/>.</summary>
    private Task<ArchiveDirectory> ReadDirectoryAsync(CancellationToken ct) =>
        Cache.GetOrReadAsync(_archivePath, innerCt =>
        {
            using var reader = _format.OpenRead(_archivePath);
            return reader.ReadDirectoryAsync(innerCt);
        }, ct);

    /// <summary>
    /// Lists the immediate children of the directory at <paramref name="path"/> inside the archive.
    /// </summary>
    public async Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default)
    {
        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        return ArchiveTree.ListChildren(dir.Entries, _archivePath, ArchivePath.SplitPath(path).innerPath);
    }

    /// <summary>
    /// Lists all descendants (recursively) of the directory at <paramref name="path"/> inside the archive.
    /// </summary>
    public async Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default)
    {
        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        return ArchiveTree.ListDescendants(dir.Entries, _archivePath, ArchivePath.SplitPath(path).innerPath);
    }

    /// <summary>Returns a <see cref="FileEntry"/> for <paramref name="path"/>, or <c>null</c> if not found.</summary>
    public async Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default)
    {
        var innerPath = VfsPath.NormalizeInner(ArchivePath.SplitPath(path).innerPath);
        if (innerPath.Length == 0)
            return new FileEntry(ArchivePath.MakePath(_archivePath, ""), true);

        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        var entry = ArchiveTree.FindEntry(dir.Entries, innerPath);
        if (entry != null)
            return new FileEntry(ArchivePath.MakePath(_archivePath, innerPath), entry.IsDirectory, true, entry.Size, lastWriteTimeUtc: entry.LastWriteTimeUtc);

        if (ArchiveTree.HasDescendants(dir.Entries, innerPath))
            return new FileEntry(ArchivePath.MakePath(_archivePath, innerPath), true);

        return null;
    }

    /// <summary>Returns <c>true</c> if <paramref name="path"/> exists inside the archive.</summary>
    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
        await GetFileInfoAsync(path, ct).ConfigureAwait(false) != null;

    /// <summary>
    /// Opens the entry at <paramref name="path"/> for reading. The returned stream is backed by a temp
    /// file (auto-deleted on dispose) and supports random access regardless of the archive format's capabilities.
    /// Throws <see cref="NotSupportedException"/> for encrypted entries.
    /// </summary>
    public async Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        var innerPath = VfsPath.NormalizeInner(ArchivePath.SplitPath(path).innerPath);
        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        var target = ArchiveTree.FindEntry(dir.Entries, innerPath)
            ?? throw new FileNotFoundException($"Entry not found in archive: {innerPath}");

        if (target.IsEncrypted)
            throw new NotSupportedException($"Entry is encrypted and cannot be opened: {innerPath}");

        var tempFile = Path.GetTempFileName();
        try
        {
            using (var archiveReader = _format.OpenRead(_archivePath))
            {
                Stream? content = archiveReader.SupportsRandomAccess
                    ? archiveReader.OpenEntry(target)
                    : await FindContentSequentiallyAsync(archiveReader, target.Index, ct).ConfigureAwait(false);

                if (content == null)
                    throw new FileNotFoundException($"Entry not found in archive: {innerPath}");

                using (content)
                using (var fs = File.Create(tempFile))
                {
                    await content.CopyToAsync(fs, ct).ConfigureAwait(false);
                }
            }

            return new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.DeleteOnClose);
        }
        catch
        {
            try { File.Delete(tempFile); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>
    /// Scans the archive sequentially until the entry at <paramref name="wantedIndex"/> is found,
    /// returning its content stream or <c>null</c> if not found.
    /// </summary>
    private static async Task<Stream?> FindContentSequentiallyAsync(IArchiveReader reader, int wantedIndex, CancellationToken ct)
    {
        await foreach (var item in reader.ScanAsync(ct).ConfigureAwait(false))
        {
            if (item.Entry.Index == wantedIndex)
                return item.Content;
            item.Content.Dispose();
        }
        return null;
    }

    /// <summary>
    /// Copies the entry at <paramref name="source"/> inside the archive to <paramref name="destination"/> on the local
    /// filesystem (or into another archive if <paramref name="destination"/> is a VFS path).
    /// </summary>
    public async Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default)
    {
        using var content = await OpenReadAsync(source, ct).ConfigureAwait(false);

        if (VfsPath.IsArchive(destination))
        {
            var destArchivePath = VfsPath.GetArchiveFile(destination);
            var destFs = ArchiveFormatRegistry.CreateFileSystem(destArchivePath)
                ?? throw new NotSupportedException($"Unsupported archive format: {destArchivePath}");
            await destFs.CopyFromStreamAsync(destination, content, ct).ConfigureAwait(false);
        }
        else
        {
            var dir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            using var fs = new FileStream(destination, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
            await content.CopyToAsync(fs, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Moves the entry at <paramref name="source"/> to <paramref name="destination"/> (copy + delete).</summary>
    public async Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default)
    {
        await CopyFileAsync(source, destination, overwrite, ct).ConfigureAwait(false);
        await DeleteAsync(source, recursive: false, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the entry at <paramref name="path"/> (and all descendants if <paramref name="recursive"/> is <c>true</c>)
    /// by rewriting the archive through <see cref="IArchiveFormat.OpenWrite"/>.
    /// </summary>
    public async Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
    {
        var innerPath = VfsPath.NormalizeInner(ArchivePath.SplitPath(path).innerPath);
        if (innerPath.Length == 0)
            return;

        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        var prefix = innerPath + "/";
        var toDelete = dir.Entries
            .Where(e =>
            {
                var name = e.FullName.Replace('\\', '/').Trim('/');
                return string.Equals(name, innerPath, StringComparison.OrdinalIgnoreCase) ||
                       name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (toDelete.Count == 0)
            return;

        await using (var writer = _format.OpenWrite(_archivePath, new ArchiveWriteOptions()))
        {
            foreach (var entry in toDelete)
                writer.TryDeleteEntry(entry);
            await writer.CommitAsync(ct).ConfigureAwait(false);
        }

        Forget(_archivePath);
    }

    /// <summary>Creates a directory entry at <paramref name="path"/> inside the archive.</summary>
    public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        var innerPath = VfsPath.NormalizeInner(ArchivePath.SplitPath(path).innerPath);
        if (innerPath.Length == 0)
            return;

        await using (var writer = _format.OpenWrite(_archivePath, new ArchiveWriteOptions()))
        {
            writer.CreateDirectoryEntry(innerPath + "/", DateTime.UtcNow);
            await writer.CommitAsync(ct).ConfigureAwait(false);
        }

        Forget(_archivePath);
    }

    /// <summary>
    /// Writes the contents of <paramref name="source"/> to a new entry at <paramref name="destinationPath"/>
    /// inside the archive. Overwrites any existing entry with the same name.
    /// </summary>
    public async Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default)
    {
        var innerPath = VfsPath.NormalizeInner(ArchivePath.SplitPath(destinationPath).innerPath);
        if (innerPath.Length == 0)
            throw new IOException("Cannot write to the archive root without an entry name.");

        var tempFile = Path.GetTempFileName();
        try
        {
            using (var tempStream = File.Create(tempFile))
                await source.CopyToAsync(tempStream, ct).ConfigureAwait(false);

            var size = new FileInfo(tempFile).Length;
            var existing = await ReadDirectoryAsync(ct).ConfigureAwait(false);
            var clash = ArchiveTree.FindEntry(existing.Entries, innerPath);

            await using (var writer = _format.OpenWrite(_archivePath, new ArchiveWriteOptions()))
            {
                if (clash != null)
                    writer.TryDeleteEntry(clash);

                using (var readStream = File.OpenRead(tempFile))
                {
                    await writer.WriteFileAsync(innerPath, readStream, size, DateTime.UtcNow,
                        ArchiveCompressionSpec.Balanced, ct).ConfigureAwait(false);
                }

                await writer.CommitAsync(ct).ConfigureAwait(false);
            }

            Forget(_archivePath);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best effort */ }
        }
    }

    /// <summary>Archive formats do not support attribute changes; this is a no-op.</summary>
    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Drive space is not applicable to archives; always returns <c>(0, 0)</c>.</summary>
    public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default) => Task.FromResult((0L, 0L));

    /// <summary>Returns the archive root path in VFS form, e.g. <c>"archive.zip|"</c>.</summary>
    public string GetRootPath(string path) => ArchivePath.MakePath(_archivePath, "");
}
