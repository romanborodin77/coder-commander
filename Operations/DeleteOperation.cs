using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Операция удаления файлов/каталогов (рекурсивно для каталогов). С отчётом прогресса
/// по числу удалённых элементов и поддержкой отмены. / File/directory delete operation
/// (recursive for directories) with per-item progress and cancellation (ph0.2).
/// </summary>
public sealed class DeleteOperation : FileOperation
{
    private readonly IFileSystem _fs;
    private readonly List<string> _targets;
    private int _total;
    private int _done;
    private long _failed;
    private readonly List<string> _errors = new();

    /// <summary>Количество успешно удалённых элементов. / Number of successfully deleted items.</summary>
    public int Deleted => _done;

    /// <summary>Количество неудач. / Number of failures.</summary>
    public long Failed => _failed;

    /// <summary>Список описаний ошибок. / List of error descriptions.</summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>
    /// Создаёт операцию удаления.
    /// Creates a delete operation.
    /// </summary>
    /// <param name="fs">Файловая система. / File system.</param>
    /// <param name="targets">Удаляемые пути. / Paths to delete.</param>
    /// <param name="progress">Приёмник прогресса. / Progress sink.</param>
    public DeleteOperation(IFileSystem fs, IEnumerable<string> targets, IProgress<OperationProgress>? progress = null)
        : base(progress)
    {
        _fs = fs;
        _targets = new List<string>(targets);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        foreach (var t in _targets)
        {
            ct.ThrowIfCancellationRequested();
            // FIXED: Use IFileSystem.ExistsAsync instead of Directory.Exists/File.Exists
            // to support cross-VFS operations (SFTP, Cloud).
            var info = await _fs.GetFileInfoAsync(t, ct).ConfigureAwait(false);
            if (info is { IsDirectory: true })
            {
                try { await CountAsync(t, ct).ConfigureAwait(false); }
                catch { /* продолжаем подсчёт насколько Возможно / best-effort */ }
            }
            else if (info is not null) _total++;
        }

        foreach (var t in _targets)
        {
            ct.ThrowIfCancellationRequested();
            try { await DeleteEntryAsync(t, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _failed++;
                _errors.Add($"{t}: {ex.Message}");
                LogService.Error($"Delete failed for {t}: {ex.Message}", nameof(DeleteOperation), ex);
            }
        }
    }

    private async Task DeleteEntryAsync(string path, CancellationToken ct)
    {
        // FIXED: Use IFileSystem.GetFileInfoAsync instead of Directory.Exists for cross-VFS support.
        var info = await _fs.GetFileInfoAsync(path, ct).ConfigureAwait(false);
        if (info is { IsDirectory: true })
        {
            // FIXED: Symlink traversal — проверяем ReparsePoint перед рекурсией.
            // Если это symlink/junction, удаляем саму ссылку, а не её содержимое.
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                await _fs.DeleteAsync(path, recursive: false, ct).ConfigureAwait(false);
                _done++; ReportProgress(path);
                return;
            }
            // Сначала удаляем содержимое, затем сам каталог (рекурсивно).
            // Delete contents first, then the directory itself (recursive).
            try
            {
                // FIXED: Use IFileSystem.EnumerateAsync instead of Directory.EnumerateFiles
                // for cross-VFS support (SFTP, Cloud).
                var entries = await _fs.EnumerateAsync(path, includeHidden: true, ct).ConfigureAwait(false);
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry.IsDirectory) continue; // Handle subdirectories in second pass
                    try
                    {
                        await _fs.DeleteAsync(entry.FullPath, false, ct).ConfigureAwait(false);
                        _done++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _failed++;
                        _errors.Add($"{entry.FullPath}: {ex.Message}");
                        LogService.Error($"Delete failed for {entry.FullPath}: {ex.Message}", nameof(DeleteOperation), ex);
                    }
                    ReportProgress(entry.FullPath);
                }

                // Now handle subdirectories
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!entry.IsDirectory) continue;
                    try { await DeleteEntryAsync(entry.FullPath, ct).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _failed++;
                        _errors.Add($"{entry.FullPath}: {ex.Message}");
                        LogService.Error($"Delete dir failed for {entry.FullPath}: {ex.Message}", nameof(DeleteOperation), ex);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _failed++;
                _errors.Add($"{path}: {ex.Message}");
                LogService.Error($"Enum entries failed for {path}: {ex.Message}", nameof(DeleteOperation), ex);
            }
            await _fs.DeleteAsync(path, recursive: false, ct).ConfigureAwait(false);
            _done++; ReportProgress(path);
        }
        else
        {
            await _fs.DeleteAsync(path, recursive: false, ct).ConfigureAwait(false);
            _done++; ReportProgress(path);
        }
    }

    private void ReportProgress(string currentFile)
        => Report(new OperationProgress(currentFile, 0, 0, _done, _total, _done, _total));

    // FIXED: Use IFileSystem.EnumerateAsync instead of Directory.* for cross-VFS support.
    private async Task CountAsync(string path, CancellationToken ct)
    {
        var entries = await _fs.EnumerateAsync(path, includeHidden: true, ct).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            _total++;
            if (entry.IsDirectory)
                await CountAsync(entry.FullPath, ct).ConfigureAwait(false);
        }
    }
}
