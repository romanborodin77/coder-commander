using System.Formats.Tar;
using System.IO.Compression;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace CoderCommander.UiTests;

/// <summary>
/// End-to-end (real app, FlaUI) coverage for the actual <c>PackDialogForm</c> UI: picking a
/// target format and a compression preset through its two <c>ThemedComboBox</c> controls (not
/// constructing <see cref="CoderCommander.Operations.PackOperation"/> directly, as
/// <c>ArchiveCompressionSettingsTests</c> and <c>AllFormatsCompressionMatrixTests</c> already do),
/// then confirming the resulting file on disk is a genuinely working archive of the chosen format.
/// <para>
/// <c>ThemedComboBox</c> (see <c>WinForms/ThemedComboBox.cs</c>) is a hand-painted
/// <c>UserControl</c>, not a real WinForms <c>ComboBox</c> - it shows up in the UIA tree as a
/// <c>[Pane]</c> whose accessible name happens to equal its field label, and clicking it opens a
/// <c>ContextMenuStrip</c> whose <c>[MenuItem]</c>s appear nested under the same dialog window
/// (confirmed via a throwaway diagnostic dump), not as a separate top-level window - so a plain
/// <c>dlg.FindFirstDescendant</c> lookup for both the combo and its dropdown items works.
/// </para>
/// </summary>
public class PackDialogFormUiTests : UiTestBase
{
    private DirectoryInfo _sandbox = null!;

    [SetUp]
    public override void Launch()
    {
        base.Launch();
        _sandbox = Directory.CreateTempSubdirectory("cc_pack_dialog_ui_");
    }

    [TearDown]
    public override void Cleanup()
    {
        base.Cleanup();
        try { Directory.Delete(_sandbox.FullName, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Clicks a <c>ThemedComboBox</c> (found by the label text it shares its accessible
    /// name with) and picks the dropdown item matching one of the given '|'-separated language
    /// alternatives - same alternatives idiom as <see cref="UiTestBase.ClickMenuPath"/>.</summary>
    private void SelectFromThemedCombo(Window dlg, string comboLabel, string itemNameAlternatives)
    {
        var combo = dlg.FindFirstDescendant(cf => cf.ByControlType(ControlType.Pane).And(cf.ByName(comboLabel)));
        Assert.That(combo, Is.Not.Null, $"\"{comboLabel}\" combo not found in PackDialogForm");
        combo!.Click();

        var options = itemNameAlternatives.Split('|');
        AutomationElement? item = null;
        Retry.WhileNull(() =>
        {
            foreach (var name in options)
            {
                item = dlg.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name)));
                if (item != null) return item;
            }
            return null;
        }, TimeSpan.FromSeconds(3));
        Assert.That(item, Is.Not.Null, $"Dropdown item not found for \"{comboLabel}\", tried: {itemNameAlternatives}");
        item!.AsMenuItem().Click();
        Thread.Sleep(200);
    }

    private Window OpenPackDialogWithFileSelected(string fileName)
    {
        NavigateActivePanelTo(_sandbox.FullName);
        SelectItemByName(fileName);
        return PressUntilModalAppears(() =>
        {
            using (Keyboard.Pressing(VirtualKeyShort.ALT))
                Keyboard.Press(VirtualKeyShort.F5);
        });
    }

    /// <summary>Regression test for a real crash: <c>ThemedComboBox.ShowDropDown()</c> disposed its
    /// menu items while enumerating that same live collection (<c>ClearMenuItems</c>), which only
    /// throws once a dropdown is opened a SECOND time (the first open starts from an empty
    /// collection, so nothing is being disposed yet) - none of the other tests here reopen the same
    /// combo, so this crash slipped through until a manual click sequence hit it. Fixed by
    /// snapshotting the items before disposing them.</summary>
    [Test]
    public void ReopeningTheSameCombo_MultipleTimesInARow_DoesNotCrash()
    {
        File.WriteAllText(Path.Combine(_sandbox.FullName, "hello.txt"), "regression test for combo reopen");
        var dlg = OpenPackDialogWithFileSelected("hello.txt");

        SelectFromThemedCombo(dlg, "Format:", "ZIP");
        SelectFromThemedCombo(dlg, "Format:", "TAR");
        SelectFromThemedCombo(dlg, "Format:", "TAR.GZ");
        SelectFromThemedCombo(dlg, "Format:", "ZIP");

        AssertAlive("reopen the same ThemedComboBox repeatedly");
    }

    [Test]
    public void SelectingZipFormatAndMaximumCompression_ProducesAWorkingZipArchive()
    {
        File.WriteAllText(Path.Combine(_sandbox.FullName, "hello.txt"), "hello from the pack dialog UI test");

        var dlg = OpenPackDialogWithFileSelected("hello.txt");

        SelectFromThemedCombo(dlg, "Format:", "ZIP");
        SelectFromThemedCombo(dlg, "Compression:", "Maximum|Максимальное");

        var archivePath = Path.Combine(_sandbox.FullName, "picked.zip");
        var nameBox = dlg.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit))?.AsTextBox();
        Assert.That(nameBox, Is.Not.Null, "Archive name textbox not found");
        nameBox!.Text = archivePath;

