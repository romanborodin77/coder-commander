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

    public ArchiveFileSystem(IArchiveFormat format, string archivePath)
    {
        _format = format;
        _archivePath = archivePath;
    }

    public string Name => _format.Id.ToUpperInvariant();

    public static void Forget(string archivePath) => Cache.Forget(archivePath);

    private Task<ArchiveDirectory> ReadDirectoryAsync(CancellationToken ct) =>
        Cache.GetOrReadAsync(_archivePath, innerCt =>
        {
            using var reader = _format.OpenRead(_archivePath);
            return reader.ReadDirectoryAsync(innerCt);
        }, ct);

    public async Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default)
    {
        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        return ArchiveTree.ListChildren(dir.Entries, _archivePath, ArchivePath.SplitPath(path).innerPath);
    }

    public async Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default)
    {
        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        return ArchiveTree.ListDescendants(dir.Entries, _archivePath, ArchivePath.SplitPath(path).innerPath);
    }

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

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
        await GetFileInfoAsync(path, ct).ConfigureAwait(false) != null;

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

    public async Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default)
    {
        await CopyFileAsync(source, destination, overwrite, ct).ConfigureAwait(false);
        await DeleteAsync(source, recursive: false, ct).ConfigureAwait(false);
    }

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

    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) => Task.CompletedTask;

    public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default) => Task.FromResult((0L, 0L));

    public string GetRootPath(string path) => ArchivePath.MakePath(_archivePath, "");
}
