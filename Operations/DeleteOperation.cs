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
            if (Directory.Exists(t))
            {
                try { Count(t, ct); }
                catch { /* продолжаем подсчёт насколько возможно / best-effort */ }
            }
            else if (File.Exists(t)) _total++;
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
        if (Directory.Exists(path))
        {
            // Сначала удаляем содержимое, затем сам каталог (рекурсивно).
            // Delete contents first, then the directory itself (recursive).
            try
            {
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        await _fs.DeleteAsync(f, false, ct).ConfigureAwait(false);
                        _done++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _failed++;
                        _errors.Add($"{f}: {ex.Message}");
                        LogService.Error($"Delete failed for {f}: {ex.Message}", nameof(DeleteOperation), ex);
                    }
                    ReportProgress(f);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _failed++;
                _errors.Add($"{path}: {ex.Message}");
                LogService.Error($"Enum files failed for {path}: {ex.Message}", nameof(DeleteOperation), ex);
            }
            try
            {
                foreach (var d in Directory.EnumerateDirectories(path))
                {
                    ct.ThrowIfCancellationRequested();
                    try { await DeleteEntryAsync(d, ct).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _failed++;
                        _errors.Add($"{d}: {ex.Message}");
                        LogService.Error($"Delete dir failed for {d}: {ex.Message}", nameof(DeleteOperation), ex);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _failed++;
                _errors.Add($"{path}: {ex.Message}");
                LogService.Error($"Enum dirs failed for {path}: {ex.Message}", nameof(DeleteOperation), ex);
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

    private void Count(string path, CancellationToken ct)
    {
        foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            _total++;
        }
        foreach (var d in Directory.EnumerateDirectories(path))
        {
            ct.ThrowIfCancellationRequested();
            _total++;
            Count(d, ct);
        }
    }
}
