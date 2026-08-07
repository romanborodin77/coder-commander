using System.IO.Compression;
using CoderCommander.FileSystem;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the silent-data-loss bug fixed in <see cref="ConflictResolver"/>'s Rename
/// handling: it used to trust a resolver-supplied name verbatim with no re-check against the real
/// destination. The one shipped resolver (MainForm.GenerateUniqueName) verifies uniqueness against
/// the real Windows disk via File.Exists/Directory.Exists - meaningless for an archive-backed
/// IFileSystem, whose VFS paths use '|' (mangled by Path.GetDirectoryName), so it would almost
/// always report a name "unique" even when that exact name already existed inside the target
/// archive. This simulates that broken resolver directly (returning a name colliding with a real
/// archive entry) and verifies ConflictResolver now catches and corrects it instead of trusting it.
/// </summary>
public class ConflictResolverRenameTests
{
    private string _zipPath = "";

    [SetUp]
    public void CreateTestZip()
    {
        _zipPath = Path.Combine(Path.GetTempPath(), $"cc_conflict_rename_test_{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(_zipPath, ZipArchiveMode.Create);
        zip.CreateEntry("notes.txt");
        zip.CreateEntry("notes (1).txt"); // pre-existing "already renamed once" entry
    }

    [TearDown]
    public void DeleteTestZip()
    {
        ZipArchiveFileSystem.Forget(_zipPath);
        if (File.Exists(_zipPath)) File.Delete(_zipPath);
    }

    [Test]
    public async Task ResolveAsync_RenameSuggestionCollidesWithRealArchiveEntry_PicksAnActuallyFreeName()
    {
        var destFs = new ZipArchiveFileSystem(_zipPath);
        var destPath = ArchivePath.MakePath(_zipPath, "notes.txt");
        var sourceInfo = new FileEntry(@"C:\somewhere\notes.txt", isDirectory: false);

        var options = new TransferOptions
        {
            // Simulates the broken real-disk-based GenerateUniqueName: it suggests "notes (1).txt"
            // believing it's free, without ever checking the archive that actually matters.
            OverwriteResolver = (string _, string _, FileEntry _, FileEntry? _, out string? newName) =>
            {
                newName = "notes (1).txt";
                return OverwriteAction.Rename;
            }
        };

        var resolution = await ConflictResolver.ResolveAsync(
            destFs, sourceInfo.FullPath, destPath, sourceInfo, options, CancellationToken.None);

        Assert.That(resolution.Proceed, Is.True);
        Assert.That(resolution.Overwrite, Is.False);

        Assert.That(resolution.TargetPath, Is.Not.EqualTo(ArchivePath.MakePath(_zipPath, "notes (1).txt")),
            "Must not reuse a name that already exists in the archive - that would silently delete the real entry");

        // The pre-existing "notes (1).txt" entry must still be untouched (ConflictResolver only
        // decides the target path; it doesn't write anything itself).
        using var zip = ZipFile.OpenRead(_zipPath);
        Assert.That(zip.Entries.Any(e => e.FullName == "notes (1).txt"), Is.True);
    }

    /// <summary>Regression test for a bug an ultrareview pass caught in this same fix: the
    /// counter-based fallback derived the extension via FileEntry.GetExtension, which lowercases
    /// on purpose (it's meant for extension comparisons) - silently rewriting e.g. "Report.PDF"
    /// to "Report (1).pdf". Cosmetic on Windows/OrdinalIgnoreCase archive VFSes, but a genuinely
    /// different filename on a case-sensitive destination.</summary>
    [Test]
    public async Task ResolveAsync_RenameFallbackForUppercaseExtension_PreservesOriginalCasing()
    {
        using (var zip = ZipFile.Open(_zipPath, ZipArchiveMode.Update))
            zip.CreateEntry("Report.PDF");

        var destFs = new ZipArchiveFileSystem(_zipPath);
        var destPath = ArchivePath.MakePath(_zipPath, "Report.PDF");
        var sourceInfo = new FileEntry(@"C:\somewhere\Report.PDF", isDirectory: false);

        // No suggested name at all - forces the counter-based fallback loop, not the
        // suggested-name branch.
        var options = new TransferOptions
        {
            OverwriteResolver = (string _, string _, FileEntry _, FileEntry? _, out string? newName) =>
            {
                newName = null;
                return OverwriteAction.Rename;
            }
        };

        var resolution = await ConflictResolver.ResolveAsync(
            destFs, sourceInfo.FullPath, destPath, sourceInfo, options, CancellationToken.None);

        Assert.That(resolution.TargetPath, Is.EqualTo(ArchivePath.MakePath(_zipPath, "Report (1).PDF")),
            "The fallback-generated name must preserve the original extension's casing, not lowercase it");
    }
}
