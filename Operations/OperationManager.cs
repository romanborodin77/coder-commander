using System.Collections.Concurrent;

namespace CoderCommander.Operations;

/// <summary>
/// A queued operation wrapper with state tracking.
/// </summary>
public sealed class QueuedOperation
{
    public IFileOperation Operation { get; }
    public string DisplayName { get; }
    public DateTime StartTime { get; private set; }
    public OperationProgress? LastProgress { get; set; }

    /// <summary>Lets a still-queued (not yet started) operation be pulled out of the queue immediately.</summary>
    internal CancellationTokenSource QueueWaitCts { get; } = new();

    public QueuedOperation(IFileOperation op, string displayName)
    {
        Operation = op;
        DisplayName = displayName;
    }

    public void MarkStarted() => StartTime = DateTime.Now;
}

/// <summary>
/// Manages running file operations with queue support.
//// </summary>
public sealed class OperationManager : IDisposable
{
    private readonly ConcurrentDictionary<Guid, QueuedOperation> _operations = new();
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    // Operations always run sequentially (queue depth 1), like Total Commander.

    /// <summary>Delay before removing completed operation from queue (ms).</summary>
    private const int OperationRemovalDelayMs = 2000;

    /// <summary>Raised when an operation is added, completed, or progresses.</summary>
    public event EventHandler<OperationManagerEventArgs>? OperationChanged;

    /// <summary>All queued operations (snapshot).</summary>
    public IReadOnlyList<QueuedOperation> Operations => _operations.Values.ToList();

    public int ActiveCount => _operations.Count;

    public async Task RunAsync(IFileOperation operation, string displayName, CancellationToken externalCt = default)
    {
        var queued = new QueuedOperation(operation, displayName);
        var id = Guid.NewGuid();
        _operations[id] = queued;

        operation.ProgressChanged += (_, p) =>
        {
            queued.LastProgress = p;
            OperationChanged?.Invoke(this, new OperationManagerEventArgs(id, queued, OperationChangeType.Progress));
        };
        operation.StateChanged += (_, state) =>
        {
            OperationChanged?.Invoke(this, new OperationManagerEventArgs(id, queued, MapChangeType(state)));
            if (state is OperationState.Completed or OperationState.Canceled or OperationState.Failed)
            {
                Services.LogService.LogFileOperation(operation.GetType().Name, $"{displayName} -> {state}");
                _ = Task.Delay(OperationRemovalDelayMs, _disposeCts.Token).ContinueWith(t =>
                {
                    _operations.TryRemove(id, out var _);
                    OperationChanged?.Invoke(this, new OperationManagerEventArgs(id, queued, OperationChangeType.Removed));
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            }
        };

        Services.LogService.LogFileOperation(operation.GetType().Name, $"{displayName} -> queued");
        OperationChanged?.Invoke(this, new OperationManagerEventArgs(id, queued, OperationChangeType.Added));

        try
        {
            using var queueWait = CancellationTokenSource.CreateLinkedTokenSource(externalCt, queued.QueueWaitCts.Token);
            await _queueLock.WaitAsync(queueWait.Token);
        }
        catch (OperationCanceledException)
        {
            _operations.TryRemove(id, out var _);
            OperationChanged?.Invoke(this, new OperationManagerEventArgs(id, queued, OperationChangeType.Removed));
            queued.QueueWaitCts.Dispose();
            throw;
        }

        try
        {
            queued.MarkStarted();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(externalCt, _disposeCts.Token);
            await operation.ExecuteAsync(linked.Token);
        }
        finally
        {
            _queueLock.Release();
            queued.QueueWaitCts.Dispose();
        }
    }

    private static OperationChangeType MapChangeType(OperationState state) => state switch
    {
        OperationState.Running => OperationChangeType.Started,
        OperationState.Completed => OperationChangeType.Completed,
        OperationState.Canceled => OperationChangeType.Canceled,
        OperationState.Failed => OperationChangeType.Failed,
        _ => OperationChangeType.Progress
    };

    public void Cancel(Guid id)
    {
        if (_operations.TryGetValue(id, out var q))
            CancelQueued(q);
    }

    public void CancelAll()
    {
        foreach (var q in _operations.Values)
            CancelQueued(q);
    }

    /// <summary>Cancels a queued/running operation, unblocking it immediately if it's still waiting for its turn.</summary>
    private static void CancelQueued(QueuedOperation q)
    {
        q.Operation.Cancel();
        try { q.QueueWaitCts.Cancel(); }
        catch (ObjectDisposedException) { /* already started running and cleaned up its queue wait */ }
    }

    public void RemoveCompleted()
    {
        foreach (var kv in _operations)
        {
            if (kv.Value.Operation.State is OperationState.Completed or OperationState.Canceled or OperationState.Failed)
            {
                _operations.TryRemove(kv.Key, out _);
                OperationChanged?.Invoke(this, new OperationManagerEventArgs(kv.Key, kv.Value, OperationChangeType.Removed));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposeCts.Cancel();

        // Cancel everything up front (queued and running) so a running operation gets a chance to
        // unwind cooperatively instead of us just waiting blindly below.
        foreach (var queued in _operations.Values)
            CancelQueued(queued);

        // Wait for queue lock with timeout (don't use async in Dispose). Only dispose the semaphore
        // if we actually acquired it here: if the wait times out, some operation is still running and
        // holds the lock, and it will call _queueLock.Release() from its own finally block once it
        // eventually finishes/cancels - disposing the semaphore now would turn that into an
        // unhandled ObjectDisposedException on a background thread.
        if (_queueLock.Wait(TimeSpan.FromSeconds(5)))
        {
            _queueLock.Release();
            _queueLock.Dispose();
        }

        _disposeCts.Dispose();
    }
}

public enum OperationChangeType
{
    Added,
    Started,
    Progress,
    Completed,
    Canceled,
    Failed,
    Removed
}

public sealed class OperationManagerEventArgs(Guid id, QueuedOperation op, OperationChangeType change) : EventArgs
{
    public Guid Id { get; } = id;
    public QueuedOperation Operation { get; } = op;
    public OperationChangeType Change { get; } = change;
}
