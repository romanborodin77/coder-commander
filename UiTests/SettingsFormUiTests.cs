using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace CoderCommander.UiTests;

/// <summary>
/// End-to-end (real app, FlaUI) coverage for the Settings dialog's per-format archive compression
/// picker (File Operations tab).
/// <para>
/// Regression test for a real bug: the format list there used to be built from every registered
/// format (a since-removed <c>ArchiveFormatRegistry.All</c>, unused anywhere else), so read-only
/// formats (7Z, RAR, TAR.BZ2 at the time, TAR.XZ) showed up right alongside the creatable ones -
/// selecting one left the compression combo permanently empty (nothing to configure) with no
/// indication why, and the list didn't match <c>PackDialogForm</c>'s format combo (built from
/// <c>ArchiveFormatRegistry.Creatable</c>), which is the only place a format actually gets used to
/// pack anything. Fixed by scoping Settings' list to <c>Creatable</c> too, same as the pack dialog.
/// </para>
/// </summary>
public class SettingsFormUiTests : UiTestBase
{
    /// <summary>Doesn't use the shared <see cref="UiTestBase.ClickMenuPath"/> helper: the
    /// "Settings…" dropdown item isn't a UIA descendant of the clicked "Configuration" MenuItem
    /// (its popup attaches elsewhere in the tree, confirmed via a throwaway diagnostic dump), so it
    /// has to be searched for across the whole window instead of scoped under "Configuration".</summary>
    private Window OpenSettingsDialog()
    {
        var menuBar = MainWindow!.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuBar))?.AsMenu();
        Assert.That(menuBar, Is.Not.Null, "Main menu bar not found");

        var config = menuBar!.FindFirstDescendant(cf => cf.ByName("Configuration").Or(cf.ByName("Конфигурация")));
        Assert.That(config, Is.Not.Null, "Configuration menu not found");
        config!.AsMenuItem().Click();

        AutomationElement? settings = null;
        Retry.WhileNull(() => settings = MainWindow!.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName("Settings…").Or(cf.ByName("Настройки…")))),
            TimeSpan.FromSeconds(3));
        Assert.That(settings, Is.Not.Null, "Settings… menu item not found");
        settings!.AsMenuItem().Click();

        return WaitForModal(TimeSpan.FromSeconds(5));
    }

    private static void CloseWithCancel(Window dlg)
    {
        var cancelBtn = dlg.FindFirstDescendant(cf => cf.ByName("Cancel").Or(cf.ByName("Отмена")))?.AsButton();
        Assert.That(cancelBtn, Is.Not.Null, "Cancel button not found in Settings dialog");
        cancelBtn!.Invoke();
    }

    [Test]
    public void ArchiveFormatCombo_OnlyOffersCreatableFormats_NotReadOnlyOnes()
    {
        var dlg = OpenSettingsDialog();

        var fileOpsTab = dlg.FindFirstDescendant(cf => cf.ByName("File Operations").Or(cf.ByName("Файловые операции")));
        Assert.That(fileOpsTab, Is.Not.Null, "File Operations tab not found");
        fileOpsTab!.Click();

        var formatCombo = dlg.FindFirstDescendant(cf => cf.ByControlType(ControlType.Pane)
            .And(cf.ByName("Archive format:").Or(cf.ByName("Формат архива:"))));
        Assert.That(formatCombo, Is.Not.Null, "Archive format combo not found on File Operations tab");
        formatCombo!.Click();

        AutomationElement[] items = Array.Empty<AutomationElement>();
        Retry.WhileTrue(() =>
        {
            items = dlg.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)).ToArray();
            return items.Length == 0;
        }, TimeSpan.FromSeconds(3));

        // Golden list of Creatable formats - update alongside PackDialogFormUiTests whenever a
        // format's write support changes (most recently: TAR.BZ2 became writable).
        var names = items.Select(i => i.Name).ToArray();
        Assert.That(names, Is.EquivalentTo(new[] { "ZIP", "TAR", "TAR.GZ", "TAR.BZ2" }),
            "Settings' archive format list must match PackDialogForm's Creatable list exactly - " +
            "read-only formats (7Z/RAR/TAR.XZ) have nothing to configure and must not appear");

        AssertAlive("open settings archive-format combo");
        CloseWithCancel(dlg);
    }
}
