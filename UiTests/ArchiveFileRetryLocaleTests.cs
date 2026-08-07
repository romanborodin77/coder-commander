using CoderCommander.Archives;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the locale-dependence fix in <see cref="ArchiveFileRetry"/>: the retry
/// used to key off <c>IOException.Message.Contains("being used by another process")</c>, but
/// that text is localized by the OS/CLR - on a non-English Windows install the substring never
/// matches, silently disabling the very retry that exists to survive AV/indexer locks. It now
/// checks <c>ex.HResult</c> against Win32 ERROR_SHARING_VIOLATION instead, which is
/// locale-independent. This proves the HResult check actually matches a real sharing-violation
/// exception thrown by a locked file - the retry engages and succeeds once the lock clears,
/// rather than propagating immediately as it would if the `when` clause failed to match.
/// </summary>
public class ArchiveFileRetryLocaleTests
{
    private string _path = "";

    [SetUp]
    public void CreateTestFile()
    {
        _path = Path.Combine(Path.GetTempPath(), $"cc_retry_locale_test_{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(_path, new byte[] { 0x50, 0x4B, 0x05, 0x06 }); // minimal empty-zip EOCD
    }

    [TearDown]
    public void DeleteTestFile()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Test]
    public async Task OpenReadWithRetry_SharingViolationClearsDuringRetry_SucceedsWithoutMatchingMessageText()
    {
        var exclusiveLock = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var openTask = Task.Run(() => ArchiveFileRetry.OpenReadWithRetry(_path));

        // First retry delay is 150ms; release well before the retry budget (4.7s total) is exhausted
        // so the only way OpenReadWithRetry can succeed is if its `when (IsSharingViolation(ex))`
        // clause matched the real exception and actually retried instead of propagating immediately.
        await Task.Delay(400);
        exclusiveLock.Dispose();

        using var opened = await openTask;
        Assert.That(opened, Is.Not.Null);
    }
}
