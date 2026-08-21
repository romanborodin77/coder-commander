namespace CoderCommander.Operations;

/// <summary>
/// Contract for an asynchronous file operation (copy, move, delete, etc.).
/// </summary>
public interface IFileOperation
{
    /// <summary>Type of this operation (copy, move, delete, etc.).</summary>
    OperationType Type { get; }

    /// <summary>Current lifecycle state of the operation.</summary>
    OperationState State { get; }

    /// <summary>Last exception that caused a failure, or null.</summary>
    Exception? LastError { get; }

    /// <summary>Human-readable title for display (e.g. "Copy", "Move").</summary>
    string Title { get; }

    /// <summary>True when this operation actually honors <see cref="Pause"/>/<see cref="Resume"/>/
    /// <see cref="RequestSkip"/> in its own per-file loop - callers (the Pause/Skip UI) should only
    /// offer those controls when this is true, since calling them on an operation that never checks
    /// for pause/skip would flip <see cref="State"/> to <see cref="OperationState.Paused"/> without
    /// the operation actually stopping.</summary>
    bool SupportsPauseAndSkip { get; }

    /// <summary>Raised when <see cref="State"/> changes.</summary>
    event EventHandler<OperationState>? StateChanged;

    /// <summary>Raised periodically with progress updates during execution.</summary>
    event EventHandler<OperationProgress>? ProgressChanged;

    /// <summary>Starts the operation. Throws if already running.</summary>
    Task ExecuteAsync(CancellationToken ct = default);

    /// <summary>Requests cooperative cancellation of a running or queued operation.</summary>
    void Cancel();

    /// <summary>Pauses a running operation. No-op unless <see cref="SupportsPauseAndSkip"/> and
    /// <see cref="State"/> is <see cref="OperationState.Running"/>.</summary>
    void Pause();

    /// <summary>Resumes a paused operation. No-op unless <see cref="State"/> is
    /// <see cref="OperationState.Paused"/>.</summary>
    void Resume();

    /// <summary>Requests that the file currently being processed be abandoned and the operation
    /// move on to the next one, without cancelling the operation as a whole.</summary>
    void RequestSkip();
}
