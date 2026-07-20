using System.Formats.Tar;
using System.IO.Compression;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using SharpCompress.Writers.SevenZip;

namespace CoderCommander.UiTests;

/// <summary>
/// End-to-end (real app, FlaUI) coverage for the actual user-facing path that opens an archive as
/// a panel: double-click or Enter on the file in <c>FilePanelUserControl</c>. An earlier
/// <c>CommandIds.ArchiveOpen</c>/<c>MainViewModel.OpenArchive()</c>/<c>ArchiveRequested</c> command
/// chain did the same thing but was never wired to any menu item or hotkey - unreachable from the
/// UI and not what a user actually triggers - so it was removed outright rather than kept as an
/// unused second path to the same place.
/// <para>
/// Written to catch (and now guard) a real bug found while adding this coverage:
/// <c>FilePanelUserControl.OnItemDoubleClick</c>/<c>OnFileListKeyDown</c> had a hardcoded
/// <c>item.Extension is ".zip" or ".jar"</c> check gating whether <c>ArchiveEntered</c> fires at
/// all - it was never updated when TAR/TAR.GZ (Phase 2) or 7z/RAR/TAR.BZ2/TAR.XZ (Phase 3) support
/// was added, so only ZIP/JAR files were actually enterable through the real UI regardless of what
/// <c>ArchiveFormatRegistry</c> supported underneath. Fixed to check
/// <c>ArchiveFormatRegistry.FromExtension(...) != null</c> instead.
/// </para>
/// </summary>
public class ArchiveAsPanelEntryTests : UiTestBase
{
    private DirectoryInfo _sandbox = null!;

    [SetUp]
    public override void Launch()
    {
        base.Launch();
        _sandbox = Directory.CreateTempSubdirectory("cc_archive_panel_entry_");
    }

    [TearDown]
    public override void Cleanup()
    {
        base.Cleanup();
        try { Directory.Delete(_sandbox.FullName, recursive: true); } catch { /* best effort */ }
    }

    private static void WriteZipFixture(string path, string entryName, string content)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void WriteTarGzFixture(string path, string entryName, string content)
    {
        using var fileStream = File.Create(path);
        using var gzip = new GZipStream(fileStream, CompressionMode.Compress);
        using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
        {
            DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content))
        };
        writer.WriteEntry(entry);
    }

    /// <summary>SharpCompress's own writer, test-only (see <c>SharpCompressFormatsTests</c>) - the
    /// app itself never writes 7z, only reads it via <c>SharpCompressReader</c>.</summary>
    private static void WriteSevenZipFixture(string path, string entryName, string content)
    {
        using var stream = File.Create(path);
        using var writer = new SevenZipWriter(stream, new SevenZipWriterOptions());
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        writer.Write(entryName, ms, DateTime.UtcNow);
    }

    private bool AnyPathBarContains(string substring) =>
        MainWindow!.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
            .Any(e =>
            {
                try { return e.AsTextBox().Text?.Contains(substring, StringComparison.OrdinalIgnoreCase) == true; }
                catch { return false; }
            });

    private bool ItemVisible(string name) =>
        MainWindow!.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    [Test]
    public void PressingEnterOnZipFile_NavigatesIntoItsContents()
    {
        var archivePath = Path.Combine(_sandbox.FullName, "archive.zip");
        WriteZipFixture(archivePath, "hello-zip.txt", "hi from zip");

        NavigateActivePanelTo(_sandbox.FullName);
        SelectItemByName("archive.zip");
        Keyboard.Press(VirtualKeyShort.ENTER);

        Retry.WhileFalse(() => AnyPathBarContains(archivePath + "|"), TimeSpan.FromSeconds(5));
        Retry.WhileFalse(() => ItemVisible("hello-zip.txt"), TimeSpan.FromSeconds(5));
        AssertAlive("enter zip via Enter key");
    }

    /// <summary>The exact case the bugfix targets: before it, this extension never even reached
    /// <c>ArchiveEntered</c>, so double-clicking a .tar.gz file did nothing archive-related at all.</summary>
    [Test]
    public void DoubleClickingTarGzFile_NavigatesIntoItsContents()
    {
        var archivePath = Path.Combine(_sandbox.FullName, "archive.tar.gz");
        WriteTarGzFixture(archivePath, "hello-targz.txt", "hi from tar.gz");

        NavigateActivePanelTo(_sandbox.FullName);

        AutomationElement? item = null;
        Retry.WhileNull(() => item = MainWindow!.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .FirstOrDefault(e => string.Equals(e.Name, "archive.tar.gz", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(e.Name, "archive.tar", StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(3));
        Assert.That(item, Is.Not.Null, "archive.tar.gz not found in the file list");
        item!.DoubleClick();

        Retry.WhileFalse(() => AnyPathBarContains(archivePath + "|"), TimeSpan.FromSeconds(5));
        Retry.WhileFalse(() => ItemVisible("hello-targz.txt"), TimeSpan.FromSeconds(5));
        AssertAlive("enter tar.gz via double-click");
    }

    /// <summary>The read-only-format case: 7z has no writer in the app at all (SharpCompress can
    /// only read it), so entering one as a panel must still work purely through
    /// <c>ArchiveFileSystem</c>/<c>SharpCompressReader</c> with no write capability involved.</summary>
    [Test]
    public void PressingEnterOnSevenZipFile_NavigatesIntoItsContents()
    {
        var archivePath = Path.Combine(_sandbox.FullName, "archive.7z");
        WriteSevenZipFixture(archivePath, "hello-7z.txt", "hi from 7z");

        NavigateActivePanelTo(_sandbox.FullName);
        SelectItemByName("archive.7z");
        Keyboard.Press(VirtualKeyShort.ENTER);

        Retry.WhileFalse(() => AnyPathBarContains(archivePath + "|"), TimeSpan.FromSeconds(5));
        Retry.WhileFalse(() => ItemVisible("hello-7z.txt"), TimeSpan.FromSeconds(5));
        AssertAlive("enter 7z via Enter key");
    }

    [Test]
    public void ExitingArchive_ReturnsToTheRealFolder()
    {
        var archivePath = Path.Combine(_sandbox.FullName, "archive.zip");
        WriteZipFixture(archivePath, "inner.txt", "hi");

        NavigateActivePanelTo(_sandbox.FullName);
        SelectItemByName("archive.zip");
        Keyboard.Press(VirtualKeyShort.ENTER);
        Retry.WhileFalse(() => AnyPathBarContains(archivePath + "|"), TimeSpan.FromSeconds(5));

        Keyboard.Press(VirtualKeyShort.BACK);
        Retry.WhileFalse(() => AnyPathBarContains(_sandbox.FullName) && !AnyPathBarContains(archivePath + "|"),
            TimeSpan.FromSeconds(5));
        Retry.WhileFalse(() => ItemVisible("archive.zip"), TimeSpan.FromSeconds(5));
        AssertAlive("exit archive back to the real folder");
    }
}
