using CoderCommander.FileSystem;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the zero-partial-failure-tolerance bug fixed in <see cref="CopyOperation"/>:
/// a single locked/inaccessible file used to abort the ENTIRE copy - nothing caught an exception
/// from CopyFileWithProgress, so it propagated straight out of ExecuteCoreAsync and every
/// remaining planned file (potentially thousands, alphabetically after the failed one) was never
/// even attempted. Unlike Pack/Unpack, which already tolerate this.
/// </summary>
public class CopyOperationPartialFailureTests
{
    private string _sourceDir = "";
    private string _destDir = "";

    [SetUp]
    public void CreateFixtures()
    {
        _sourceDir = Directory.CreateTempSubdirectory("cc_copy_partial_src_").FullName;
        File.WriteAllText(Path.Combine(_sourceDir, "a.txt"), "a-new");
        File.WriteAllText(Path.Combine(_sourceDir, "b.txt"), "b-new");
        File.WriteAllText(Path.Combine(_sourceDir, "c.txt"), "c-new");

        _destDir = Directory.CreateTempSubdirectory("cc_copy_partial_dest_").FullName;
        var lockedPath = Path.Combine(_destDir, "b.txt");
        File.WriteAllText(lockedPath, "b-original - must survive");
        File.SetAttributes(lockedPath, FileAttributes.ReadOnly);
    }

    [TearDown]
    public void DeleteFixtures()
    {
        var lockedPath = Path.Combine(_destDir, "b.txt");
        if (File.Exists(lockedPath)) File.SetAttributes(lockedPath, FileAttributes.Normal);
        if (Directory.Exists(_sourceDir)) Directory.Delete(_sourceDir, recursive: true);
        if (Directory.Exists(_destDir)) Directory.Delete(_destDir, recursive: true);
    }

    [Test]
    public async Task Copy_OneFileFailsMidBatch_StillCopiesTheRestInsteadOfAbortingEntirely()
    {
        var fs = new LocalFileSystem();
        var files = new[]
        {
            new FileEntry(Path.Combine(_sourceDir, "a.txt"), isDirectory: false),
            new FileEntry(Path.Combine(_sourceDir, "b.txt"), isDirectory: false),
            new FileEntry(Path.Combine(_sourceDir, "c.txt"), isDirectory: false)
        };

        using var copy = new CopyOperation(fs, fs, files, _sourceDir, _destDir,
            new TransferOptions { Overwrite = true });
        await copy.ExecuteAsync();

        Assert.That(copy.State, Is.EqualTo(OperationState.Failed),
            $"Must report Failed once something couldn't be copied (LastError: {copy.LastError?.Message})");

        Assert.That(File.ReadAllText(Path.Combine(_destDir, "a.txt")), Is.EqualTo("a-new"),
            "A file alphabetically before the failed one must still be copied");
        Assert.That(File.ReadAllText(Path.Combine(_destDir, "c.txt")), Is.EqualTo("c-new"),
            "A file alphabetically after the failed one must still be copied - the old code would have aborted before ever reaching it");
        Assert.That(File.ReadAllText(Path.Combine(_destDir, "b.txt")), Is.EqualTo("b-original - must survive"),
            "The locked file's original destination content must be untouched");
    }
}
