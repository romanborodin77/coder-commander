using CoderCommander.FileSystem;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the spurious-failure bug fixed in <see cref="MoveOperation"/>: unlike
/// CopyOperation (which flattens into individual file-level plan entries and dedups there), a
/// directory selected for Move is relocated whole via a single Directory.Move call. If the same
/// selection also separately lists a file already nested inside that directory (Flat View allows
/// selecting both a folder and a file inside it), the directory move already relocates that file -
/// so the redundant separate entry then fails outright once the loop reaches it (its source is
/// gone), and that failure used to propagate out and mark the WHOLE move Failed, even though
/// everything the user selected had, in fact, already been moved successfully.
/// </summary>
public class MoveOperationRedundantSelectionTests
{
    private string _sourceParent = "";
    private string _projDir = "";
    private string _destParent = "";

    [SetUp]
    public void CreateFixtures()
    {
        _sourceParent = Directory.CreateTempSubdirectory("cc_move_redundant_src_").FullName;
        _projDir = Directory.CreateDirectory(Path.Combine(_sourceParent, "Proj")).FullName;
        File.WriteAllText(Path.Combine(_projDir, "notes.txt"), "content");
        _destParent = Directory.CreateTempSubdirectory("cc_move_redundant_dest_").FullName;
    }

    [TearDown]
    public void DeleteFixtures()
    {
        if (Directory.Exists(_sourceParent)) Directory.Delete(_sourceParent, recursive: true);
        if (Directory.Exists(_destParent)) Directory.Delete(_destParent, recursive: true);
    }

    [Test]
    public void RemoveEntriesInsideSelectedDirectories_FileNestedInSelectedFolder_IsDropped()
    {
        var notesPath = Path.Combine(_projDir, "notes.txt");
        var files = new FileEntry[]
        {
            new(notesPath, isDirectory: false),
            new(_projDir, isDirectory: true)
        };

        var filtered = MoveOperation.RemoveEntriesInsideSelectedDirectories(files);

        Assert.That(filtered.Select(f => f.FullPath), Is.EquivalentTo(new[] { _projDir }),
            "The file nested inside the selected folder must be dropped - the folder move already relocates it");
    }

    [Test]
    public async Task Move_FolderAndNestedFileBothSelected_CompletesInsteadOfFailing()
    {
        var notesPath = Path.Combine(_projDir, "notes.txt");
        var files = new FileEntry[]
        {
            new(notesPath, isDirectory: false),
            new(_projDir, isDirectory: true)
        };

        var fs = new LocalFileSystem();
        using var move = new MoveOperation(fs, fs, files, _sourceParent, _destParent);
        await move.ExecuteAsync();

        Assert.That(move.State, Is.EqualTo(OperationState.Completed),
            $"Move must complete, not fail, when the selection redundantly lists a file already inside a selected folder (LastError: {move.LastError?.Message})");
        Assert.That(File.Exists(Path.Combine(_destParent, "Proj", "notes.txt")), Is.True);
        Assert.That(Directory.Exists(_projDir), Is.False, "The source folder must have been moved, not left behind");
    }
}
