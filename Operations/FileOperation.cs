using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Base class for all file operations: manages state, progress, cancellation.
//// </summary>
public abstract class FileOperation : IFileOperation, IDisposable
{
    public abstract OperationType Type { get; }
    public abstract string Title { get; }

    private volatile OperationState _state = OperationState.NotStarted;
    private Exception? _lastError;
    private CancellationTokenSource? _cts;
    private readonly object _stateLock = new();
    private bool _cancelRequested;

    public OperationState State => _state;
    public Exception? LastError => _lastError;

    public event EventHandler<OperationState>? StateChanged;
    public event EventHandler<OperationProgress>? ProgressChanged;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        CancellationTokenSource cts;
        lock (_stateLock)
        {
            if (_state is OperationState.Running)
                throw new InvalidOperationException("Operation already running");

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

    public void Cancel()
    {
        lock (_stateLock)
        {
            _cancelRequested = true;
            _cts?.Cancel();
        }
    }

    protected abstract Task ExecuteCoreAsync(CancellationToken ct);

    protected void Report(OperationProgress p) => ProgressChanged?.Invoke(this, p);

    protected void SetState(OperationState s)
    {
        lock (_stateLock)
        {
            _state = s;
        }
        StateChanged?.Invoke(this, s);
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }
}
