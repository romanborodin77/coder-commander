using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Models;
using CoderCommander.Operations;

namespace CoderCommander.Services;

/// <summary>
/// Сервис очереди операций: управление очередью файловых операций с ограничением параллелизма.
/// Operation queue service: manages file operation queue with concurrency limit.
/// Максимум 3 параллельные операции (SemaphoreSlim). Автоматический старт следующей при завершении текущей.
/// Max 3 parallel operations (SemaphoreSlim). Auto-starts next when current finishes.
/// Поддержка паузы/возобновления очереди.
/// </summary>
public sealed class OperationQueueService
{
    /// <summary>Текущий экземпляр синглтона. / Current singleton instance.</summary>
    public static OperationQueueService Current { get; } = new();

    /// <summary>Максимальное число параллельных операций. / Max concurrent operations.</summary>
    private const int MaxConcurrency = 3;

    /// <summary>Семафор для ограничения параллелизма. / Semaphore for concurrency limiting.</summary>
    private readonly SemaphoreSlim _semaphore = new(MaxConcurrency, MaxConcurrency);

    /// <summary>Блокировка для потокобезопасной модификации коллекций. / Lock for thread-safe collection mutations.</summary>
    private readonly object _lock = new();

    /// <summary>Пауза очереди (при true — новые операции не стартуют). / Queue paused (when true — new ops don't start).</summary>
    private volatile bool _isPaused;

    /// <summary>Очередь ожидающих операций. / Pending operations queue.</summary>
    public ObservableCollection<QueuedOperation> Pending { get; } = new();

    /// <summary>Активные (выполняемые) операции. / Active (running) operations.</summary>
    public ObservableCollection<QueuedOperation> Active { get; } = new();

    /// <summary>Завершённые операции (успешные, ошибки, отменённые). / Completed operations (success, failed, cancelled).</summary>
    public ObservableCollection<QueuedOperation> Completed { get; } = new();

    /// <summary>Событие изменения состояния очереди (добавление/завершение/удаление). / Queue state changed event.</summary>
    public event EventHandler? QueueChanged;

    /// <summary>Событие паузы/возобновления. / Pause/Resume event.</summary>
    public event EventHandler<bool>? PauseStateChanged;

    /// <summary>Текущий статус паузы. / Current pause state.</summary>
    public bool IsPaused => _isPaused;

    /// <summary>
    /// Приостанавливает очередь: новые операции не стартуют, выполняющиеся продолжаются.
    /// Pauses the queue: new operations don't start, running operations continue.
    /// </summary>
    public void Pause()
    {
        if (_isPaused) return;
        _isPaused = true;
        RaisePauseStateChanged(true);
        RaiseQueueChanged();
    }

    /// <summary>
    /// Возобновляет очередь: запускает ожидающие операции.
    /// Resumes the queue: starts pending operations.
    /// </summary>
    public void Resume()
    {
        if (!_isPaused) return;
        _isPaused = false;
        RaisePauseStateChanged(false);
        RaiseQueueChanged();

        lock (_lock)
        {
            foreach (var qo in Pending.Where(q => q.Status == QueuedOperationStatus.Queued).ToList())
            {
                _ = TryStartAsync(qo);
            }
        }
    }

    /// <summary>
    /// Переключает паузу. / Toggles pause.
    /// </summary>
    public void TogglePause()
    {
        if (_isPaused) Resume();
        else Pause();
    }

    /// <summary>
    /// Добавляет операцию в очередь и запускает, если есть свободные слоты и очередь не на паузе.
    /// Adds operation to queue and starts if there are free slots and queue is not paused.
    /// </summary>
    public QueuedOperation Enqueue(IFileOperation operation, string description, string sourcePath = "", string destPath = "", string operationType = "Copy")
    {
        var qo = new QueuedOperation(operation, description, sourcePath, destPath, operationType);
        lock (_lock) { Pending.Add(qo); }
        RaiseQueueChanged();

        if (!_isPaused)
        {
            _ = TryStartAsync(qo);
        }
        return qo;
    }

