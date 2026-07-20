using System;
using System.Threading;
using System.Threading.Tasks;

namespace CoderCommander.Operations;

/// <summary>
/// Контракт асинхронной файловой операции (копирование, перенос, удаление, хеширование и т.д.).
/// Contract for an asynchronous file operation (copy, move, delete, hashing, etc.).
/// </summary>
public interface IFileOperation
{
    /// <summary>Текущее состояние операции. / Current operation state.</summary>
    OperationState State { get; }

    /// <summary>Исключение, если операция завершилась с ошибкой. / Exception if the operation failed.</summary>
    Exception? LastError { get; }

    /// <summary>Событие смены состояния. / State-change event.</summary>
    event EventHandler<OperationState>? StateChanged;

    /// <summary>Событие прогресса (дублирует IProgress). / Progress event (mirrors IProgress).</summary>
    event EventHandler<OperationProgress>? ProgressChanged;

    /// <summary>
    /// Выполняет операцию асинхронно с поддержкой отмены.
    /// Runs the operation asynchronously with cancellation support.
    /// </summary>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    Task ExecuteAsync(CancellationToken ct = default);
}
