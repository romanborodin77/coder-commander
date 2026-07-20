using System;
using System.Threading;
using System.Threading.Tasks;

namespace CoderCommander.Operations;

/// <summary>
/// Обёртка над делегатом Func&lt;CancellationToken, Task&gt;, реализующая IFileOperation.
/// Wrapper over delegate Func&lt;CancellationToken, Task&gt; implementing IFileOperation.
/// Позволяет enqueue произвольные асинхронные операции (копирование, удаление и т.д.) в очередь.
/// Allows enqueuing arbitrary async operations (copy, delete, etc.) into the queue.
/// </summary>
public sealed class DelegateOperation : FileOperation
{
    private readonly Func<CancellationToken, Task> _work;
    private readonly IFileOperation? _innerOperation;

    /// <summary>
    /// Создаёт операцию-обёртку над делегатом.
    /// Creates a delegate-wrapping operation.
    /// </summary>
    /// <param name="work">Асинхронная логика операции. / Async operation logic.</param>
    /// <param name="progress">Приёмник прогресса (необязательный). / Progress sink (optional).</param>
    public DelegateOperation(Func<CancellationToken, Task> work, IProgress<OperationProgress>? progress = null)
        : base(progress)
    {
        _work = work ?? throw new ArgumentNullException(nameof(work));
    }

    /// <summary>
    /// Создаёт операцию-обёртку с внутренней операцией для форварда прогресса.
    /// Creates a delegate-wrapping operation with inner operation for progress forwarding.
    /// </summary>
    /// <param name="inner">Внутренняя операция, чей прогресс будет форвардиться. / Inner operation whose progress will be forwarded.</param>
    /// <param name="work">Асинхронная логика операции. / Async operation logic.</param>
    public DelegateOperation(IFileOperation inner, Func<CancellationToken, Task> work)
        : base(null)
    {
        _innerOperation = inner ?? throw new ArgumentNullException(nameof(inner));
        _work = work ?? throw new ArgumentNullException(nameof(work));

        inner.ProgressChanged += OnInnerProgressChanged;
        inner.StateChanged += OnInnerStateChanged;
    }

    private void OnInnerProgressChanged(object? sender, OperationProgress p)
    {
        Report(p);
    }

    private void OnInnerStateChanged(object? sender, OperationState state)
    {
        // Don't forward state - DelegateOperation manages its own state
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        try
        {
            await _work(ct).ConfigureAwait(false);
        }
        finally
        {
            if (_innerOperation != null)
            {
                _innerOperation.ProgressChanged -= OnInnerProgressChanged;
                _innerOperation.StateChanged -= OnInnerStateChanged;
            }
        }
    }
}
