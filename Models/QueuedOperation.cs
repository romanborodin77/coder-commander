using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Operations;

namespace CoderCommander.Models;

/// <summary>
/// Статус операции в очереди.
/// Operation status in the queue.
/// </summary>
public enum QueuedOperationStatus
{
    /// <summary>Ожидает запуска. / Waiting to start.</summary>
    Queued,
    /// <summary>Выполняется. / Running.</summary>
    Running,
    /// <summary>Успешно завершена. / Completed successfully.</summary>
    Completed,
    /// <summary>Завершена ошибкой. / Failed.</summary>
    Failed,
    /// <summary>Отменена. / Cancelled.</summary>
    Cancelled
}

/// <summary>
/// Модель операции в очереди: обёртка над IFileOperation + UI-свойства (статус, прогресс, команды).
/// Queue operation model: wrapper around IFileOperation + UI properties (status, progress, commands).
/// </summary>
public partial class QueuedOperation : ObservableObject
{
    /// <summary>Подлежащая операция. / Underlying operation.</summary>
    public IFileOperation Operation { get; }

    /// <summary>Описание операции (для отображения в UI). / Operation description (for UI display).</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>Путь-источник. / Source path.</summary>
    [ObservableProperty]
    private string _sourcePath = string.Empty;

    /// <summary>Путь назначения. / Destination path.</summary>
    [ObservableProperty]
    private string _destPath = string.Empty;

    /// <summary>Тип операции (Copy/Move). / Operation type (Copy/Move).</summary>
    [ObservableProperty]
    private string _operationType = "Copy";

    /// <summary>Текущий статус операции. / Current operation status.</summary>
    [ObservableProperty]
    private QueuedOperationStatus _status = QueuedOperationStatus.Queued;

    /// <summary>Текущий прогресс. / Current progress snapshot.</summary>
    [ObservableProperty]
    private OperationProgress? _progress;

    /// <summary>Токен отмены. / Cancellation token source.</summary>
    public CancellationTokenSource Cts { get; set; }

    /// <summary>Исключение, если операция завершилась ошибкой. / Exception if operation failed.</summary>
    public Exception? LastError => Operation.LastError;

    /// <summary>Время добавления в очередь. / Time added to queue.</summary>
    public DateTime EnqueuedAt { get; }

    /// <summary>
    /// Создаёт экземпляр QueuedOperation.
    /// Creates a QueuedOperation instance.
    /// </summary>
    /// <param name="operation">Файловая операция. / File operation.</param>
    /// <param name="description">Описание для UI. / Description for UI.</param>
    /// <param name="sourcePath">Путь-источник. / Source path.</param>
    /// <param name="destPath">Путь назначения. / Destination path.</param>
    /// <param name="operationType">Тип операции (Copy/Move). / Operation type (Copy/Move).</param>
    public QueuedOperation(IFileOperation operation, string description, string sourcePath = "", string destPath = "", string operationType = "Copy")
    {
        Operation = operation;
        Description = description;
        SourcePath = sourcePath;
        DestPath = destPath;
        OperationType = operationType;
        Cts = new CancellationTokenSource();
        EnqueuedAt = DateTime.Now;

        Operation.StateChanged += OnStateChanged;
        Operation.ProgressChanged += OnProgressChanged;
    }

    /// <summary>Команда отмены операции. / Cancel operation command.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel()
    {
        Cts.Cancel();
        Status = QueuedOperationStatus.Cancelled;
    }

    private bool CanCancel() => Status is QueuedOperationStatus.Queued or QueuedOperationStatus.Running;

    /// <summary>Команда повтора операции. / Retry operation command.</summary>
    [RelayCommand(CanExecute = nameof(CanRetry))]
    public void Retry()
    {
        Cts.Dispose();
        Cts = new CancellationTokenSource();
        // FIXED: отписываемся перед повторной подпиской, чтобы избежать дублирования хендлеров.
        // Unsubscribe before re-subscribing to prevent duplicate event handlers.
        Operation.StateChanged -= OnStateChanged;
        Operation.ProgressChanged -= OnProgressChanged;
        Operation.StateChanged += OnStateChanged;
        Operation.ProgressChanged += OnProgressChanged;
        Status = QueuedOperationStatus.Queued;
        Progress = null;
    }

    private bool CanRetry() => Status is QueuedOperationStatus.Failed or QueuedOperationStatus.Cancelled;

    private void OnStateChanged(object? sender, OperationState state)
    {
        Status = state switch
        {
            OperationState.Running => QueuedOperationStatus.Running,
            OperationState.Completed => QueuedOperationStatus.Completed,
            OperationState.Canceled => QueuedOperationStatus.Cancelled,
            OperationState.Failed => QueuedOperationStatus.Failed,
            _ => Status
        };
    }

    private void OnProgressChanged(object? sender, OperationProgress progress)
    {
        Progress = progress;
    }

    public void MarkRunning()
    {
        Status = QueuedOperationStatus.Running;
    }

    public void Detach()
    {
        Operation.StateChanged -= OnStateChanged;
        Operation.ProgressChanged -= OnProgressChanged;
        // FIXED: диспозим Cts для предотвращения утечки ресурса.
        // Dispose Cts to prevent resource leak.
        Cts.Dispose();
    }
}