    /// <summary>
    /// Пытается начать выполнение операции, захватив слот семафора.
    /// Attempts to start operation execution by acquiring a semaphore slot.
    /// </summary>
    private async Task TryStartAsync(QueuedOperation qo)
    {
        if (qo.Status != QueuedOperationStatus.Queued) return;
        if (_isPaused) return;

        try
        {
            await _semaphore.WaitAsync(qo.Cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (qo.Status != QueuedOperationStatus.Queued || _isPaused)
        {
            _semaphore.Release();
            return;
        }

        QueuedOperation? movedQ = null;
        lock (_lock)
        {
            if (Pending.Remove(qo))
            {
                Active.Add(qo);
                movedQ = qo;
            }
        }

        if (movedQ != null)
        {
            movedQ.MarkRunning();
            RaiseQueueChanged();
        }

        try
        {
            await qo.Operation.ExecuteAsync(qo.Cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (qo.Cts.IsCancellationRequested)
        {
            qo.Status = QueuedOperationStatus.Cancelled;
        }
        catch (Exception)
        {
            if (qo.Status != QueuedOperationStatus.Cancelled)
                qo.Status = QueuedOperationStatus.Failed;
        }
        finally
        {
            bool wasRemoved = false;
            lock (_lock)
            {
                wasRemoved = Active.Remove(qo);
                if (wasRemoved)
                    Completed.Add(qo);
            }

            qo.Detach();
            _semaphore.Release();
            RaiseQueueChanged();

            if (wasRemoved && !_isPaused)
                StartNextPending();
        }
    }

    private void StartNextPending()
    {
        if (_isPaused) return;
        QueuedOperation? next;
        lock (_lock) { next = Pending.FirstOrDefault(q => q.Status == QueuedOperationStatus.Queued); }
        if (next != null)
            _ = TryStartAsync(next);
    }

    public void CancelAll()
    {
        QueuedOperation[] pendingSnapshot;
        QueuedOperation[] activeSnapshot;
        lock (_lock)
        {
            pendingSnapshot = Pending.ToArray();
            activeSnapshot = Active.ToArray();
        }

        lock (_lock)
        {
            Pending.Clear();
            foreach (var qo in pendingSnapshot)
            {
                qo.Cancel();
                qo.Detach();
                Completed.Add(qo);
            }
        }

        foreach (var qo in activeSnapshot)
            qo.Cancel();

        RaiseQueueChanged();
    }

    public void Cancel(QueuedOperation qo)
    {
        qo.Cancel();
        RaiseQueueChanged();
    }

    public void RetryFailed()
    {
        QueuedOperation[] failed;
        lock (_lock)
        {
            failed = Completed
                .Where(q => q.Status is QueuedOperationStatus.Failed or QueuedOperationStatus.Cancelled)
                .ToArray();
        }

        lock (_lock)
        {
            foreach (var qo in failed)
            {
                Completed.Remove(qo);
                qo.Retry();
                Pending.Add(qo);
            }
        }
        RaiseQueueChanged();

        if (!_isPaused)
        {
            foreach (var qo in failed.Where(q => q.Status == QueuedOperationStatus.Queued))
                _ = TryStartAsync(qo);
        }
    }

    public void RemoveCompleted()
    {
        QueuedOperation[] snapshot;
        lock (_lock)
        {
            snapshot = Completed.ToArray();
            Completed.Clear();
        }
        foreach (var qo in snapshot)
            qo.Detach();
        RaiseQueueChanged();
    }

    private void RaiseQueueChanged()
    {
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaisePauseStateChanged(bool isPaused)
    {
        PauseStateChanged?.Invoke(this, isPaused);
    }

    public int ActiveCount => Active.Count;
    public int PendingCount => Pending.Count;
    public int CompletedCount => Completed.Count;
    public bool HasActiveOrPending => ActiveCount > 0 || PendingCount > 0;
}
