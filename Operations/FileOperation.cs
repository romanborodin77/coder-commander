using System;
using System.Threading;
using System.Threading.Tasks;

namespace CoderCommander.Operations;

/// <summary>
/// Базовый класс для всех файловых операций: управляет состоянием, прогрессом и отменой.
/// Base class for all file operations: manages state, progress and cancellation.
/// Модель "операция + прогресс + отмена" (см. exp.yml, phase0/ph0.2).
/// Follows the "operation + progress + cancellation" pattern from exp.yml (phase0/ph0.2).
/// </summary>
public abstract class FileOperation : IFileOperation
{
    private readonly IProgress<OperationProgress>? _progress;
    private OperationState _state = OperationState.NotStarted;
    private Exception? _lastError;

    /// <inheritdoc/>
    public OperationState State => _state;

    /// <inheritdoc/>
    public Exception? LastError => _lastError;

    /// <inheritdoc/>
    public event EventHandler<OperationState>? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<OperationProgress>? ProgressChanged;

    /// <summary>
    /// Создаёт операцию. / Creates the operation.
    /// </summary>
    /// <param name="progress">Приёмник прогресса (необязательный). / Progress sink (optional).</param>
    protected FileOperation(IProgress<OperationProgress>? progress = null) => _progress = progress;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        if (_state is OperationState.Running)
            throw new InvalidOperationException("Operation already running");
        SetState(OperationState.Running);
        try
        {
            await ExecuteCoreAsync(ct).ConfigureAwait(false);
            SetState(OperationState.Completed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SetState(OperationState.Canceled);
        }
        catch (Exception ex)
        {
            _lastError = ex;
            LogError(ex);
            SetState(OperationState.Failed);
        }
    }

    /// <summary>
    /// Основная логика операции. Наследники реализуют именно её.
    /// Core operation logic — implemented by subclasses.
    /// </summary>
    protected abstract Task ExecuteCoreAsync(CancellationToken ct);

    /// <summary>
    /// Публикует прогресс (IProgress + событие). / Reports progress (IProgress + event).
    /// </summary>
    protected void Report(OperationProgress p)
    {
        _progress?.Report(p);
        ProgressChanged?.Invoke(this, p);
    }

    /// <summary>
    /// Переводит операцию в новое состояние и уведомляет подписчиков.
    /// Transitions the operation to a new state and notifies subscribers.
    /// </summary>
    protected void SetState(OperationState s)
    {
        _state = s;
        StateChanged?.Invoke(this, s);
    }

    /// <summary>Логирует ошибку операции (переопределяемо). / Logs an operation error (overridable).</summary>
    protected virtual void LogError(Exception ex)
        => CoderCommander.Services.LogService.Error($"Operation {GetType().Name} failed: {ex.Message}", GetType().Name, ex);
}
