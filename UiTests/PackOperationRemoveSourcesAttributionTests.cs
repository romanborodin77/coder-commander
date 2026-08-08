using System.IO.Compression;
using CoderCommander.FileSystem;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the misattribution bug fixed in
/// <see cref="PackOperation.RemoveSourcesAsync"/>: it used to decide whether a file was "part of"
/// a selected top-level folder via BuildPlanAsync's TopLevelIndex, which the `seen` dedup can
/// attribute to whichever selection index reached a given file FIRST - not necessarily the folder
/// that actually contains it on disk (e.g. both a file and its containing folder are selected
/// together, as Flat View allows). A file that failed to write into the archive (Skip conflict)
/// could then be silently excluded from its containing folder's "did everything get written?"
/// check, and the recursive delete for that folder swept it up anyway.
/// </summary>
public class PackOperationRemoveSourcesAttributionTests
{
    private string _parentDir = "";
    private string _projDir = "";
    private string _zipPath = "";

    [SetUp]
    public void CreateFixtures()
    {
        _parentDir = Directory.CreateTempSubdirectory("cc_pack_attribution_test_").FullName;
        _projDir = Directory.CreateDirectory(Path.Combine(_parentDir, "Proj")).FullName;
        File.WriteAllText(Path.Combine(_projDir, "notes.txt"), "must survive - never written to the archive");
        // A second file with no conflict, so at least one file in the plan actually gets written
        // (RemoveSourcesAsync only runs when PackOperation's own `written` counter is > 0).
        File.WriteAllText(Path.Combine(_projDir, "other.txt"), "written fine, safe to remove from source");

        // A pre-existing "Proj/notes.txt" entry in the destination archive, matching the entry
        // name BuildPlanAsync will compute for the packed file - with no OverwriteResolver and
        // Overwrite=false (both defaults), this makes PackOperation's clash resolution Skip this
        // one file, so it never actually gets written (while other.txt writes normally).
        _zipPath = Path.Combine(_parentDir, "archive.zip");
        using var zip = ZipFile.Open(_zipPath, ZipArchiveMode.Create);
        zip.CreateEntry("Proj/notes.txt");
    }

    [TearDown]
    public void DeleteFixtures()
    {
        if (Directory.Exists(_parentDir)) Directory.Delete(_parentDir, recursive: true);
    }

    [Test]
    public async Task RemoveSourcesAsync_FileAndItsContainingFolderBothSelected_SkippedFileSurvives()
    {
        var notesPath = Path.Combine(_projDir, "notes.txt");

        // The file is selected directly AND its containing folder is also selected - the Flat
        // View scenario that lets BuildPlanAsync's dedup attribute notes.txt to the wrong index.
        var files = new[]
        {
            new FileEntry(notesPath, isDirectory: false),
            new FileEntry(_projDir, isDirectory: true)
        };

        using var pack = new PackOperation(new LocalFileSystem(), files, _parentDir, _zipPath,
            options: new TransferOptions(), removeSource: true);
        await pack.ExecuteAsync();

        Assert.That(File.Exists(notesPath), Is.True,
            "A file that was never actually written to the archive must not be deleted just because its containing folder's other content was");
        Assert.That(Directory.Exists(_projDir), Is.True,
            "The containing folder must not be recursively deleted while it still holds an un-written file");
        Assert.That(File.Exists(Path.Combine(_projDir, "other.txt")), Is.False,
            "Sanity check: the file that WAS written must still be removed - this isn't just RemoveSourcesAsync never running at all");
    }
}
