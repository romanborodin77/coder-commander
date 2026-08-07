using System.IO.Compression;
using CoderCommander.FileSystem;

namespace CoderCommander.UiTests;

/// <summary>
/// Direct (no UI) tests for <see cref="ZipArchiveFileSystem.ZipUpdateSession"/>'s explicit-commit
/// guard: before it existed, <c>Dispose</c> always replaced the real archive with whatever had
/// been staged, even when the caller never finished (or threw partway through) its own write -
/// see the session's own doc comment. These mirror
/// <c>RewritingArchiveWriterTests.AbandonedSession_*</c>, which cover the same property for
/// TAR/TAR.GZ.
/// </summary>
public class ZipUpdateSessionCommitTests
{
    private string _zipPath = "";

    [SetUp]
    public void CreateTestZip()
    {
        _zipPath = Path.Combine(Path.GetTempPath(), $"cc_zip_commit_test_{Guid.NewGuid():N}.zip");
        using (var zip = ZipFile.Open(_zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("original.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("original content");
        }
        ZipArchiveFileSystem.Forget(_zipPath);
    }

    [TearDown]
    public void DeleteTestZip()
    {
        ZipArchiveFileSystem.Forget(_zipPath);
        if (File.Exists(_zipPath)) File.Delete(_zipPath);
    }

    private List<string> ReadEntryNames()
    {
        using var zip = ZipFile.OpenRead(_zipPath);
        return zip.Entries.Select(e => e.FullName).ToList();
    }

    [Test]
    public void AbandonedSession_WithoutCommit_LeavesOriginalArchiveByteForByteUnchanged()
    {
        var originalBytes = File.ReadAllBytes(_zipPath);

        // Stage a new entry but deliberately never call session.Commit() - `using` disposes the
        // session as if an exception had aborted the operation partway through.
        using (var session = ZipArchiveFileSystem.OpenForUpdate(_zipPath, new[] { "should-not-appear.txt" }))
        {
            var entry = session.Archive.CreateEntry("should-not-appear.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("this must not end up in the archive");
        }

        var afterAbandonedSession = File.ReadAllBytes(_zipPath);
        Assert.That(afterAbandonedSession, Is.EqualTo(originalBytes),
            "Original archive must be untouched when the session is disposed without committing");

        var names = ReadEntryNames();
        Assert.That(names, Does.Contain("original.txt"));
        Assert.That(names, Does.Not.Contain("should-not-appear.txt"));
    }

    [Test]
    public void AbandonedSession_ThrowingMidWrite_LeavesOriginalArchiveIntact()
    {
        var originalBytes = File.ReadAllBytes(_zipPath);

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var session = ZipArchiveFileSystem.OpenForUpdate(_zipPath, new[] { "partial.txt" });
            var entry = session.Archive.CreateEntry("partial.txt");
            using (var writer = new StreamWriter(entry.Open()))
                writer.Write("half-written");
            throw new InvalidOperationException("simulated failure mid-pack");
            // session.Commit() is never reached.
        });

        var afterFailure = File.ReadAllBytes(_zipPath);
        Assert.That(afterFailure, Is.EqualTo(originalBytes));
    }

    [Test]
    public void Committed_WritesPersist()
    {
        using (var session = ZipArchiveFileSystem.OpenForUpdate(_zipPath, new[] { "added.txt" }))
        {
            var entry = session.Archive.CreateEntry("added.txt");
            using (var writer = new StreamWriter(entry.Open()))
                writer.Write("added content");
            session.Commit();
        }

        var names = ReadEntryNames();
        Assert.That(names, Does.Contain("original.txt"), "Prior entries must survive a committed update");
        Assert.That(names, Does.Contain("added.txt"));
    }
}
