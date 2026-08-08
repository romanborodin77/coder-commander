using System.IO.Compression;
using CoderCommander.FileSystem;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the swallowed-extraction-failure bug fixed in
/// <see cref="UnpackOperation"/>: ExtractAsync caught every non-cancellation exception, logged a
/// warning, and returned false - ProcessRecordAsync just moved on, and ExecuteCoreAsync finished
/// normally, reporting OperationState.Completed even though one or more entries never actually
/// made it to disk. The same failure-swallowed-to-Completed shape already fixed for WipeOperation
/// and CopyOperation this round.
/// </summary>
public class UnpackOperationFailureReportingTests
{
    private string _zipPath = "";
    private string _destDir = "";

    [SetUp]
    public void CreateFixtures()
    {
        _zipPath = Path.Combine(Path.GetTempPath(), $"cc_unpack_failure_test_{Guid.NewGuid():N}.zip");
        using (var zip = ZipFile.Open(_zipPath, ZipArchiveMode.Create))
        {
            using (var s = zip.CreateEntry("ok.txt").Open())
            using (var w = new StreamWriter(s)) w.Write("fine");
            using (var s = zip.CreateEntry("locked.txt").Open())
            using (var w = new StreamWriter(s)) w.Write("new content that must not land");
        }

        _destDir = Directory.CreateTempSubdirectory("cc_unpack_failure_dest_").FullName;
        var lockedPath = Path.Combine(_destDir, "locked.txt");
        File.WriteAllText(lockedPath, "original content - must survive");
        File.SetAttributes(lockedPath, FileAttributes.ReadOnly);
    }

    [TearDown]
    public void DeleteFixtures()
    {
        var lockedPath = Path.Combine(_destDir, "locked.txt");
        if (File.Exists(lockedPath)) File.SetAttributes(lockedPath, FileAttributes.Normal);
        if (Directory.Exists(_destDir)) Directory.Delete(_destDir, recursive: true);
        if (File.Exists(_zipPath)) File.Delete(_zipPath);
    }

    [Test]
    public async Task Unpack_OneEntryFailsToExtract_ReportsFailedNotCompleted()
    {
        var destFs = new LocalFileSystem();
        using var unpack = new UnpackOperation(_zipPath, Array.Empty<FileEntry>(), "", destFs, _destDir,
            new TransferOptions { Overwrite = true });
        await unpack.ExecuteAsync();

        Assert.That(unpack.State, Is.EqualTo(OperationState.Failed),
            $"Must not report Completed when an entry failed to extract (LastError: {unpack.LastError?.Message})");
        Assert.That(File.ReadAllText(Path.Combine(_destDir, "ok.txt")), Is.EqualTo("fine"),
            "The entry that succeeded must still have been extracted");
        Assert.That(File.ReadAllText(Path.Combine(_destDir, "locked.txt")), Is.EqualTo("original content - must survive"),
            "The read-only file must be untouched, since its extraction failed");
    }
}
