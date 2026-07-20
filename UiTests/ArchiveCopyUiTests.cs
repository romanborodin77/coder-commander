using System.Formats.Tar;
using System.IO.Compression;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace CoderCommander.UiTests;

/// <summary>
/// End-to-end (real app, FlaUI) coverage for copying files into and out of an archive through the
/// actual F5 Copy command/panel path - not by constructing <c>PackOperation</c>/<c>UnpackOperation</c>
/// directly (already covered elsewhere), but by driving two real panels the way a user would: one
/// panel browsing a real folder, the other having entered an archive as its current location (see
/// <c>ArchiveAsPanelEntryTests</c>), then pressing F5 and confirming.
/// <para>
/// <see cref="CoderCommander.ViewModels.MainViewModel.Copy"/> always reads
/// <c>InactivePanel.CurrentPath</c> as the destination (and <c>ActivePanel</c> for the source), so
/// whichever panel is inside the archive when F5 is pressed decides the copy direction - this is
/// exactly what a real user does to move things in or out of an archive, with no dedicated
/// "copy to archive" command of its own.
/// </para>
/// <para>
/// Panels are switched by clicking directly inside the target panel's ListView (found by on-screen
/// left/right position, re-queried fresh every time) rather than with Tab: a Tab-toggle sequence
/// that works fine between two plain folders (see <c>FullExplorationTests.Copy_CopiesFileToOtherPanel</c>)
/// was found empirically to misfire once one panel has entered an archive - likely because
/// switching a panel's <c>IFileSystem</c> rebuilds its ListView and drops the focus chain Tab relies
/// on. Clicking a specific panel's list directly sidesteps that regardless of cause.
/// </para>
/// <para>
/// Each test keeps the two panels pointed at different real folders throughout (a source dir and a
/// separate archive dir / extract dir) - never the same folder in both panels at once - because
/// <see cref="UiTestBase.SelectItemByName"/> matches by name across the whole window and would
/// otherwise risk clicking the wrong panel's identically-named item.
/// </para>
/// </summary>
public class ArchiveCopyUiTests : UiTestBase
{
    private DirectoryInfo _sourceDir = null!;
    private DirectoryInfo _archiveDir = null!;
    private DirectoryInfo _extractDir = null!;

    [SetUp]
    public override void Launch()
    {
        base.Launch();
        _sourceDir = Directory.CreateTempSubdirectory("cc_archive_copy_src_");
        _archiveDir = Directory.CreateTempSubdirectory("cc_archive_copy_arc_");
        _extractDir = Directory.CreateTempSubdirectory("cc_archive_copy_dst_");
    }

