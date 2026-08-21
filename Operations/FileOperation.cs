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
    private TaskCompletionSource<bool>? _pauseTcs;
    private CancellationTokenSource? _skipCts;

    /// <summary>True once a subclass has actually wired <see cref="WaitIfPausedAsync"/> and
    /// <see cref="BeginFile"/>/<see cref="EndFile"/> into its own per-file loop - <see cref="Pause"/>/
    /// <see cref="RequestSkip"/> are implemented generically here in the base class, but calling
    /// them on an operation that never checks the gate would silently do nothing while still
    /// flipping <see cref="State"/> to <see cref="OperationState.Paused"/>, which is worse than not
    /// offering the buttons at all. Defaults to false; overridden by whichever operations have
    /// actually been wired.</summary>
    public virtual bool SupportsPauseAndSkip => false;

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
        TaskCompletionSource<bool>? pauseTcs;
        lock (_stateLock)
        {
            _cancelRequested = true;
            _cts?.Cancel();
            // A paused operation is blocked inside WaitIfPausedAsync's await, which only observes
            // the linked ct - _cts.Cancel() above already signals that, but the pause TCS itself
            // must also be released so the await actually wakes up and re-checks it, rather than
            // sitting blocked forever behind a gate nothing will ever open otherwise.
            pauseTcs = _pauseTcs;
            _pauseTcs = null;
        }
        pauseTcs?.TrySetResult(true);
    }

    /// <summary>Pauses a running operation - a no-op unless <see cref="State"/> is currently
    /// <see cref="OperationState.Running"/> (so a stray click after the operation already finished,
    /// or a double-click, can't leave it stuck showing Paused forever).</summary>
    public void Pause()
    {
        lock (_stateLock)
        {
            if (_state != OperationState.Running) return;
            _pauseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _state = OperationState.Paused;
        }
        StateChanged?.Invoke(this, OperationState.Paused);
    }

    /// <summary>Resumes a paused operation - a no-op unless <see cref="State"/> is currently
    /// <see cref="OperationState.Paused"/>.</summary>
    public void Resume()
    {
        TaskCompletionSource<bool>? tcs;
        lock (_stateLock)
        {
            if (_state != OperationState.Paused) return;
            tcs = _pauseTcs;
            _pauseTcs = null;
            _state = OperationState.Running;
        }
        StateChanged?.Invoke(this, OperationState.Running);
        tcs?.TrySetResult(true);
    }

    /// <summary>Requests that whichever file is currently being processed be abandoned and the
    /// operation move on to the next one, without cancelling the operation as a whole. A no-op if
    /// no file is currently in flight (nothing between <see cref="BeginFile"/> and <see cref="EndFile"/>
    /// right now) - there is nothing to skip.</summary>
    public void RequestSkip()
    {
        lock (_stateLock) { _skipCts?.Cancel(); }
    }

    /// <summary>Awaited between files (and, for a single large file, periodically during its own
    /// streamed copy - see <see cref="ProgressStream"/> callers) so a paused operation genuinely
    /// stops doing I/O instead of merely showing "Paused" while continuing to run underneath.
    /// Async, not a blocking wait - an operation that stays paused for a long time (the user
    /// stepped away) does not tie up a thread-pool thread for the duration.</summary>
    protected async Task WaitIfPausedAsync(CancellationToken ct)
    {
        while (true)
        {
            TaskCompletionSource<bool>? tcs;
            lock (_stateLock) { tcs = _pauseTcs; }
            if (tcs == null) return;

            using var reg = ct.Register(static s => ((TaskCompletionSource<bool>)s!).TrySetCanceled(), tcs);
            await tcs.Task.ConfigureAwait(false);
            // Loop back rather than trusting a single wait: Resume() then an immediate re-Pause()
            // could otherwise let a caller fall through believing it's still running when a new
            // pause already started.
        }
    }

    /// <summary>Synchronous variant of <see cref="WaitIfPausedAsync"/> for callers that can't be
    /// async themselves - e.g. <see cref="ProgressStream"/>'s per-chunk callback, invoked from
    /// inside <see cref="Stream.CopyToAsync(Stream)"/>'s own read loop. Safe to block on: operations
    /// already run off the UI thread (no captured <see cref="SynchronizationContext"/> to deadlock
    /// against), and <see cref="WaitIfPausedAsync"/>'s own await is <c>ConfigureAwait(false)</c>
    /// throughout.</summary>
    protected void WaitIfPausedSync(CancellationToken ct) => WaitIfPausedAsync(ct).GetAwaiter().GetResult();

    /// <summary>Marks the start of processing one file - creates a fresh cancellation source linked
    /// to <paramref name="ct"/> that <see cref="RequestSkip"/> can trigger independently, and pass
    /// its <see cref="CancellationTokenSource.Token"/> into that single file's I/O. Must be paired
    /// with <see cref="EndFile"/> (a <c>using</c>/<c>try</c>/<c>finally</c>) so a skip request can
    /// never leak into the next file by hitting a stale, already-disposed source.</summary>
    protected CancellationTokenSource BeginFile(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_stateLock) { _skipCts = cts; }
        return cts;
    }

    /// <summary>Ends the file started by <see cref="BeginFile"/> - clears <c>_skipCts</c> only if it
    /// still points at <paramref name="cts"/> (a concurrent <see cref="BeginFile"/> for the next
    /// file may already have replaced it) and disposes it.</summary>
    protected void EndFile(CancellationTokenSource cts)
    {
        lock (_stateLock) { if (ReferenceEquals(_skipCts, cts)) _skipCts = null; }
        cts.Dispose();
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
            // Not expected to still be non-null here in the ordinary case (EndFile already cleared
            // it once the last file finished) - only reachable if the operation was disposed while
            // a file was genuinely still in flight (e.g. torn down mid-operation), which BeginFile/
            // EndFile's own pairing can't prevent from outside.
            _skipCts?.Dispose();
            _skipCts = null;
        }
        // No finalizer on this class today, so this costs nothing either way - but it's the
        // correct defensive default (CA1816) if a derived type ever adds one: without it, that
        // finalizer would still run and re-queue for finalization needlessly on every instance.
        GC.SuppressFinalize(this);
    }
}
