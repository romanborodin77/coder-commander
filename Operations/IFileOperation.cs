namespace CoderCommander.Operations;

/// <summary>
/// Contract for an asynchronous file operation (copy, move, delete, etc.).
//// </summary>
public interface IFileOperation
{
    OperationType Type { get; }
    OperationState State { get; }
    Exception? LastError { get; }
    string Title { get; }

    event EventHandler<OperationState>? StateChanged;
    event EventHandler<OperationProgress>? ProgressChanged;

    Task ExecuteAsync(CancellationToken ct = default);
    void Cancel();
}
