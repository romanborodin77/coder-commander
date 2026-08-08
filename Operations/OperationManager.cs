using System.Collections.Concurrent;

namespace CoderCommander.Operations;

/// <summary>
/// A queued operation wrapper with state tracking.
/// </summary>
public sealed class QueuedOperation
{
    /// <summary>The underlying file operation.</summary>
    public IFileOperation Operation { get; }

    /// <summary>Human-readable name shown in the progress UI.</summary>
    public string DisplayName { get; }

    /// <summary>Time the operation started executing (set by <see cref="MarkStarted"/>).</summary>
    public DateTime StartTime { get; private set; }

    /// <summary>Most recent progress report, or null if none received yet.</summary>
    public OperationProgress? LastProgress { get; set; }

    /// <summary>Lets a still-queued (not yet started) operation be pulled out of the queue immediately.</summary>
    internal CancellationTokenSource QueueWaitCts { get; } = new();

    /// <summary>Guards <see cref="QueueWaitCts"/>'s Cancel() and Dispose() calls against each other -
    /// CancellationTokenSource documents that calling Cancel() concurrently with Dispose() from
    /// another thread isn't guaranteed safe, and RunAsync/CancelQueued can genuinely race on it
    /// (a user cancelling right as the operation finishes running).</summary>
    internal readonly object CtsLock = new();

    /// <summary>Creates a queued operation wrapping <paramref name="op"/>.</summary>
    public QueuedOperation(IFileOperation op, string displayName)
    {
        Operation = op;
        DisplayName = displayName;
    }

    /// <summary>Sets <see cref="StartTime"/> to now.</summary>
    public void MarkStarted() => StartTime = DateTime.Now;
}

/// <summary>
/// Manages running file operations with queue support.
/// </summary>
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

    /// <summary>Number of operations currently in the queue.</summary>
    public int ActiveCount => _operations.Count;

    /// <summary>Queues and executes an operation. Blocks if another operation is already running.</summary>
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
                try
                {
                    _ = Task.Delay(OperationRemovalDelayMs, _disposeCts.Token).ContinueWith(t =>
                    {
                        _operations.TryRemove(id, out var _);
                        OperationChanged?.Invoke(this, new OperationManagerEventArgs(id, queued, OperationChangeType.Removed));
                    }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
                }
                catch (ObjectDisposedException)
                {
                    // Dispose()'s 5-second wait already elapsed and disposed _disposeCts while
                    // this operation was still finishing up - accessing .Token above throws
                    // synchronously, right inside this StateChanged handler. Left unguarded, that
                    // exception propagated out of FileOperation.SetState (called from inside
                    // ExecuteAsync's own try block), got caught by ExecuteAsync's catch, which
                    // called SetState(Failed) - triggering this same throw a second time, this
                    // time inside the catch itself, so it escaped ExecuteAsync entirely and
                    // surfaced as an unobserved task exception on the fire-and-forget
                    // "_ = Operations.RunAsync(...)" callers use - while the operation's own
                    // State had already been silently overwritten from Completed to Failed by
                    // that second SetState call. There's nothing left to schedule removal for
                    // once the manager is disposing anyway, so just let the operation finish
                    // reporting its real, correct final state.
                }
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
            lock (queued.CtsLock) { queued.QueueWaitCts.Dispose(); }
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
            lock (queued.CtsLock) { queued.QueueWaitCts.Dispose(); }
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

    /// <summary>Cancels the operation with the given id.</summary>
    public void Cancel(Guid id)
    {
        if (_operations.TryGetValue(id, out var q))
            CancelQueued(q);
    }

    /// <summary>Cancels all queued and running operations.</summary>
    public void CancelAll()
    {
        foreach (var q in _operations.Values)
            CancelQueued(q);
    }

    /// <summary>Cancels a queued/running operation, unblocking it immediately if it's still waiting for its turn.</summary>
    private static void CancelQueued(QueuedOperation q)
    {
        q.Operation.Cancel();
        lock (q.CtsLock)
        {
            try { q.QueueWaitCts.Cancel(); }
            catch (ObjectDisposedException) { /* already started running and cleaned up its queue wait */ }
        }
    }

    /// <summary>Immediately removes all completed, cancelled, or failed operations from the queue.</summary>
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

    /// <summary>Cancels all operations and releases resources.</summary>
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

/// <summary>Indicates what changed about the operation queue.</summary>
public enum OperationChangeType
{
    /// <summary>A new operation was added to the queue.</summary>
    Added,

    /// <summary>An operation started executing.</summary>
    Started,

    /// <summary>Progress was reported by an operation.</summary>
    Progress,

    /// <summary>An operation completed successfully.</summary>
    Completed,

    /// <summary>An operation was cancelled.</summary>
    Canceled,

    /// <summary>An operation failed with an error.</summary>
    Failed,

    /// <summary>An operation was removed from the queue.</summary>
    Removed
}

/// <summary>Event data for <see cref="OperationManager.OperationChanged"/>.</summary>
public sealed class OperationManagerEventArgs(Guid id, QueuedOperation op, OperationChangeType change) : EventArgs
{
    /// <summary>Unique id of the operation.</summary>
    public Guid Id { get; } = id;

    /// <summary>The queued operation this event pertains to.</summary>
    public QueuedOperation Operation { get; } = op;

    /// <summary>What changed.</summary>
    public OperationChangeType Change { get; } = change;
}
