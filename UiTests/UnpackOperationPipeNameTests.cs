using System.IO.Compression;
using CoderCommander.FileSystem;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the bug fixed in <see cref="UnpackOperation.ExtractAsync"/>: an archive
/// entry name containing '|' (legal for a ZIP/TAR entry, illegal in a real Windows path/filename)
/// used to abort the whole extraction once it reached disk. VfsPath.IsArchive's bare
/// '|'-substring check misread the combined destination path (_destPath + entry name) as
/// VFS-flavored ("archive.zip|inner/path"), so VfsPath.GetParent split it on the wrong character
/// and handed CreateDirectoryAsync a mangled, invalid path (e.g. "C:\dest\a|" instead of
/// "C:\dest"), throwing ERROR_INVALID_NAME <em>outside</em> ExtractAsync's own try/catch - which
/// aborted extraction of every entry still queued behind the pipe-named one. The pipe-named entry
/// itself still can't be written (NTFS disallows '|' in a filename, same as pre-fix), so the
/// operation is still expected to report Failed for that one entry - what changed is that the
/// entry queued after it must no longer be collateral damage.
/// </summary>
public class UnpackOperationPipeNameTests
{
    private string _zipPath = "";
    private string _destDir = "";

    [SetUp]
    public void CreateFixtures()
    {
        _zipPath = Path.Combine(Path.GetTempPath(), $"cc_unpack_pipe_test_{Guid.NewGuid():N}.zip");
        using (var zip = ZipFile.Open(_zipPath, ZipArchiveMode.Create))
        {
            using (var s = zip.CreateEntry("a|b.txt").Open())
            using (var w = new StreamWriter(s)) w.Write("pipe-named entry");
            using (var s = zip.CreateEntry("after.txt").Open())
            using (var w = new StreamWriter(s)) w.Write("comes after alphabetically");
        }

        _destDir = Directory.CreateTempSubdirectory("cc_unpack_pipe_dest_").FullName;
    }

    [TearDown]
    public void DeleteFixtures()
    {
        if (Directory.Exists(_destDir)) Directory.Delete(_destDir, recursive: true);
        if (File.Exists(_zipPath)) File.Delete(_zipPath);
    }

    [Test]
    public async Task Unpack_EntryNameContainsPipe_DoesNotAbortExtractionOfLaterEntries()
    {
        var destFs = new LocalFileSystem();
        using var unpack = new UnpackOperation(_zipPath, Array.Empty<FileEntry>(), "", destFs, _destDir,
            new TransferOptions { Overwrite = true });
        await unpack.ExecuteAsync();

        // The pipe-named entry itself can never land on NTFS ('|' is an illegal filename
        // character), so Failed here is expected and correct - the bug was that this single
        // unwritable entry used to take the rest of the archive down with it.
        Assert.That(unpack.State, Is.EqualTo(OperationState.Failed));
        Assert.That(File.Exists(Path.Combine(_destDir, "after.txt")), Is.True,
            "An entry queued after the pipe-named one must still be extracted, not aborted");
    }
}
