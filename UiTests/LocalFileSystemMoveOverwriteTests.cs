using CoderCommander.FileSystem;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the destructive overwrite-retry bug fixed in
/// <see cref="LocalFileSystem.MoveAsync"/>: when the first move attempt failed and overwrite was
/// requested, the old code deleted the existing destination unconditionally, then retried - if
/// the retry also failed (the same transient condition that failed the first attempt, e.g. disk
/// space pressure during a cross-volume move, usually hadn't gone away), the destination ended up
/// permanently gone with nothing having replaced it. Forces this deterministically by locking the
/// source file (FileShare.None) so both the first attempt and the retry fail for the same reason,
/// and verifies the destination's original content survives.
/// </summary>
public class LocalFileSystemMoveOverwriteTests
{
    private string _dir = "";

    [SetUp]
    public void CreateTempDir()
    {
        _dir = Directory.CreateTempSubdirectory("cc_move_overwrite_test_").FullName;
    }

    [TearDown]
    public void DeleteTempDir()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public void MoveAsync_OverwriteRetryAlsoFails_RestoresOriginalDestinationContent()
    {
        var sourcePath = Path.Combine(_dir, "source.txt");
        var destPath = Path.Combine(_dir, "dest.txt");
        File.WriteAllText(sourcePath, "source content");
        File.WriteAllText(destPath, "original destination content - must survive a failed retry");

        var fs = new LocalFileSystem();

        // Locking the source (not the destination) makes both the first attempt and the retry
        // fail for the identical reason (can't move a locked source file), deterministically
        // reproducing "retry also fails" without needing to simulate real disk-space exhaustion.
        using (new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAsync<IOException>(async () => await fs.MoveAsync(sourcePath, destPath, overwrite: true));
        }

        Assert.That(File.Exists(destPath), Is.True, "Destination must not end up permanently gone after a failed retry");
        Assert.That(File.ReadAllText(destPath), Is.EqualTo("original destination content - must survive a failed retry"));
        Assert.That(File.Exists(sourcePath), Is.True, "Source was never actually moved, since every attempt failed");

        var strayBackups = Directory.GetFiles(_dir, "dest.txt.bak-*");
        Assert.That(strayBackups, Is.Empty, "A successful restore must clean up its own backup file");
    }

    [Test]
    public async Task MoveAsync_OverwriteSucceedsOnRetry_ReplacesDestinationAndLeavesNoBackupFile()
    {
        var sourcePath = Path.Combine(_dir, "source.txt");
        var destPath = Path.Combine(_dir, "dest.txt");
        File.WriteAllText(sourcePath, "new content");
        File.WriteAllText(destPath, "old content");

        var fs = new LocalFileSystem();
        await fs.MoveAsync(sourcePath, destPath, overwrite: true);

        Assert.That(File.Exists(sourcePath), Is.False);
        Assert.That(File.ReadAllText(destPath), Is.EqualTo("new content"));

        var strayBackups = Directory.GetFiles(_dir, "dest.txt.bak-*");
        Assert.That(strayBackups, Is.Empty, "A successful overwrite must not leave a stray backup file behind");
    }
}
