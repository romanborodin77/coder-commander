using CoderCommander.FileSystem;
using CoderCommander.Utils;

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

    /// <inheritdoc/>
    /// <remarks>
    /// Same as <see cref="FileSystem.ZipArchiveFileSystem"/> - a virtual tree inside one file, so
    /// never <see cref="FileSystemCapabilities.NativePaths"/>. This provider backs every format
    /// except ZIP, and it is precisely the provider the old <c>is ZipArchiveFileSystem</c> guards
    /// failed to recognise.
    ///
    /// <para>Unlike ZIP, the write-side flags are NOT unconditional: they're derived from
    /// <see cref="_format"/>'s own <see cref="ArchiveCapabilities"/>, because this is the one
    /// provider whose format can genuinely be read-only (7z, RAR, TAR.XZ - see each format's own
    /// <c>Capabilities</c>). This is what lets a caller (menu enablement, a pre-flight check before
    /// Pack) ask "can I write here" once, up front, instead of discovering it from the
    /// <see cref="NotSupportedException"/> that <see cref="DeleteAsync"/>/<see cref="CreateDirectoryAsync"/>/
    /// <see cref="CopyFromStreamAsync"/> still throw as the fail-closed lower layer - the flags don't
    /// replace those throws, they let most callers avoid reaching them.</para>
    /// </remarks>
    public FileSystemCapabilities Capabilities
    {
        get
        {
            var caps = FileSystemCapabilities.None;
            if (_format.Capabilities.HasFlag(ArchiveCapabilities.Create) ||
                _format.Capabilities.HasFlag(ArchiveCapabilities.AddEntries))
                caps |= FileSystemCapabilities.Writable;
            if (_format.Capabilities.HasFlag(ArchiveCapabilities.DeleteEntries))
                caps |= FileSystemCapabilities.Deletable;
            return caps;
        }
    }

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
        return ArchiveTree.ListChildren(dir, _archivePath, ArchivePath.SplitPath(path).innerPath);
    }

    /// <summary>
    /// Lists all descendants (recursively) of the directory at <paramref name="path"/> inside the archive.
    /// </summary>
    public async Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default)
    {
        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        return ArchiveTree.ListDescendants(dir, _archivePath, ArchivePath.SplitPath(path).innerPath);
    }

    /// <summary>Returns a <see cref="FileEntry"/> for <paramref name="path"/>, or <c>null</c> if not found.</summary>
    public async Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default)
    {
        var innerPath = VfsPath.NormalizeInner(ArchivePath.SplitPath(path).innerPath);
        if (innerPath.Length == 0)
            return new FileEntry(ArchivePath.MakePath(_archivePath, ""), true);

        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        var entry = ArchiveTree.FindEntry(dir, innerPath);
        if (entry != null)
            return new FileEntry(ArchivePath.MakePath(_archivePath, innerPath), entry.IsDirectory, true, entry.Size, lastWriteTimeUtc: entry.LastWriteTimeUtc);

        if (ArchiveTree.HasDescendants(dir, innerPath))
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
        var target = ArchiveTree.FindEntry(dir, innerPath)
            ?? throw new FileNotFoundException($"Entry not found in archive: {innerPath}");

        if (target.IsEncrypted)
            throw new NotSupportedException($"Entry is encrypted and cannot be opened: {innerPath}");

        var tempFile = TempFileNaming.NextTo(_archivePath, "extract");
        try
        {
            using (var archiveReader = _format.OpenRead(_archivePath))
            {
                if (archiveReader.SupportsRandomAccess)
                {
                    using var content = archiveReader.OpenEntry(target)
                        ?? throw new FileNotFoundException($"Entry not found in archive: {innerPath}");
                    using var fs = File.Create(tempFile);
                    await content.CopyToAsync(fs, ct).ConfigureAwait(false);
                }
                else
                {
                    // Sequential formats (TAR/7z/RAR): ScanAsync's finally block disposes the
                    // reader and its underlying stream when the enumerator is disposed — which
                    // happens on return/break from `await foreach`. Returning the content stream
                    // and copying AFTER the enumerator is disposed reads from a dead stream.
                    // Copy to temp INSIDE the loop while the reader is still alive.
                    var found = false;
                    await foreach (var item in archiveReader.ScanAsync(ct).ConfigureAwait(false))
                    {
                        if (item.Entry.Index != target.Index)
                        {
                            item.Content.Dispose();
                            continue;
                        }
                        using (item.Content)
                        using (var fs = File.Create(tempFile))
                            await item.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
                        found = true;
                        break;
                    }
                    if (!found)
                        throw new FileNotFoundException($"Entry not found in archive: {innerPath}");
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
        else if (RemotePath.IsRemote(destination))
        {
            // Archives/ has no reference to FileSystem.Remote's ConnectionManager (nor should it -
            // it doesn't know which live connection owns this path), so a remote destination here
            // cannot be routed correctly. The operations layer never actually reaches this branch -
            // Copy/MoveOperation always transfer via OpenReadAsync + destFs.CopyFromStreamAsync on
            // the DESTINATION's own IFileSystem instance, never through this method - but failing
            // loudly is what keeps that true: the previous code silently treated a
            // "sftp://host/x.txt" destination as a literal local Windows path (Path.GetDirectoryName
            // + new FileStream), which would have written a file with that exact string as its name
            // into whatever the current working directory happened to be, rather than failing.
            throw new NotSupportedException(
                $"\"{destination}\" is a remote path; use the destination's own IFileSystem instead of ArchiveFileSystem.CopyFileAsync.");
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
        if (!_format.Capabilities.HasFlag(ArchiveCapabilities.DeleteEntries))
            throw new NotSupportedException($"Archive format \"{_format.Id}\" does not support deleting entries.");

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

        if (!recursive)
        {
            // Directory.Delete(path, false) semantics: refuse a non-empty directory instead of
            // silently deleting its contents. `recursive: false` used to be ignored entirely here,
            // so MoveOperation's own "clean up now-empty directories after a move" pass - which
            // calls DeleteAsync(dir, recursive: false) and relies on a non-empty directory failing
            // rather than being wiped - silently deleted directories that still had files left in
            // them under a TAR/TAR.GZ/7z/RAR source. A lone file target is unaffected: nothing
            // else in toDelete can match its own prefix, so hasDescendants is false for it.
            var hasDescendants = toDelete.Any(e =>
                !string.Equals(e.FullName.Replace('\\', '/').Trim('/'), innerPath, StringComparison.OrdinalIgnoreCase));
            if (hasDescendants)
                throw new IOException($"\"{innerPath}\" is not empty.");

            toDelete = toDelete
                .Where(e => string.Equals(e.FullName.Replace('\\', '/').Trim('/'), innerPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (toDelete.Count == 0)
                return; // a purely synthetic empty directory (no marker entry of its own) - nothing to delete
        }

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
        if (!_format.Capabilities.HasFlag(ArchiveCapabilities.AddEntries))
            throw new NotSupportedException($"Archive format \"{_format.Id}\" does not support adding entries.");

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
        if (!_format.Capabilities.HasFlag(ArchiveCapabilities.Create))
            throw new NotSupportedException($"Archive format \"{_format.Id}\" is read-only and does not support writing.");

        var innerPath = VfsPath.NormalizeInner(ArchivePath.SplitPath(destinationPath).innerPath);
        if (innerPath.Length == 0)
            throw new IOException("Cannot write to the archive root without an entry name.");

        var existing = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        var clash = ArchiveTree.FindEntry(existing, innerPath);

        // A directory occupying this exact name - explicitly (a real directory-marker entry) or
        // implicitly (child entries like "name/file.txt" with no marker of their own, which many
        // ZIP tools never write) - must reject the write, the same way LocalFileSystem's
        // File.Move fails loud when the destination is an existing directory. Without this,
        // FindEntry finds no exact clash to delete in the implicit case (nothing is named
        // exactly "name"), and a brand-new file entry gets written right alongside the
        // directory's own children - the same path ends up used as both a file and a directory
        // at once, a structurally inconsistent archive with no error shown to the user.
        if ((clash != null && clash.IsDirectory) ||
            (clash == null && ArchiveTree.HasDescendants(existing, innerPath)))
            throw new IOException($"Cannot overwrite \"{innerPath}\": a directory with that name already exists in the archive.");

        var tempFile = TempFileNaming.NextTo(_archivePath, "stage");
        try
        {
            using (var tempStream = File.Create(tempFile))
                await source.CopyToAsync(tempStream, ct).ConfigureAwait(false);

            var size = new FileInfo(tempFile).Length;

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
