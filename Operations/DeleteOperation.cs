using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Delete operation: removes files and directories.
/// When UseRecycleBin is true, files are sent to Recycle Bin instead of permanent deletion.
/// </summary>
public sealed class DeleteOperation : FileOperation
{
    public override OperationType Type => OperationType.Delete;
    public override string Title => "Delete";

    private readonly IFileSystem _fs;
    private readonly IReadOnlyList<FileEntry> _files;

    /// <summary>When true, files are sent to the Recycle Bin instead of being permanently deleted.</summary>
    public bool UseRecycleBin { get; init; } = true;

    /// <summary>
    /// Invoked when the Recycle Bin failed for at least one file and permanent deletion of the
    /// files still on disk is the only remaining option. Receives their full paths; return true to
    /// proceed with permanent deletion, false to leave them untouched. May be called from a
    /// background thread. If not set, the fallback is skipped (fail-safe default: nothing is
    /// permanently deleted without explicit confirmation).
    /// </summary>
    public Func<IReadOnlyList<string>, bool>? ConfirmPermanentDelete { get; init; }

    private int _filesProcessed;
    private int _filesTotal;

    /// <summary>Creates a delete operation for the given files.</summary>
    public DeleteOperation(IFileSystem fs, IReadOnlyList<FileEntry> files)
    {
        _fs = fs;
        _files = files;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        _filesTotal = _files.Count;

        if (UseRecycleBin && _fs.Capabilities.HasFlag(FileSystemCapabilities.RecycleBin))
        {
            // Send all to Recycle Bin in one Shell operation (faster, single undo)
            var paths = _files.Select(f => f.FullPath).ToList();
            var ok = await Task.Run(() => RecycleBinHelper.MoveToRecycleBin(paths), ct).ConfigureAwait(false);
            if (!ok)
            {
                // The shell call failed for at least one file. Anything it did manage to move is
                // already gone from disk; only files that still exist need a decision - falling
                // back to permanent deletion of the whole batch would silently destroy files the
                // user only asked to move to the Recycle Bin. SHFileOperationW (used by
                // RecycleBinHelper) doesn't report per-file results - only the newer IFileOperation
                // COM API does, which would be a much larger rewrite - so an existence check is the
                // only way to tell which files survived; at least run the N checks concurrently
                // instead of one at a time.
                var existsChecks = await Task.WhenAll(_files.Select(f => _fs.ExistsAsync(f.FullPath, ct))).ConfigureAwait(false);
                var remaining = _files.Where((_, i) => existsChecks[i]).ToList();

                _filesProcessed = _filesTotal - remaining.Count;

                if (remaining.Count > 0)
                {
                    var remainingPaths = remaining.Select(f => f.FullPath).ToList();
                    var proceed = ConfirmPermanentDelete?.Invoke(remainingPaths) ?? false;
                    if (!proceed)
                    {
                        Report(new OperationProgress
                        {
                            Percent = _filesTotal > 0 ? _filesProcessed * 100 / _filesTotal : 0,
                            FilesProcessed = _filesProcessed,
                            FilesTotal = _filesTotal
                        });
                        throw new InvalidOperationException(
                            $"Recycle Bin failed for {remaining.Count} file(s); permanent deletion was not confirmed.");
                    }
                }

                foreach (var file in remaining)
                {
                    ct.ThrowIfCancellationRequested();
                    await _fs.DeleteAsync(file.FullPath, recursive: true, ct).ConfigureAwait(false);
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
            else
            {
                _filesProcessed = _filesTotal;
                Report(new OperationProgress
                {
                    Percent = 100,
                    FilesProcessed = _filesTotal,
                    FilesTotal = _filesTotal
                });
            }
        }
        else if (_fs is IBatchDeletableFileSystem batchFs)
        {
            // Use batch delete for file systems that support it (e.g., archives)
            var paths = _files.Select(f => f.FullPath).ToList();
            await batchFs.DeleteBatchAsync(paths, recursive: true, ct).ConfigureAwait(false);
            _filesProcessed = _filesTotal;
            Report(new OperationProgress
            {
                Percent = 100,
                FilesProcessed = _filesTotal,
                FilesTotal = _filesTotal
            });
        }
        else
        {
            foreach (var file in _files)
            {
                ct.ThrowIfCancellationRequested();
                await _fs.DeleteAsync(file.FullPath, recursive: true, ct).ConfigureAwait(false);
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
    }
}

/// <summary>
/// Wipe operation: overwrites files with zeroes before deletion (secure delete).
/// </summary>
public sealed class WipeOperation : FileOperation
{
    public override OperationType Type => OperationType.Wipe;
    public override string Title => "Wipe";

    private readonly IFileSystem _fs;
    private readonly IReadOnlyList<FileEntry> _files;
    private int _filesProcessed;
    private int _filesTotal;

    /// <summary>Creates a wipe operation for the given files.</summary>
    public WipeOperation(IFileSystem fs, IReadOnlyList<FileEntry> files)
    {
        _fs = fs;
        _files = files;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        // Fail-closed, inside the operation itself rather than trusting the caller to have gated
        // it first: every wipe primitive below (WipeDirectory/WipeFile) goes straight to
        // System.IO (DirectoryInfo/FileInfo/FileStream) against _fs's paths, bypassing IFileSystem
        // entirely. For a remote directory, DirectoryInfo(path).Exists is false for a path that
        // does exist on the server - not "wipe failed", but "nothing to wipe" - so WipeDirectory
        // silently reports success and the caller falls through to an ordinary recursive
        // IFileSystem.DeleteAsync: "secure overwrite" quietly degrades to a plain delete with no
        // overwrite pass and no visible sign anything was skipped, which is exactly the outcome
        // this class's own contract (see the comment below) says must never happen. Until now this
        // was caught only by MainViewModel checking PanelViewModel.IsVirtual before constructing a
        // WipeOperation at all - true for remote panels too, but by accident of what that flag
        // actually tests, not by a rule this class enforces, and only on that one call path.
        if (!_fs.Capabilities.HasFlag(FileSystemCapabilities.NativePaths))
            throw new NotSupportedException(
                $"Secure wipe requires direct disk access; \"{_fs.Name}\" does not provide it.");

        _filesTotal = _files.Count;

        // Wipe's entire point is guaranteeing the overwritten content is unrecoverable - deleting
        // a file whose overwrite pass actually failed (locked, permission denied, disk error)
        // would silently downgrade it to an ordinary, recoverable delete with no visible sign
        // anything went wrong. Failures are collected and left on disk unwiped-but-intact rather
        // than deleted, then reported as a clear operation failure at the end - the same pattern
        // PackOperation.RemoveSourcesAsync already uses for its own best-effort-with-a-loud-failure case.
        var failures = new List<string>();

        foreach (var file in _files)
        {
            ct.ThrowIfCancellationRequested();

            if (file.IsDirectory)
            {
                var directoryOk = await WipeDirectory(file.FullPath, ct, failures);
                if (directoryOk)
                    await _fs.DeleteAsync(file.FullPath, recursive: true, ct);
            }
            else if (await _fs.ExistsAsync(file.FullPath, ct))
            {
                if (await WipeFile(file.FullPath, ct))
                    await _fs.DeleteAsync(file.FullPath, recursive: true, ct);
                else
                    failures.Add(file.FullPath);
            }
            else
            {
                await _fs.DeleteAsync(file.FullPath, recursive: true, ct);
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

        if (failures.Count > 0)
            throw new IOException(
                $"Secure overwrite failed for {failures.Count} file(s) - left on disk unwiped " +
                $"rather than deleting them without one: {string.Join(", ", failures.Take(5))}" +
                (failures.Count > 5 ? $" and {failures.Count - 5} more" : ""));
    }

    /// <summary>Wipes every file under <paramref name="path"/>. Returns false (and appends every
    /// failed file's path to <paramref name="failures"/>) if any file's overwrite failed - the
    /// directory itself is then left in place by the caller instead of being deleted, since
    /// deleting it would remove the still-unwiped files along with the successfully wiped ones.</summary>
    private async Task<bool> WipeDirectory(string path, CancellationToken ct, List<string> failures)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists)
            return true;

        // ReparsePointGuard.SkipRecursion is not optional here. Without it this walk follows a
        // junction placed inside the directory being wiped and overwrites the linked target's file
        // contents with zeros - confirmed with a real junction before this fix. That is precisely
        // the kind of irreversible destruction a secure wipe promises, aimed at files the user
        // never selected.
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = ReparsePointGuard.SkipRecursion
        };

        var allOk = true;
        foreach (var file in dir.EnumerateFiles("*", options))
        {
            ct.ThrowIfCancellationRequested();
            if (!await WipeFile(file.FullName, ct))
            {
                failures.Add(file.FullName);
                allOk = false;
            }
        }
        return allOk;
    }

    /// <summary>Overwrites the file's content with zeros. Returns false (never throws, except
    /// for cancellation) if the overwrite failed for any reason - a locked file, a permission
    /// error, a disk I/O failure - so the caller can leave the file undeleted instead of quietly
    /// treating a failed wipe the same as a successful one.</summary>
    private static async Task<bool> WipeFile(string path, CancellationToken ct)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists)
                return true;

            // A reparse point (symlink/hardlink) opens the TARGET when FileStream follows it —
            // wiping would destroy the linked file's content, not the link itself. The same
            // class of bug already fixed for WipeDirectory (via ReparsePointGuard.SkipRecursion
            // in enumeration), but individual file selections bypass that guard.
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                LogService.Warning($"Wipe: refusing reparse point {path} — target would be destroyed");
                return false;
            }

            if ((fi.Attributes & FileAttributes.ReadOnly) != 0)
                fi.Attributes = FileAttributes.Normal;

            var length = fi.Length;
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough);
            var buffer = new byte[81920];
            Array.Clear(buffer);
            long written = 0;
            while (written < length)
            {
                ct.ThrowIfCancellationRequested();
                var toWrite = (int)Math.Min(buffer.Length, length - written);
                await fs.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, toWrite), ct);
                written += toWrite;
            }
            fs.Flush();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogService.Warning($"Wipe: overwrite failed for {path}: {ex.Message}", "FileOp");
            return false;
        }
    }
}
