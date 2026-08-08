using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the bug fixed in <see cref="OperationManager"/>: cancelling a still-queued
/// (not yet started) operation removed it from the manager but never touched the underlying
/// IFileOperation's own State - it stayed NotStarted forever, indistinguishable from an operation
/// that was simply never touched. Anything observing State directly (rather than going through
/// OperationManager's OperationChanged event) couldn't tell a cancelled-before-it-ran operation
/// apart from one that's still sitting untouched in some other queue.
/// </summary>
public class OperationManagerQueuedCancelStateTests
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
    public async Task Cancel_StillQueuedOperation_MarksItsOwnStateAsCanceled()
    {
        var manager = new OperationManager();
        var blocking = new ControllableOperation();
        var queuedOp = new ControllableOperation();

        var blockingTask = manager.RunAsync(blocking, "blocking-op");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (blocking.State != OperationState.Running && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.That(blocking.State, Is.EqualTo(OperationState.Running));

        var queuedRunTask = manager.RunAsync(queuedOp, "queued-op");
        await Task.Delay(50); // let it actually enqueue behind the blocking op

        // Find the Guid key OperationManager uses internally for this queued operation.
        var opsField = typeof(OperationManager).GetField("_operations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var ops = (System.Collections.Concurrent.ConcurrentDictionary<Guid, QueuedOperation>)opsField.GetValue(manager)!;
        var id = ops.Single(kv => kv.Value.Operation == queuedOp).Key;

        manager.Cancel(id);

        try
        {
            await queuedRunTask;
            Assert.Fail("Cancelling a queued operation must still throw OperationCanceledException from RunAsync");
        }
        catch (OperationCanceledException) { }

        Assert.That(queuedOp.State, Is.EqualTo(OperationState.Canceled),
            "The queued operation's own State must become Canceled, not stay NotStarted forever");

        blocking.ReadyToFinish.SetResult();
        await blockingTask;
    }
}
