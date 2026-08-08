using CoderCommander.FileSystem;
using CoderCommander.Operations;
using CoderCommander.ViewModels;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the missing containment guard fixed in
/// <see cref="MainViewModel.ExecuteTransfer"/>'s archive-destination branch: packing/moving a
/// folder into an archive file that physically lives inside that same folder had no protection
/// (unlike the plain-filesystem branch's existing IsDestinationInsideSource check). Without it,
/// PackOperation would write the archive into itself, and on Move,
/// PackOperation.RemoveSourcesAsync would then delete the whole source folder afterward -
/// including the archive it had just finished writing, destroying everything.
/// </summary>
public class ExecuteTransferPackContainmentTests
{
    private string _parentDir = "";
    private string _dataDir = "";

    [SetUp]
    public void CreateFixtures()
    {
        _parentDir = Directory.CreateTempSubdirectory("cc_pack_containment_test_").FullName;
        _dataDir = Directory.CreateDirectory(Path.Combine(_parentDir, "Data")).FullName;
        File.WriteAllText(Path.Combine(_dataDir, "keep.txt"), "must survive");
    }

    [TearDown]
    public void DeleteFixtures()
    {
        if (Directory.Exists(_parentDir)) Directory.Delete(_parentDir, recursive: true);
    }

    [Test]
    public void ExecuteTransfer_PackFolderIntoArchiveInsideItself_IsRejectedNotQueued()
    {
        using var vm = new MainViewModel();

        var archivePath = Path.Combine(_dataDir, "backup.zip");
        var entries = new[] { new FileEntry(_dataDir, isDirectory: true) };

        string? rejectedKey = null;
        vm.OperationRejected += (_, key) => rejectedKey = key;

        vm.ExecuteTransfer(new LocalFileSystem(), _parentDir, entries,
            ArchivePath.MakePath(archivePath, ""), new TransferOptions(), move: true);

        Assert.That(rejectedKey, Is.EqualTo("Transfer.SourceEqualsDestination"));

        // Nothing should have been queued/executed - the source folder and its file must be
        // completely untouched, and no archive should have been created at all.
        Assert.That(File.Exists(Path.Combine(_dataDir, "keep.txt")), Is.True);
        Assert.That(File.Exists(archivePath), Is.False);
    }
}