        var okButton = dlg.FindFirstDescendant(cf => cf.ByName("OK").Or(cf.ByName("ОК")))?.AsButton();
        Assert.That(okButton, Is.Not.Null, "OK button not found");
        okButton!.Invoke();

        Retry.WhileFalse(() => File.Exists(archivePath), TimeSpan.FromSeconds(10));
        Assert.That(File.Exists(archivePath), Is.True, "Picking ZIP + Maximum in the dialog should produce picked.zip");
        AssertAlive("pack via dialog (zip/maximum)");

        using var zip = ZipFile.OpenRead(archivePath);
        var entry = zip.Entries.Single();
        Assert.That(entry.Name, Is.EqualTo("hello.txt"));
        using var reader = new StreamReader(entry.Open());
        Assert.That(reader.ReadToEnd(), Is.EqualTo("hello from the pack dialog UI test"));
    }

    /// <summary>Also confirms picking a different format actually changes the compression combo's
    /// available options: TAR only supports Store, so after switching format the combo should no
    /// longer offer Maximum at all.</summary>
    [Test]
    public void SelectingTarFormat_ProducesAWorkingTarArchive_AndCompressionComboNoLongerOffersMaximum()
    {
        File.WriteAllText(Path.Combine(_sandbox.FullName, "hello.txt"), "hello from tar via the pack dialog");

        var dlg = OpenPackDialogWithFileSelected("hello.txt");

        SelectFromThemedCombo(dlg, "Format:", "TAR");

        var compressionCombo = dlg.FindFirstDescendant(cf => cf.ByControlType(ControlType.Pane).And(cf.ByName("Compression:")));
        Assert.That(compressionCombo, Is.Not.Null);
        compressionCombo!.Click();
        var maximumItem = dlg.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName("Maximum|Максимальное".Split('|')[0])));
        Assert.That(maximumItem, Is.Null, "TAR only supports Store, so Maximum must not be offered after switching to TAR");
        Keyboard.Press(VirtualKeyShort.ESC);
        Thread.Sleep(200);

        var archivePath = Path.Combine(_sandbox.FullName, "picked.tar");
        var nameBox = dlg.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit))?.AsTextBox();
        Assert.That(nameBox, Is.Not.Null);
        nameBox!.Text = archivePath;

        var okButton = dlg.FindFirstDescendant(cf => cf.ByName("OK").Or(cf.ByName("ОК")))?.AsButton();
        Assert.That(okButton, Is.Not.Null);
        okButton!.Invoke();

        Retry.WhileFalse(() => File.Exists(archivePath), TimeSpan.FromSeconds(10));
        Assert.That(File.Exists(archivePath), Is.True, "Picking TAR in the dialog should produce picked.tar");
        AssertAlive("pack via dialog (tar)");

        using var fileStream = File.OpenRead(archivePath);
        using var tarReader = new TarReader(fileStream);
        var tarEntry = tarReader.GetNextEntry();
        Assert.That(tarEntry, Is.Not.Null);
        Assert.That(tarEntry!.Name, Is.EqualTo("hello.txt"));
        using var contentReader = new StreamReader(tarEntry.DataStream!);
        Assert.That(contentReader.ReadToEnd(), Is.EqualTo("hello from tar via the pack dialog"));
        Assert.That(tarReader.GetNextEntry(), Is.Null, "Only one file was packed");
    }
}
