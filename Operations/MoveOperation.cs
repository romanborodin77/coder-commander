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
                                     (_sourceFs is LocalFileSystem && _destFs is LocalFileSystem);

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        _filesTotal = _files.Count;
        LogService.Info($"Move: starting with {_filesTotal} files, source={_sourceFs.Name}, dest={_destFs.Name}, CanRenameInPlace={CanRenameInPlace}");

        await _destFs.CreateDirectoryAsync(_destPath, ct).ConfigureAwait(false);

        foreach (var file in _files)
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

    /// <summary>Cross-provider (or cross-volume) fallback: stream everything over, then drop the original.</summary>
    private async Task TransferAndDeleteAsync(FileEntry file, string destFullPath, CancellationToken ct)
    {
        using var copy = new CopyOperation(_sourceFs, _destFs, new[] { file }, _sourceBasePath, _destPath, _options);
        copy.ProgressChanged += (_, p) => Report(p);
        await copy.ExecuteAsync(ct).ConfigureAwait(false);

        if (copy.State != OperationState.Completed)
        {
            if (copy.LastError != null)
                throw copy.LastError;
            ct.ThrowIfCancellationRequested();
            return;
        }

        if (await _destFs.ExistsAsync(destFullPath, ct).ConfigureAwait(false))
            await _sourceFs.DeleteAsync(file.FullPath, file.IsDirectory, ct).ConfigureAwait(false);
    }

    private async Task MoveEntryWithResolver(FileEntry file, string destFullPath, CancellationToken ct)
    {
        var actualDest = destFullPath;
        var overwrite = _options.Overwrite;

        if (await _destFs.ExistsAsync(destFullPath, ct).ConfigureAwait(false))
        {
            var action = OverwriteAction.Skip;
            string? newName = null;

            if (_options.OverwriteResolver != null)
            {
                var destInfo = await _destFs.GetFileInfoAsync(destFullPath, ct).ConfigureAwait(false);
                action = _options.OverwriteResolver(file.FullPath, destFullPath, file, destInfo, out newName);
            }
            else if (_options.Overwrite)
            {
                action = OverwriteAction.Overwrite;
            }

            if (action is OverwriteAction.Skip or OverwriteAction.SkipAll)
                return;

            // Resolver decision overrides the static flag.
            if (action is OverwriteAction.Overwrite or OverwriteAction.OverwriteAll or OverwriteAction.OverwriteOlder)
                overwrite = true;

            if (action == OverwriteAction.Rename && !string.IsNullOrEmpty(newName))
            {
                actualDest = VfsPath.ChangeName(destFullPath, newName);
                overwrite = false;
            }
        }

        await _sourceFs.MoveAsync(file.FullPath, actualDest, overwrite, ct).ConfigureAwait(false);

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