    [TearDown]
    public override void Cleanup()
    {
        base.Cleanup();
        foreach (var dir in new[] { _sourceDir, _archiveDir, _extractDir })
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteZipFixture(string path, string entryName, string content)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void WriteTarFixture(string path, string entryName, string content)
    {
        using var fileStream = File.Create(path);
        using var writer = new TarWriter(fileStream, TarEntryFormat.Pax, leaveOpen: false);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
        {
            DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content))
        };
        writer.WriteEntry(entry);
    }

    private bool AnyPathBarContains(string substring) =>
        MainWindow!.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
            .Any(e =>
            {
                try { return e.AsTextBox().Text?.Contains(substring, StringComparison.OrdinalIgnoreCase) == true; }
                catch { return false; }
            });

    /// <summary>The two panel ListViews ordered by on-screen X position - re-queried fresh on
    /// every call rather than cached, since entering an archive can recreate the control.</summary>
    private AutomationElement[] GetPanelListsLeftToRight() =>
        MainWindow!.FindAllDescendants(cf => cf.ByControlType(ControlType.List))
            .OrderBy(e => e.BoundingRectangle.X)
            .ToArray();

    /// <summary>Makes the panel at <paramref name="leftToRightIndex"/> (0 = left, 1 = right) the
    /// active one by clicking directly inside its ListView.</summary>
    private void ActivatePanel(int leftToRightIndex)
    {
        var lists = GetPanelListsLeftToRight();
        Assert.That(lists.Length, Is.EqualTo(2), "Expected exactly two panel ListViews");
        lists[leftToRightIndex].Click();
        Thread.Sleep(150);
    }

    /// <summary>Navigates whichever panel is currently active into an archive by name, and waits
    /// until its path bar shows the archive's VFS root.</summary>
    private void EnterArchiveInActivePanel(string archivePath)
    {
        NavigateActivePanelTo(Path.GetDirectoryName(archivePath)!);
        SelectItemByName(Path.GetFileName(archivePath));
        Keyboard.Press(VirtualKeyShort.ENTER);
        Retry.WhileFalse(() => AnyPathBarContains(archivePath + "|"), TimeSpan.FromSeconds(5));
    }

    private void PressF5AndConfirm()
    {
        var confirmDlg = PressUntilModalAppears(() => Keyboard.Press(VirtualKeyShort.F5));
        var okBtn = confirmDlg.FindFirstDescendant(cf => cf.ByName("OK").Or(cf.ByName("ОК")))?.AsButton();
        Assert.That(okBtn, Is.Not.Null, "Copy confirm OK button not found");
        okBtn!.Invoke();
    }

    [Test]
    public void CopyingARealFileIntoAZipArchive_ViaF5_AddsItToTheArchive()
    {
        var archivePath = Path.Combine(_archiveDir.FullName, "container.zip");
        WriteZipFixture(archivePath, "existing.txt", "already inside");
        File.WriteAllText(Path.Combine(_sourceDir.FullName, "newfile.txt"), "copied into zip via F5");

        ActivatePanel(0);
        NavigateActivePanelTo(_sourceDir.FullName);
        SelectItemByName("newfile.txt");

        ActivatePanel(1);
        EnterArchiveInActivePanel(archivePath);

        ActivatePanel(0); // back to the source panel
        SelectItemByName("newfile.txt");

        PressF5AndConfirm();

        Retry.WhileFalse(() =>
        {
            using var zip = ZipFile.OpenRead(archivePath);
            return zip.Entries.Any(e => e.Name == "newfile.txt");
        }, TimeSpan.FromSeconds(10));
        AssertAlive("copy real file into zip");

        using var finalZip = ZipFile.OpenRead(archivePath);
        Assert.That(finalZip.Entries.Select(e => e.Name), Is.EquivalentTo(new[] { "existing.txt", "newfile.txt" }));
        using var reader = new StreamReader(finalZip.GetEntry("newfile.txt")!.Open());
        Assert.That(reader.ReadToEnd(), Is.EqualTo("copied into zip via F5"));
    }

    [Test]
    public void CopyingAnEntryOutOfAZipArchive_ViaF5_ExtractsItToTheRealFolder()
    {
        var archivePath = Path.Combine(_archiveDir.FullName, "container.zip");
        WriteZipFixture(archivePath, "existing.txt", "extract me via F5");

        ActivatePanel(0);
        EnterArchiveInActivePanel(archivePath);

        ActivatePanel(1);
        NavigateActivePanelTo(_extractDir.FullName);

        ActivatePanel(0); // back into the archive
        SelectItemByName("existing.txt");

        PressF5AndConfirm();

        var extractedPath = Path.Combine(_extractDir.FullName, "existing.txt");
        Retry.WhileFalse(() => File.Exists(extractedPath), TimeSpan.FromSeconds(10));
        AssertAlive("copy entry out of zip");

        Assert.That(File.ReadAllText(extractedPath), Is.EqualTo("extract me via F5"));
    }

    /// <summary>The RewritingArchiveWriter add-path (TAR has no in-place update), driven through
    /// the real F5 command rather than constructed directly, as <c>RewritingArchiveWriterTests</c>
    /// already does at the operation layer.</summary>
    [Test]
    public void CopyingARealFileIntoATarArchive_ViaF5_AddsItToTheArchive()
    {
        var archivePath = Path.Combine(_archiveDir.FullName, "container.tar");
        WriteTarFixture(archivePath, "existing.txt", "already inside tar");
        File.WriteAllText(Path.Combine(_sourceDir.FullName, "newfile.txt"), "copied into tar via F5");

        ActivatePanel(0);
        NavigateActivePanelTo(_sourceDir.FullName);
        SelectItemByName("newfile.txt");

        ActivatePanel(1);
        EnterArchiveInActivePanel(archivePath);

        ActivatePanel(0);
        SelectItemByName("newfile.txt");

        PressF5AndConfirm();

        Retry.WhileFalse(() =>
        {
            using var fileStream = File.OpenRead(archivePath);
            using var tarReader = new TarReader(fileStream);
            TarEntry? e;
            while ((e = tarReader.GetNextEntry()) != null)
                if (e.Name == "newfile.txt") return true;
            return false;
        }, TimeSpan.FromSeconds(10));
        AssertAlive("copy real file into tar");

        using var finalStream = File.OpenRead(archivePath);
        using var finalReader = new TarReader(finalStream);
        var names = new List<string>();
        TarEntry? entry;
        while ((entry = finalReader.GetNextEntry()) != null)
            names.Add(entry.Name);
        Assert.That(names, Is.EquivalentTo(new[] { "existing.txt", "newfile.txt" }));
    }
}
