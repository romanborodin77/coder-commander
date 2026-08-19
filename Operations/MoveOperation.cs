using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Move/rename operation: moves files and directories, falling back to copy+delete when needed.
/// </summary>
public sealed class MoveOperation : FileOperation
{
    public override OperationType Type => OperationType.Move;
    public override string Title => "Move";

    private readonly IFileSystem _sourceFs;
    private readonly IFileSystem _destFs;
    private readonly IReadOnlyList<FileEntry> _files;
    private readonly string _sourceBasePath;
    private readonly string _destPath;
    private readonly TransferOptions _options;

    private int _filesProcessed;
    private int _filesTotal;

    /// <summary>Creates a move operation from <paramref name="sourceFs"/> to <paramref name="destFs"/>.</summary>
    public MoveOperation(
        IFileSystem sourceFs,
        IFileSystem destFs,
        IReadOnlyList<FileEntry> files,
        string sourceBasePath,
        string destPath,
        TransferOptions? options = null)
    {
        _sourceFs = sourceFs;
        _destFs = destFs;
        _files = files;
        _sourceBasePath = sourceBasePath;
        _destPath = destPath;
        _options = options ?? new TransferOptions();
    }

    /// <summary>A native rename is only possible while both sides are served by the same provider.</summary>
    private bool CanRenameInPlace => ReferenceEquals(_sourceFs, _destFs) ||
                                     (_sourceFs.Capabilities.HasFlag(FileSystemCapabilities.NativePaths) &&
                                      _destFs.Capabilities.HasFlag(FileSystemCapabilities.NativePaths));

    /// <summary>Drops any selected entry that's physically nested inside another selected
    /// directory - moving that directory already relocates it, so processing it again separately
    /// would only find its source gone. VfsPath.IsDescendantOf, not a bare
    /// Path.DirectorySeparatorChar prefix test: a remote or archive path never contains '\', so the
    /// old prefix test silently failed to dedup a nested selection on those filesystems, and the
    /// nested entry was processed a second time after its containing folder's move had already
    /// relocated it - failing, not corrupting data, but still the exact case this method exists to
    /// prevent.</summary>
    internal static List<FileEntry> RemoveEntriesInsideSelectedDirectories(IReadOnlyList<FileEntry> files)
    {
        var directories = files.Where(f => f.IsDirectory).ToList();
        return files.Where(f => !directories.Any(d =>
                !ReferenceEquals(d, f) && VfsPath.IsDescendantOf(d.FullPath, f.FullPath)))
            .ToList();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        // Unlike CopyOperation (which flattens into individual file-level plan entries and dedups
        // there), a directory selected for Move is moved whole via a single Directory.Move/MoveAsync
        // call - so if the same selection also separately lists a file inside that directory (Flat
        // View allows selecting both a folder and a file already nested inside it), the directory
        // move already relocates that file, and the redundant separate entry then fails outright
        // (its source no longer exists) once the loop reaches it. That failure used to propagate
        // out and mark the WHOLE move Failed, even though everything the user selected had, in
        // fact, already been moved successfully.
        var files = RemoveEntriesInsideSelectedDirectories(_files);

        _filesTotal = files.Count;
        LogService.Info($"Move: starting with {_filesTotal} files, source={_sourceFs.Name}, dest={_destFs.Name}, CanRenameInPlace={CanRenameInPlace}");

        await _destFs.CreateDirectoryAsync(_destPath, ct).ConfigureAwait(false);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var destFullPath = VfsPath.Combine(_destPath, VfsPath.GetRelative(_sourceBasePath, file.FullPath));
            LogService.Info($"Move: processing {file.FullPath} -> {destFullPath}");

            var renamed = false;
            if (CanRenameInPlace)
            {
                try
                {
                    await MoveEntryWithResolver(file, destFullPath, ct).ConfigureAwait(false);
                    renamed = true;
                    LogService.Info($"Move: renamed {file.Name}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogService.Warning($"Move: rename failed for {file.FullPath}, falling back to copy: {ex.Message}");
                }
            }

            if (!renamed)
            {
                LogService.Info($"Move: using TransferAndDelete for {file.Name}");
                await TransferAndDeleteAsync(file, destFullPath, ct).ConfigureAwait(false);
            }

            _filesProcessed++;
            Report(new OperationProgress
            {
                Percent = _filesTotal > 0 ? _filesProcessed * 100 / _filesTotal : 0,
                CurrentFile = file.Name,
                FilesProcessed = _filesProcessed,
                FilesTotal = _filesTotal
            });
        }
    }

