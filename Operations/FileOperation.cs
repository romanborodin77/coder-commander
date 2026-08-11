using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Base class for all file operations: manages state, progress, cancellation.
/// </summary>
public abstract class FileOperation : IFileOperation, IDisposable
{
    /// <inheritdoc/>
    public abstract OperationType Type { get; }

    /// <inheritdoc/>
    public abstract string Title { get; }

    private volatile OperationState _state = OperationState.NotStarted;
    private Exception? _lastError;
    private CancellationTokenSource? _cts;
    private readonly object _stateLock = new();
    private bool _cancelRequested;

    /// <inheritdoc/>
    public OperationState State => _state;

    /// <inheritdoc/>
    public Exception? LastError => _lastError;

    /// <inheritdoc/>
    public event EventHandler<OperationState>? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<OperationProgress>? ProgressChanged;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        CancellationTokenSource cts;
        lock (_stateLock)
        {
            // Rejects any re-entry, not just a concurrent one: an operation instance is meant to
            // run exactly once. OperationManager.RunAsync subscribes a fresh ProgressChanged/
            // StateChanged handler pair on every call - re-running an already-Completed/Failed/
            // Canceled operation (rather than throwing) used to silently restart it from scratch
            // and, if that happened via a second RunAsync call on the same instance, double up
            // every progress/state event through the old handlers still attached from the first run.
            if (_state is not OperationState.NotStarted)
                throw new InvalidOperationException($"Operation already {(_state == OperationState.Running ? "running" : "run")} (State={_state})");

            _cts?.Dispose();
            cts = _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (_cancelRequested)
                cts.Cancel();
        }

        SetState(OperationState.Running);

        try
        {
            await ExecuteCoreAsync(cts.Token).ConfigureAwait(false);
            SetState(OperationState.Completed);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            SetState(OperationState.Canceled);
        }
        catch (Exception ex)
        {
            _lastError = ex;
            LogService.Error($"Operation {GetType().Name} failed: {ex.Message}", ex);
            SetState(OperationState.Failed);
        }
    }

    /// <inheritdoc/>
    public void Cancel()
    {
        lock (_stateLock)
        {
            _cancelRequested = true;
            _cts?.Cancel();
        }
    }

    /// <summary>Subclasses implement the actual work here. Called once per <see cref="ExecuteAsync"/> invocation.</summary>
    protected abstract Task ExecuteCoreAsync(CancellationToken ct);

    /// <summary>Raises <see cref="ProgressChanged"/> with the given progress report.</summary>
    protected void Report(OperationProgress p) => ProgressChanged?.Invoke(this, p);

    private long _lastReportTicks;

    /// <summary>Invokes <paramref name="report"/> at most once every 250ms - shared by
    /// operations (Pack/Unpack) whose per-chunk progress callback would otherwise flood the UI
    /// with updates while a large file streams.</summary>
    protected void ReportThrottled(Action report)
    {
        var now = Environment.TickCount64;
        if (now - _lastReportTicks < 250) return;
        _lastReportTicks = now;
        report();
    }

    /// <summary>Updates <see cref="State"/> and raises <see cref="StateChanged"/>.</summary>
    protected void SetState(OperationState s)
    {
        lock (_stateLock)
        {
            _state = s;
        }
        StateChanged?.Invoke(this, s);
    }

    /// <summary>
    /// Marks a still-queued operation as Canceled without it ever running - used by
    /// <see cref="OperationManager"/> when a queued operation is cancelled before its turn comes
    /// up, so <see cref="ExecuteAsync"/> is never even called and the normal
    /// "catch OperationCanceledException -&gt; SetState(Canceled)" path inside it never runs
    /// either. Without this, such an operation's <see cref="State"/> stayed <see
    /// cref="OperationState.NotStarted"/> forever - indistinguishable, to anything observing
    /// State, from an operation that was simply never touched. A no-op if <see cref="ExecuteAsync"/>
    /// has already started (State is no longer NotStarted) - that path owns its own transition.
    /// </summary>
    internal void MarkCanceledWithoutRunning()
    {
        lock (_stateLock)
        {
            if (_state != OperationState.NotStarted) return;
            _state = OperationState.Canceled;
        }
        StateChanged?.Invoke(this, OperationState.Canceled);
    }

    /// <summary>Disposes the internal cancellation token source.</summary>
    public void Dispose()
    {
        lock (_stateLock)
        {
            _cts?.Dispose();
            _cts = null;
        }
        // No finalizer on this class today, so this costs nothing either way - but it's the
        // correct defensive default (CA1816) if a derived type ever adds one: without it, that
        // finalizer would still run and re-queue for finalization needlessly on every instance.
        GC.SuppressFinalize(this);
    }
}
