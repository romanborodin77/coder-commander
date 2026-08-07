using CoderCommander.FileSystem;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Direct (no UI) tests for <see cref="WipeOperation"/>'s failure handling: before this fix, a
/// failed secure-overwrite pass (locked file, permission error, disk I/O failure) was silently
/// swallowed and the file was deleted anyway - a caller asking for "unrecoverable deletion" got
/// an ordinary, recoverable delete with no visible sign the overwrite never happened.
/// </summary>
public class WipeOperationTests
{
    private string _path = "";
    private readonly LocalFileSystem _fs = new();

    [SetUp]
    public void CreateTestFile()
    {
        _path = Path.Combine(Path.GetTempPath(), $"cc_wipe_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(_path, "sensitive content that must be overwritten before deletion");
    }

    [TearDown]
    public void DeleteTestFile()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Test]
    public async Task WipeFile_OverwriteFails_LeavesFileOnDiskAndReportsFailure()
    {
        var entry = new FileEntry(_path, isDirectory: false, size: new FileInfo(_path).Length);

        // Hold the file open so WipeFile's own FileShare.None write-open cannot succeed -
        // simulates a locked/in-use file (e.g. an AV scan, or the file open elsewhere).
        using (File.OpenRead(_path))
        {
            var op = new WipeOperation(_fs, new[] { entry });
            await op.ExecuteAsync();

            Assert.That(op.State, Is.EqualTo(OperationState.Failed),
                "A failed overwrite must surface as a failed operation, not a silent success");
            Assert.That(op.LastError, Is.Not.Null);
        }

        Assert.That(File.Exists(_path), Is.True,
            "The file must not be deleted when it could not be securely overwritten first");
    }

    [Test]
    public async Task WipeFile_OverwriteSucceeds_DeletesFile()
    {
        var entry = new FileEntry(_path, isDirectory: false, size: new FileInfo(_path).Length);

        var op = new WipeOperation(_fs, new[] { entry });
        await op.ExecuteAsync();

        Assert.That(op.State, Is.EqualTo(OperationState.Completed), $"Wipe failed: {op.LastError}");
        Assert.That(File.Exists(_path), Is.False, "A successfully wiped file must be deleted");
    }
}
