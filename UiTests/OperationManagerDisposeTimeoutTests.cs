using System.Reflection;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the race fixed in <see cref="OperationManager"/>: <see cref="OperationManager.Dispose"/>
/// disposes its internal <c>_disposeCts</c> once its 5-second wait for the queue lock elapses,
/// even though the still-running operation holding that lock hasn't finished yet. When that
/// operation later completes and raises StateChanged, the removal-scheduling code used to touch
/// the already-disposed CTS's Token, throwing ObjectDisposedException synchronously inside the
/// handler - which FileOperation.ExecuteAsync's catch block turned into a second SetState(Failed)
/// call (overwriting the operation's real Completed state) and then let escape as an unobserved
/// exception on the operation's Task. This test disposes the manager's _disposeCts directly (via
/// reflection, since we can't cheaply wait out the real 5-second Dispose() timeout) to simulate
/// exactly that moment, then lets the running operation finish normally.
/// </summary>
public class OperationManagerDisposeTimeoutTests
{
    private sealed class ControllableOperation : FileOperation
    {
        public override OperationType Type => OperationType.Copy;
        public override string Title => "Test";
        public readonly TaskCompletionSource ReadyToFinish = new();

        protected override async Task ExecuteCoreAsync(CancellationToken ct)
        {
            await ReadyToFinish.Task.ConfigureAwait(false);
        }
    }

    [Test]
    public async Task StateChanged_AfterDisposeCtsAlreadyDisposed_StillReportsCompletedNotFailed()
    {
        var manager = new OperationManager();
        var op = new ControllableOperation();

        var runTask = manager.RunAsync(op, "test-op");

        // Wait until the operation has actually started (holds the queue lock) before disposing
        // the CTS out from under it - mirrors Dispose()'s 5-second-timeout path where a running
        // operation is still in flight.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (op.State != OperationState.Running && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.That(op.State, Is.EqualTo(OperationState.Running), "Operation must be running before we simulate the dispose race");

        var disposeCtsField = typeof(OperationManager).GetField("_disposeCts", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(disposeCtsField, Is.Not.Null, "OperationManager must still have a _disposeCts field");
        var disposeCts = (CancellationTokenSource)disposeCtsField!.GetValue(manager)!;
        disposeCts.Dispose();

        // Now let the "still running" operation actually complete.
        op.ReadyToFinish.SetResult();

        await runTask;

        Assert.That(op.State, Is.EqualTo(OperationState.Completed),
            $"A genuinely successful operation must not be reported as Failed just because the manager's dispose-timeout CTS was already disposed (LastError: {op.LastError?.Message})");
    }
}