    /// <summary>Cross-provider (or cross-volume) fallback: stream everything over, then drop only
    /// what was actually written.</summary>
    private async Task TransferAndDeleteAsync(FileEntry file, string destFullPath, CancellationToken ct)
    {
        using var copy = new CopyOperation(_sourceFs, _destFs, new[] { file }, _sourceBasePath, _destPath, _options);
        copy.ProgressChanged += (_, p) => Report(p);
        await copy.ExecuteAsync(ct).ConfigureAwait(false);

        if (copy.State != OperationState.Completed)
        {
            // Cancellation: don't delete source files. The user pressed Cancel expecting the
            // source to be preserved — deleting already-copied files from the source turns a
            // cancellation into a partial move, which is surprising and destructive.
            if (ct.IsCancellationRequested)
            {
                if (copy.LastError != null)
                    throw copy.LastError;
                ct.ThrowIfCancellationRequested();
            }

            // Partial copy (non-cancellation failure): some files landed at the destination,
            // others failed. For a directory move, deleting only what WrittenPaths confirms
            // made it across completes as much of the move as possible. For a single-file move
            // with a failure, nothing was written, so rethrowing preserves the source untouched.
            if (file.IsDirectory && copy.WrittenPaths.Count > 0)
            {
                foreach (var writtenPath in copy.WrittenPaths)
                {
                    try { await _sourceFs.DeleteAsync(writtenPath, recursive: false, ct).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogService.Warning($"Move: cannot remove source {writtenPath}: {ex.Message}");
                    }
                }
                await CleanupEmptyDirectoriesAsync(file.FullPath, ct).ConfigureAwait(false);
            }

            if (copy.LastError != null)
                throw copy.LastError;
            ct.ThrowIfCancellationRequested();
            return;
        }

        if (!file.IsDirectory)
        {
            // Checking "does the destination path exist" (the old check) is wrong: it's true
            // even when CopyOperation Skipped this file because something already occupied that
            // destination path - exactly the conflict that produced the Skip in the first place.
            // That silently deleted a source file that was never actually copied anywhere.
            if (copy.WrittenPaths.Contains(file.FullPath))
                await _sourceFs.DeleteAsync(file.FullPath, recursive: false, ct).ConfigureAwait(false);
            return;
        }

        // Directory move: remove exactly the files CopyOperation actually wrote, leaving
        // anything skipped untouched on the source side - same "delete only what really made it
        // across" philosophy as PackOperation.RemoveSourcesAsync's per-file fallback.
        foreach (var writtenPath in copy.WrittenPaths)
        {
            try { await _sourceFs.DeleteAsync(writtenPath, recursive: false, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogService.Warning($"Move: cannot remove source {writtenPath}: {ex.Message}");
            }
        }

        await CleanupEmptyDirectoriesAsync(file.FullPath, ct).ConfigureAwait(false);
    }

    /// <summary>Best-effort removal of directories left empty by the per-file deletes above
    /// (deepest first), including the moved root itself if everything under it made it across.
    /// A directory that still contains a skipped file fails to delete and is correctly left in
    /// place - not forced away with a recursive delete.</summary>
    private async Task CleanupEmptyDirectoriesAsync(string rootPath, CancellationToken ct)
    {
        try
        {
            var descendants = await _sourceFs.EnumerateDeepAsync(rootPath, includeHidden: true, ct).ConfigureAwait(false);
            foreach (var dir in descendants.Where(d => d.IsDirectory).OrderByDescending(d => d.FullPath.Length))
            {
                try { await _sourceFs.DeleteAsync(dir.FullPath, recursive: false, ct).ConfigureAwait(false); }
                catch { /* not empty, or otherwise busy - leave it */ }
            }
            try { await _sourceFs.DeleteAsync(rootPath, recursive: false, ct).ConfigureAwait(false); }
            catch { /* not empty - some skipped file remains beneath it, correctly left in place */ }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Warning($"Move: cannot clean up empty directories under {rootPath}: {ex.Message}");
        }
    }

    private async Task MoveEntryWithResolver(FileEntry file, string destFullPath, CancellationToken ct)
    {
        var resolution = await ConflictResolver.ResolveAsync(_destFs, file.FullPath, destFullPath, file, _options, ct).ConfigureAwait(false);
        if (!resolution.Proceed)
            return;
        var actualDest = resolution.TargetPath;

        await _sourceFs.MoveAsync(file.FullPath, actualDest, resolution.Overwrite, ct).ConfigureAwait(false);

        if (_options.CopyAttributes && file.Attributes != default)
        {
            try
            {
                await _destFs.SetAttributesAsync(actualDest, file.Attributes, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogService.Warning($"Move: cannot set attributes on {actualDest}: {ex.Message}");
            }
        }

        if (_options.CopyTimestamps)
            CopyOperation.ApplyTimestamps(_destFs, actualDest, file);
    }
}
