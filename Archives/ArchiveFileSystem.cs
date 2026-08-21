using CoderCommander.FileSystem;
using CoderCommander.Services;
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
public sealed class ArchiveFileSystem : IFileSystem, IBatchReadableFileSystem, IBatchDeletableFileSystem
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
    ///
    /// Wrapped in <see cref="Task.Run{TResult}(Func{Task{TResult}}, CancellationToken)"/> rather
    /// than a plain <c>async</c> method - <see cref="ArchiveDirectoryCache.GetOrReadAsync"/> can
    /// resolve synchronously on a cache hit (the common case: the same snapshot is reused across
    /// every navigation inside the same archive at the same file stamp), and an `await` on an
    /// already-completed <see cref="Task"/> continues inline on the calling thread regardless of
    /// <c>ConfigureAwait</c>. Without this, every navigation inside a TAR/TAR.GZ/7z/RAR archive
    /// after the first ran its full tree-index build/query directly on the UI thread - the same
    /// reasoning <see cref="FileSystem.ZipArchiveFileSystem"/>'s own <c>Task.Run</c> wrappers
    /// document.
    /// </summary>
    public Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<FileEntry>>(async () =>
        {
            var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
            return ArchiveTree.ListChildren(dir, _archivePath, ArchivePath.SplitPath(path).innerPath);
        }, ct);

    /// <summary>
    /// Lists all descendants (recursively) of the directory at <paramref name="path"/> inside the archive.
    /// See <see cref="EnumerateAsync"/>'s doc comment for why this is wrapped in <see cref="Task.Run{TResult}(Func{Task{TResult}}, CancellationToken)"/>.
    /// </summary>
    public Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<FileEntry>>(async () =>
        {
            var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
            return ArchiveTree.ListDescendants(dir, _archivePath, ArchivePath.SplitPath(path).innerPath);
        }, ct);

    /// <summary>Returns a <see cref="FileEntry"/> for <paramref name="path"/>, or <c>null</c> if
    /// not found. See <see cref="EnumerateAsync"/>'s doc comment for why this is wrapped in
    /// <see cref="Task.Run{TResult}(Func{Task{TResult}}, CancellationToken)"/>.</summary>
    public Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default) =>
        Task.Run<FileEntry?>(async () =>
        {
            var innerPath = VfsPath.NormalizeInner(ArchivePath.SplitPath(path).innerPath);
            if (innerPath.Length == 0)
                return new FileEntry(ArchivePath.MakePath(_archivePath, ""), true);

            var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
            var entry = ArchiveTree.FindEntry(dir, innerPath);
            if (entry != null)
                return new FileEntry(ArchivePath.MakePath(_archivePath, innerPath), entry.IsDirectory, true, entry.Size, lastWriteTimeUtc: entry.LastWriteTimeUtc, attributes: entry.Attributes);

            if (ArchiveTree.HasDescendants(dir, innerPath))
                return new FileEntry(ArchivePath.MakePath(_archivePath, innerPath), true);

            return null;
        }, ct);

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

    /// <inheritdoc/>
    /// <remarks>
    /// Batch counterpart to <see cref="OpenReadAsync"/>'s sequential-format branch, generalized to
    /// many targets in one pass instead of one. For a source without random-access entry opening
    /// (<see cref="IArchiveReader.SupportsRandomAccess"/> false - TAR/TAR.GZ/7z/RAR), calling
    /// <see cref="OpenReadAsync"/> once per file each independently scans from the start of the
    /// archive and discards everything before the target - O(N x archive size) for N files. This
    /// opens the reader once and streams every requested entry out as it's encountered in a single
    /// forward pass instead - what <see cref="CoderCommander.Operations.UnpackOperation"/> already
    /// did correctly; <see cref="CoderCommander.Operations.CopyOperation"/> did not.
    /// </remarks>
    public async Task CopyManyToAsync(
        IReadOnlyList<(string SourcePath, string DestPath)> items,
        IFileSystem destFs,
        Func<string, long, CancellationToken, Task>? onFileCopied,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
            return;

        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);

        // Resolve every requested source path to its ArchiveEntryRecord.Index up front - the
        // token ScanAsync's own entries carry, matched below in O(1) per scanned entry instead of
        // re-deriving/re-comparing normalized paths. Keyed by Index (not SourcePath), so a
        // duplicate request for the same archive entry under two different destinations is still
        // possible to represent, if rare in practice: this codebase's own build-plan step
        // (CopyOperation.FlattenAsync) already dedupes by destination before this is ever called.
        var wanted = new Dictionary<int, (string SourcePath, string DestPath)>(items.Count);
        foreach (var (sourcePath, destPath) in items)
        {
            var innerPath = VfsPath.NormalizeInner(ArchivePath.SplitPath(sourcePath).innerPath);
            var entry = ArchiveTree.FindEntry(dir, innerPath);
            if (entry == null || entry.IsDirectory)
            {
                LogService.Warning($"CopyManyToAsync: entry not found for \"{sourcePath}\", skipping");
                continue;
            }
            wanted[entry.Index] = (sourcePath, destPath);
        }
        if (wanted.Count == 0)
            return;

        using var archiveReader = _format.OpenRead(_archivePath);
        var remaining = wanted.Count;
        await foreach (var item in archiveReader.ScanAsync(ct).ConfigureAwait(false))
        {
            if (remaining == 0)
            {
                // Every requested entry has already been found and written - stop scanning
                // instead of decompressing the rest of the archive for nothing. Disposing the
                // enumerator (loop exit) runs ScanAsync's own finally, releasing the reader/stream.
                item.Content.Dispose();
                break;
            }
            if (!wanted.TryGetValue(item.Entry.Index, out var target))
            {
                item.Content.Dispose();
                continue;
            }

            using (item.Content)
            {
                await destFs.CopyFromStreamAsync(target.DestPath, item.Content, ct).ConfigureAwait(false);
            }
            remaining--;

            if (onFileCopied != null)
                await onFileCopied(target.SourcePath, item.Entry.Size, ct).ConfigureAwait(false);
        }

        if (remaining > 0)
            LogService.Warning($"CopyManyToAsync: {remaining} requested entr{(remaining == 1 ? "y" : "ies")} not found while scanning {_archivePath}");
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

    /// <summary>
    /// Renames/moves an entry within this archive using ONE writer session and
    /// <see cref="IArchiveWriter.TryRenameEntry"/>, instead of the previous <see cref="CopyFileAsync"/>
    /// + <see cref="DeleteAsync"/> pairing (audit finding G054). For a <see cref="RewritingArchiveWriter"/>-
    /// backed format (TAR/TAR.GZ/TAR.BZ2 - the only writable formats this class serves; 7z/RAR/TAR.XZ
    /// have no writer at all and never reach here), each writer session is a full container rewrite,
    /// so the old two-session path cost two full rewrites for what an interactive F2 rename typically
    /// changes: one entry's name - <see cref="TryRenameEntry"/>'s own doc comment on
    /// <see cref="RewritingArchiveWriter"/> explains how the single <c>CopySurvivorsAsync</c> pass
    /// streams the renamed entry's content through unchanged, without <see cref="CopyFileAsync"/>'s
    /// separate extract-to-temp step at all.
    /// <para>
    /// Same "always the same archive" reasoning as <see cref="FileSystem.ZipArchiveFileSystem.MoveAsync"/>'s
    /// own doc comment: this class's <see cref="MoveAsync"/> is only ever reached with both
    /// <paramref name="source"/> and <paramref name="destination"/> inside this instance's own
    /// <c>_archivePath</c> - <see cref="Operations.MoveOperation.CanRenameInPlace"/> requires
    /// <c>ReferenceEquals(sourceFs, destFs)</c> for a provider with no <see cref="FileSystemCapabilities.NativePaths"/>.
    /// </para>
    /// </summary>
    public async Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default)
    {
        if (!_format.Capabilities.HasFlag(ArchiveCapabilities.AddEntries) ||
            !_format.Capabilities.HasFlag(ArchiveCapabilities.DeleteEntries))
            throw new NotSupportedException($"Archive format \"{_format.Id}\" does not support renaming entries.");

        var srcInner = VfsPath.NormalizeInner(ArchivePath.SplitPath(source).innerPath);
        var dstInner = VfsPath.NormalizeInner(ArchivePath.SplitPath(destination).innerPath);
        if (srcInner.Length == 0 || dstInner.Length == 0)
            throw new IOException("Cannot move to/from the archive root without an entry name.");

        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        var srcEntry = dir.Index.Navigate(srcInner)?.Entry
            ?? throw new FileNotFoundException($"Entry not found in archive: {srcInner}");

        if (srcEntry.IsDirectory)
        {
            // Directory.Move(path, path) semantics for a non-empty folder: refuse rather than
            // rename just the marker and orphan the children still filed under the old prefix -
            // same guard DeleteAsync(recursive:false) already applies, checked up front here so a
            // refusal leaves the archive completely untouched (see ZipArchiveFileSystem.MoveAsync's
            // identical guard for why checking AFTER a partial rename would be worse than refusing).
            var srcNode = dir.Index.Navigate(srcInner);
            if (srcNode != null && srcNode.Children.Count > 0)
                throw new IOException($"\"{srcInner}\" is not empty.");
        }

        var dstEntry = dir.Index.Navigate(dstInner)?.Entry;

        await using var writer = _format.OpenWrite(_archivePath, new ArchiveWriteOptions());
        if (dstEntry != null)
        {
            if (!overwrite)
                throw new IOException($"\"{dstInner}\" already exists.");
            writer.TryDeleteEntry(dstEntry);
        }

        var newName = srcEntry.IsDirectory ? dstInner.TrimEnd('/') + "/" : dstInner;
        writer.TryRenameEntry(srcEntry, newName);
        await writer.CommitAsync(ct).ConfigureAwait(false);

        Forget(_archivePath);
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

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="DeleteAsync"/> already opens one writer and commits once per *call* - the O(N)
    /// rewrite cost this exists to remove came from <c>Operations.DeleteOperation</c> calling
    /// <see cref="DeleteAsync"/> once per *selected item* when a filesystem doesn't implement
    /// <see cref="IBatchDeletableFileSystem"/>: deleting 50 selected files/folders meant 50
    /// separate writer sessions, each a full container rewrite for a
    /// <see cref="RewritingArchiveWriter"/>-backed format (TAR/TAR.GZ/TAR.BZ2). This resolves every
    /// requested path to its covering entries via <see cref="ArchiveDirectory.Index"/> (O(children)
    /// per path, not O(n) - see <see cref="Utils.PrefixTreeIndex{T}"/>), dedupes by
    /// <see cref="ArchiveEntryRecord.Index"/> so two overlapping requests (a folder and a file
    /// inside it, both selected) don't double-count, then commits once for the whole batch.
    /// </remarks>
    public async Task DeleteBatchAsync(IReadOnlyList<string> paths, bool recursive, CancellationToken ct = default)
    {
        if (!_format.Capabilities.HasFlag(ArchiveCapabilities.DeleteEntries))
            throw new NotSupportedException($"Archive format \"{_format.Id}\" does not support deleting entries.");
        if (paths.Count == 0)
            return;

        var dir = await ReadDirectoryAsync(ct).ConfigureAwait(false);
        var toDelete = new Dictionary<int, ArchiveEntryRecord>();

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var innerPath = VfsPath.NormalizeInner(ArchivePath.SplitPath(path).innerPath);
            if (innerPath.Length == 0)
                continue;

            var node = dir.Index.Navigate(innerPath);
            if (node == null)
                continue; // nothing at this path (already gone, or never existed) - nothing to delete

            // Directory.Delete(path, false) semantics - same guard as DeleteAsync.
            if (!recursive && node.Children.Count > 0)
                throw new IOException($"\"{innerPath}\" is not empty.");

            CollectEntriesForDeletion(node, toDelete);
        }

        if (toDelete.Count == 0)
            return;

        await using (var writer = _format.OpenWrite(_archivePath, new ArchiveWriteOptions()))
        {
            foreach (var entry in toDelete.Values)
                writer.TryDeleteEntry(entry);
            await writer.CommitAsync(ct).ConfigureAwait(false);
        }

        Forget(_archivePath);
    }

    private static void CollectEntriesForDeletion(
        Utils.PrefixTreeIndex<ArchiveEntryRecord>.Node node, Dictionary<int, ArchiveEntryRecord> result)
    {
        if (node.Entry is { } entry)
            result[entry.Index] = entry;
        foreach (var child in node.Children.Values)
            CollectEntriesForDeletion(child, result);
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
