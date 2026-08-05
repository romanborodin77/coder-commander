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

    /// <summary>Raised when <see cref="State"/> changes.</summary>
    event EventHandler<OperationState>? StateChanged;

    /// <summary>Raised periodically with progress updates during execution.</summary>
    event EventHandler<OperationProgress>? ProgressChanged;

    /// <summary>Starts the operation. Throws if already running.</summary>
    Task ExecuteAsync(CancellationToken ct = default);

    /// <summary>Requests cooperative cancellation of a running or queued operation.</summary>
    void Cancel();
}
