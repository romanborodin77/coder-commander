using System.IO.Compression;
using CoderCommander.FileSystem;

namespace CoderCommander.UiTests;

/// <summary>
/// Direct (no UI) tests for ZipArchiveFileSystem's retry-on-locked-file behavior, simulating what
/// happens when another process (antivirus, a second panel, Explorer) briefly holds the archive.
/// </summary>
public class ZipArchiveRetryTests
{
    private string _zipPath = "";

    [SetUp]
    public void CreateTestZip()
    {
        _zipPath = Path.Combine(Path.GetTempPath(), $"cc_retry_test_{Guid.NewGuid():N}.zip");
        using (var zip = ZipFile.Open(_zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("hello.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("hello");
        }
        ZipArchiveFileSystem.Forget(_zipPath);
    }

    [TearDown]
    public void DeleteTestZip()
    {
        ZipArchiveFileSystem.Forget(_zipPath);
        if (File.Exists(_zipPath)) File.Delete(_zipPath);
    }

    [Test]
    public void ReadDirectory_ConcurrentWithLock_RetriesAndSucceeds()
    {
        // The lock is still held when ReadDirectory is first called and only released by a
        // background thread partway through the retry sequence (150+300+600ms budget) -
        // exercising the actual retry loop, not just "already unlocked by the time we get there".
        using var handle = File.Open(_zipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var releaseThread = new Thread(() =>
        {
            Thread.Sleep(400); // inside the 150+300=450ms window before the 3rd retry
            handle.Dispose();
        });
        releaseThread.IsBackground = true;
        releaseThread.Start();

        var directory = ZipArchiveFileSystem.ReadDirectory(_zipPath);
        releaseThread.Join(TimeSpan.FromSeconds(2));

        Assert.That(directory.Entries, Has.Count.EqualTo(1), "Should recover the real listing once the lock clears, not fall back to Empty");
        Assert.That(directory.Entries[0].FullName, Is.EqualTo("hello.txt"));
    }

    [Test]
    public void ReadDirectory_LockOutlastsRetryBudget_FallsBackWithoutThrowing()
    {
        // Held for longer than the full retry budget (~1050ms) - must not throw, and (per the
        // stale-cache fallback fix) must not silently claim the archive is empty either once a
        // prior successful read exists in the cache.
        var primed = ZipArchiveFileSystem.ReadDirectory(_zipPath); // warms the cache with a real listing
        Assert.That(primed.Entries, Has.Count.EqualTo(1));
        // Touch the mtime so the cache's stamp check misses and a real re-parse is attempted below
        // (a Forget() here would also invalidate the very fallback entry this test is checking).
        File.SetLastWriteTimeUtc(_zipPath, DateTime.UtcNow);

        using var handle = File.Open(_zipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var releaseThread = new Thread(() =>
        {
            Thread.Sleep(1500);
            handle.Dispose();
        });
        releaseThread.IsBackground = true;
        releaseThread.Start();

        Assert.DoesNotThrow(() =>
        {
            var directory = ZipArchiveFileSystem.ReadDirectory(_zipPath);
            Assert.That(directory.Entries, Has.Count.EqualTo(1), "Should serve the last-known-good listing instead of reporting an empty archive");
        });

        releaseThread.Join(TimeSpan.FromSeconds(3));
    }

    [Test]
    public void OpenForUpdate_ConcurrentWithLock_RetriesAndSucceeds()
    {
        var handle = File.Open(_zipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var releaseThread = new Thread(() =>
        {
            Thread.Sleep(400);
            handle.Dispose();
        });
        releaseThread.IsBackground = true;
        releaseThread.Start();

        using var zip = ZipArchiveFileSystem.OpenForUpdate(_zipPath);
        releaseThread.Join(TimeSpan.FromSeconds(2));

        Assert.That(zip.Entries.Count, Is.EqualTo(1));
    }
}
