using System.IO.Compression;
using CoderCommander.FileSystem;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the silent-data-loss bug fixed in <see cref="MoveOperation"/>'s
/// cross-provider fallback: it used to delete the source once "something exists at the
/// destination path" - true even when CopyOperation Skipped the file because of a pre-existing
/// conflict at that exact path (the very reason the conflict was raised). A cross-volume/
/// cross-provider move (source on real disk, destination inside an archive - CanRenameInPlace is
/// false whenever the destination isn't LocalFileSystem, forcing the TransferAndDeleteAsync
/// fallback this test targets) where the user picks "Skip" used to delete the source file that
/// was never actually copied anywhere.
/// </summary>
public class MoveOperationSkipTests
{
    private string _sourceDir = "";
    private string _zipPath = "";

    [SetUp]
    public void CreateFixtures()
    {
        _sourceDir = Directory.CreateTempSubdirectory("cc_move_skip_src_").FullName;
        _zipPath = Path.Combine(Path.GetTempPath(), $"cc_move_skip_dest_{Guid.NewGuid():N}.zip");

        File.WriteAllText(Path.Combine(_sourceDir, "a.jpg"), "source content - must survive a Skip");

        using var zip = ZipFile.Open(_zipPath, ZipArchiveMode.Create);
        using (var entryStream = zip.CreateEntry("a.jpg").Open())
        using (var writer = new StreamWriter(entryStream))
            writer.Write("pre-existing destination content - must NOT be overwritten");
    }

    [TearDown]
    public void DeleteFixtures()
    {
        ZipArchiveFileSystem.Forget(_zipPath);
        if (Directory.Exists(_sourceDir)) Directory.Delete(_sourceDir, recursive: true);
        if (File.Exists(_zipPath)) File.Delete(_zipPath);
    }

    [Test]
    public async Task Move_SkippedConflictAcrossProviders_LeavesSourceFileIntact()
    {
        var sourcePath = Path.Combine(_sourceDir, "a.jpg");
        var sourceFs = new LocalFileSystem();
        var destFs = new ZipArchiveFileSystem(_zipPath);

        var files = new[] { new FileEntry(sourcePath, isDirectory: false, size: new FileInfo(sourcePath).Length) };
        var options = new TransferOptions
        {
            OverwriteResolver = (string _, string _, FileEntry _, FileEntry? _, out string? newName) =>
            {
                newName = null;
                return OverwriteAction.Skip;
            }
        };

        using var move = new MoveOperation(sourceFs, destFs, files, _sourceDir, ArchivePath.MakePath(_zipPath, ""), options);
        await move.ExecuteAsync();

        Assert.That(move.State, Is.EqualTo(OperationState.Completed));
        Assert.That(File.Exists(sourcePath), Is.True, "Skipped source file must not be deleted");
        Assert.That(File.ReadAllText(sourcePath), Is.EqualTo("source content - must survive a Skip"));

        using var zip = ZipFile.OpenRead(_zipPath);
        using var reader = new StreamReader(zip.GetEntry("a.jpg")!.Open());
        Assert.That(reader.ReadToEnd(), Is.EqualTo("pre-existing destination content - must NOT be overwritten"));
    }
}
