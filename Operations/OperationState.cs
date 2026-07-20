namespace CoderCommander.Operations;

/// <summary>
/// Состояние асинхронной файловой операции.
/// State of an asynchronous file operation.
/// </summary>
public enum OperationState
{
    /// <summary>Не запущена. / Not started.</summary>
    NotStarted,
    /// <summary>Выполняется. / Running.</summary>
    Running,
    /// <summary>Успешно завершена. / Completed successfully.</summary>
    Completed,
    /// <summary>Отменена по токену. / Canceled via token.</summary>
    Canceled,
    /// <summary>Завершена с ошибкой. / Failed with an error.</summary>
    Failed
}
