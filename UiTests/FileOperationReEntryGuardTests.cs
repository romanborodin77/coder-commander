using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the bug fixed in <see cref="FileOperation.ExecuteAsync"/>: the re-entry
/// guard only rejected a call while State was Running, so calling ExecuteAsync again on an
/// already-Completed/Failed/Canceled operation silently restarted it from scratch instead of
/// throwing. An operation instance is meant to run exactly once - OperationManager.RunAsync
/// subscribes a fresh ProgressChanged/StateChanged handler pair on every call, so a second run on
/// the same instance would double up every progress/state event through the still-attached old
/// handlers.
/// </summary>
public class FileOperationReEntryGuardTests
{
    private sealed class InstantOperation : FileOperation
    {
        public override OperationType Type => OperationType.Copy;
        public override string Title => "Test";
        protected override Task ExecuteCoreAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Test]
    public async Task ExecuteAsync_CalledAgainAfterCompleted_Throws()
    {
        using var op = new InstantOperation();
        await op.ExecuteAsync();
        Assert.That(op.State, Is.EqualTo(OperationState.Completed));

        Assert.ThrowsAsync<InvalidOperationException>(async () => await op.ExecuteAsync(),
            "Re-running an already-Completed operation must throw, not silently restart it");
    }
}
