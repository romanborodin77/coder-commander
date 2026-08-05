using CoderCommander.FileSystem;

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

        if (UseRecycleBin && _fs is LocalFileSystem)
        {
            // Send all to Recycle Bin in one Shell operation (faster, single undo)
            var paths = _files.Select(f => f.FullPath).ToList();
            var ok = await Task.Run(() => RecycleBinHelper.MoveToRecycleBin(paths), ct).ConfigureAwait(false);
            if (!ok)
            {
                // The shell call failed for at least one file. Anything it did manage to move is
                // already gone from disk; only files that still exist need a decision - falling
                // back to permanent deletion of the whole batch would silently destroy files the
                // user only asked to move to the Recycle Bin.
                var remaining = new List<FileEntry>();
                foreach (var file in _files)
                {
                    if (await _fs.ExistsAsync(file.FullPath, ct).ConfigureAwait(false))
                        remaining.Add(file);
                }

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
        _filesTotal = _files.Count;

        foreach (var file in _files)
        {
            ct.ThrowIfCancellationRequested();

            if (file.IsDirectory)
            {
                await WipeDirectory(file.FullPath, ct);
            }
            else if (await _fs.ExistsAsync(file.FullPath, ct))
            {
                await WipeFile(file.FullPath, ct);
            }

            await _fs.DeleteAsync(file.FullPath, recursive: true, ct);
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

    private async Task WipeDirectory(string path, CancellationToken ct)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists)
            return;

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true
        };

        foreach (var file in dir.EnumerateFiles("*", options))
        {
            ct.ThrowIfCancellationRequested();
            await WipeFile(file.FullName, ct);
        }
    }

    private static async Task WipeFile(string path, CancellationToken ct)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists)
                return;

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
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }
    }
}
