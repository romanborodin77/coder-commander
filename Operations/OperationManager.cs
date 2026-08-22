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

    /// <summary>The id <see cref="OperationManager"/> tracks this entry under - what
    /// <see cref="OperationManager.Cancel"/>/<see cref="OperationManager.StartQueuedAsync"/> take,
    /// so a UI holding only this <see cref="QueuedOperation"/> (e.g. via a <c>ListViewItem.Tag</c>)
    /// doesn't need a separate id lookup. Set once by <c>OperationManager.AddQueued</c>.</summary>
    public Guid Id { get; internal set; }

    /// <summary>Time the operation started executing (set by <see cref="MarkStarted"/>).</summary>
    public DateTime StartTime { get; private set; }

    /// <summary>Most recent progress report, or null if none received yet.</summary>
    public OperationProgress? LastProgress { get; set; }

    /// <summary>Lets a still-queued (not yet started) operation be pulled out of the queue immediately.</summary>
    internal CancellationTokenSource QueueWaitCts { get; } = new();

    /// <summary>True for an operation added via <see cref="OperationManager.Enqueue"/> that hasn't
    /// had <see cref="OperationManager.StartQueuedAsync"/> called for it yet - it sits in
    /// <see cref="OperationState.NotStarted"/> indefinitely, with nothing awaiting the queue lock on
    /// its behalf, until something (the user, via <c>OperationQueueForm</c>'s "Start" action)
    /// actually starts it. False for a plain <see cref="OperationManager.RunAsync"/> operation,
    /// which is already waiting its turn the moment it's added - <see cref="OperationState.NotStarted"/>
    /// there just means "hasn't reached the front of the queue yet", not "never started".</summary>
    internal bool RequiresManualStart { get; set; }

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
        var id = AddQueued(operation, displayName);
        await RunQueuedAsync(id, externalCt).ConfigureAwait(true);
    }

    /// <summary>
    /// Adds an operation to the queue without starting it - it sits in
    /// <see cref="OperationState.NotStarted"/>, visible in <c>OperationQueueForm</c>, until
    /// <see cref="StartQueuedAsync"/> is called for its id. Used by <c>CopyMoveDialogForm</c>'s
    /// "Add to queue" checkbox to let several transfers be gathered before any of them run - unlike
    /// <see cref="RunAsync"/>, nothing here waits on the queue lock, so the operation does not
    /// auto-start just because an earlier one finishes.
    /// </summary>
    /// <returns>The id to pass to <see cref="StartQueuedAsync"/>.</returns>
    public Guid Enqueue(IFileOperation operation, string displayName)
    {
        var id = AddQueued(operation, displayName);
        _operations[id].RequiresManualStart = true;
        return id;
    }

    /// <summary>Starts an operation previously added via <see cref="Enqueue"/>. A no-op if the id is
    /// unknown (already removed) or was not awaiting a manual start (already running, or a plain
    /// <see cref="RunAsync"/> operation) - guards against a double click on "Start" running the
    /// same operation's <see cref="IFileOperation.ExecuteAsync"/> twice.</summary>
    public async Task StartQueuedAsync(Guid id, CancellationToken externalCt = default)
    {
        if (!_operations.TryGetValue(id, out var queued) || !queued.RequiresManualStart)
            return;
        queued.RequiresManualStart = false;
        await RunQueuedAsync(id, externalCt).ConfigureAwait(true);
    }

    /// <summary>Registers a new operation and wires its progress/state events - shared by
    /// <see cref="RunAsync"/> and <see cref="Enqueue"/>, which differ only in whether the queue-wait
    /// tail (<see cref="RunQueuedAsync"/>) runs immediately or is deferred.</summary>
    private Guid AddQueued(IFileOperation operation, string displayName)
    {
        var queued = new QueuedOperation(operation, displayName);
        var id = Guid.NewGuid();
        queued.Id = id;
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
                        try
                        {
                            // Only fire Removed if this thread actually won the TryRemove race —
                            // RemoveCompleted() may have already removed it, and a duplicate Removed
                            // event for a non-existent id confuses subscribers.
                            if (_operations.TryRemove(id, out var _))
                            {
                                OperationChanged?.Invoke(this, new OperationManagerEventArgs(id, queued, OperationChangeType.Removed));
                                (operation as IDisposable)?.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            Services.LogService.Error($"OperationManager delayed-removal failed for {id}", ex);
                        }
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

        return id;
    }

    /// <summary>The queue-wait-then-execute tail shared by <see cref="RunAsync"/> (runs it
    /// immediately after <see cref="AddQueued"/>) and <see cref="StartQueuedAsync"/> (runs it once
    /// the user starts a previously <see cref="Enqueue"/>d operation). A no-op if <paramref name="id"/>
    /// is no longer in the queue (already removed by a race with cancellation/disposal).</summary>
    private async Task RunQueuedAsync(Guid id, CancellationToken externalCt)
    {
        if (!_operations.TryGetValue(id, out var queued)) return;
        var operation = queued.Operation;

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
            if (operation is FileOperation fo)
                fo.MarkCanceledWithoutRunning();
            // Never reached ExecuteAsync, so FileOperation's own _cts field is still null and this
            // is a cheap no-op - included anyway so every exit from RunAsync disposes what it owns,
            // not just the ones that got far enough to actually need it.
            (operation as IDisposable)?.Dispose();
            throw;
        }

        try
        {
            queued.MarkStarted();
            // _disposeCts.Token can throw ObjectDisposedException if Dispose() races this exact
            // moment - reading it directly here had no catch around it, so that exception used to
            // propagate straight out of RunAsync, uncaught, onto whichever fire-and-forget
            // "_ = Operations.RunAsync(...)" caller started this operation (an unobserved task
            // exception). A CancellationToken struct obtained before disposal stays safely
            // queryable afterward, so treating "already disposed" as "already cancelled" and
            // linking that instead is both correct and exception-free.
            CancellationToken disposeToken;
            try { disposeToken = _disposeCts.Token; }
            catch (ObjectDisposedException) { disposeToken = new CancellationToken(canceled: true); }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(externalCt, disposeToken);
            await operation.ExecuteAsync(linked.Token);
        }
        finally
        {
            try { _queueLock.Release(); }
            catch (ObjectDisposedException) { /* Dispose already ran — expected during shutdown */ }
            lock (queued.CtsLock) { queued.QueueWaitCts.Dispose(); }
            // If Dispose() was called while this operation was still running, it couldn't
            // dispose _queueLock (Wait(0) returned false). Dispose it here now that we've
            // released it — otherwise the SemaphoreSlim leaks.
            if (_disposed)
            {
                try { _queueLock.Dispose(); }
                catch (ObjectDisposedException) { /* already disposed by another finally */ }
            }
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
        if (!_operations.TryGetValue(id, out var q)) return;

        if (q.RequiresManualStart)
        {
            // Nothing is awaiting the queue lock on this operation's behalf yet (see
            // RequiresManualStart's own doc comment) - CancelQueued's normal mechanism
            // (cancelling QueueWaitCts) would have nobody to wake up, and the operation would sit
            // in NotStarted forever. Finish it synchronously instead, the same way
            // RunQueuedAsync's own cancel-before-start catch block does for a plain RunAsync
            // operation cancelled while genuinely queued.
            q.RequiresManualStart = false;
            if (q.Operation is FileOperation fo)
                fo.MarkCanceledWithoutRunning();
            if (_operations.TryRemove(id, out _))
            {
                OperationChanged?.Invoke(this, new OperationManagerEventArgs(id, q, OperationChangeType.Removed));
                (q.Operation as IDisposable)?.Dispose();
            }
            return;
        }

        CancelQueued(q);
    }

    /// <summary>Cancels all queued and running operations.</summary>
    public void CancelAll()
    {
        foreach (var id in _operations.Keys.ToList())
            Cancel(id);
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
                // A user-triggered "clear finished" removal reaches the same terminal-state
                // operations the delayed removal above would have disposed eventually - dispose
                // immediately here too rather than leaving it to whichever path runs first.
                (kv.Value.Operation as IDisposable)?.Dispose();
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
        {
            CancelQueued(queued);
            // Dispose operations that may be in the 2-second delayed-disposal window — without this,
            // an operation that finished just before Dispose() was called would never be disposed
            // because the Task.Delay(2000) was cancelled by _disposeCts.Cancel() above.
            (queued.Operation as IDisposable)?.Dispose();
        }

        // Don't wait for queue lock synchronously — that blocks the UI thread for up to 5 seconds
        // if an operation is still running. Instead, mark disposed and let RunAsync's finally
        // block release and dispose the semaphore when the operation completes.
        if (_queueLock.Wait(0))
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
