using CoderCommander.FileSystem;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the double-copy bug fixed in <see cref="CopyOperation.FlattenAsync"/>:
/// it had no dedup, unlike PackOperation.BuildPlanAsync's `seen` set. Flat View (Ctrl+P) lets the
/// user select both a folder and a file already nested inside it in the same operation - without
/// dedup, that file's destination path was planned twice (once via the folder's own recursive
/// walk, once as its own selection root), causing a spurious self-conflict and inflated totals.
/// </summary>
public class CopyOperationFlattenDedupTests
{
    private string _root = "";

    [SetUp]
    public void CreateTempTree()
    {
        _root = Directory.CreateTempSubdirectory("cc_flatten_dedup_test_").FullName;
        Directory.CreateDirectory(Path.Combine(_root, "Proj"));
        File.WriteAllText(Path.Combine(_root, "Proj", "notes.txt"), "hello");
    }

    [TearDown]
    public void DeleteTempTree()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task FlattenAsync_FolderAndNestedFileBothSelected_PlansTheFileOnlyOnce()
    {
        var sourceFs = new LocalFileSystem();
        var projPath = Path.Combine(_root, "Proj");
        var notesPath = Path.Combine(_root, "Proj", "notes.txt");

        // Simulates a Flat View Ctrl-selection of both the folder and a file already nested
        // inside it.
        var roots = new[]
        {
            new FileEntry(projPath, isDirectory: true),
            new FileEntry(notesPath, isDirectory: false, size: new FileInfo(notesPath).Length)
        };

        var plan = await CopyOperation.FlattenAsync(sourceFs, roots, _root, @"D:\dest", CancellationToken.None);

        var notesEntries = plan.Where(p => !p.Entry.IsDirectory && p.Entry.FullPath.Equals(notesPath, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.That(notesEntries.Count, Is.EqualTo(1),
            "notes.txt must appear exactly once in the plan, not once from the folder walk and once as its own selection root");
    }
}
