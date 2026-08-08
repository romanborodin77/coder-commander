using CoderCommander.FileSystem;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the bug fixed in <see cref="LocalFileSystem.DeleteAsync"/>: a recursive
/// directory delete used to abort the instant it reached a read-only file, since
/// Directory.Delete(path, recursive: true) throws UnauthorizedAccessException on one - leaving an
/// unpredictable subset of the tree destroyed (traversal order is unspecified) and no way to tell
/// what survived. Explorer/Shift+Del clears ReadOnly first; DeleteAsync now does the same.
/// </summary>
public class LocalFileSystemDeleteReadOnlyTests
{
    private string _dir = "";

    [SetUp]
    public void CreateFixtures()
    {
        _dir = Directory.CreateTempSubdirectory("cc_delete_readonly_").FullName;
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "b");
        File.SetAttributes(Path.Combine(_dir, "b.txt"), FileAttributes.ReadOnly);
        File.WriteAllText(Path.Combine(_dir, "c.txt"), "c");
    }

    [TearDown]
    public void DeleteFixtures()
    {
        if (Directory.Exists(_dir))
        {
            foreach (var f in Directory.EnumerateFiles(_dir))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Test]
    public async Task DeleteAsync_TreeContainsReadOnlyFile_DeletesTheWholeTree()
    {
        var fs = new LocalFileSystem();
        await fs.DeleteAsync(_dir, recursive: true);

        Assert.That(Directory.Exists(_dir), Is.False,
            "The whole tree - including the read-only file - must be gone, not just the entries deleted before hitting it");
    }
}
