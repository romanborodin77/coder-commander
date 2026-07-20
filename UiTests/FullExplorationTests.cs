using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;

namespace CoderCommander.UiTests;

/// <summary>
/// Broad, ordered walkthrough of most of the app's command surface - not a substitute for the
/// focused regression tests elsewhere, but a "does anything blow up when you actually use it"
/// pass. Runs in a disposable sandbox directory under Temp; never touches real user files.
/// Each [Test] is a checkpoint: a failure here identifies which feature broke without needing to
/// guess from a single giant method.
///
/// The app is relaunched fresh per test (via UiTestBase's [SetUp]/[TearDown]) rather than shared
/// across the whole ordered run - a long-lived single session was observed to accumulate COM/UIA
/// client-side degradation (spurious "window died" failures and timeouts) after enough rapid
/// automation calls. The sandbox directory itself, however, is created once and persists across
/// tests in this fixture, since several tests depend on filesystem state left behind by earlier
/// ones in the [Order] sequence (e.g. Rename depends on MakeDir's folder still being there).
/// </summary>
[Order(100)] // run after the more targeted, faster suites
public class FullExplorationTests : UiTestBase
{
    private DirectoryInfo _sandbox = null!;

    [OneTimeSetUp]
    public void CreateSandbox()
    {
        _sandbox = Directory.CreateTempSubdirectory("cc_full_exploration_");
        Directory.CreateDirectory(Path.Combine(_sandbox.FullName, "subdir"));
        File.WriteAllText(Path.Combine(_sandbox.FullName, "alpha.txt"), "alpha content");
        File.WriteAllText(Path.Combine(_sandbox.FullName, "beta.txt"), "beta content, a bit longer than alpha");
        File.WriteAllText(Path.Combine(_sandbox.FullName, "subdir", "nested.txt"), "nested content");
    }

    [OneTimeTearDown]
    public void DeleteSandbox()
    {
        try { Directory.Delete(_sandbox.FullName, recursive: true); } catch { /* best-effort */ }
    }

    [SetUp]
    public override void Launch()
    {
        base.Launch();
        NavigateActivePanelTo(_sandbox.FullName);
    }

    /// <summary>
    /// Flat View shows nested items under their relative path (subdir\nested.txt), not their bare
    /// file name - ByName is an exact match, so this has to search all list items for one whose
    /// name ends with "nested.txt" rather than look up "nested.txt" directly.
    /// </summary>
    private bool NestedTxtVisible() =>
        MainWindow!.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .Any(e => e.Name != null && e.Name.EndsWith("nested.txt", StringComparison.OrdinalIgnoreCase));

    // ── Navigation, sorting, selection ─────────────────────────────────

    [Test, Order(1)]
    public void SortByEachColumn_DoesNotCrash()
    {
        foreach (var col in new[] { "Name", "Extension", "Size", "Date modified" })
        {
            ClickMenuPath("View", "Sort by", col);
            AssertAlive($"sort by {col}");
        }
        ClickMenuPath("View", "Sort by", "Directories first");
        AssertAlive("toggle DirectoriesFirst");
        ClickMenuPath("View", "Sort by", "Descending");
        AssertAlive("toggle Descending");
        // leave sorting back in a sane state for later steps (persisted to settings across relaunch)
        ClickMenuPath("View", "Sort by", "Directories first");
        ClickMenuPath("View", "Sort by", "Descending");
        ClickMenuPath("View", "Sort by", "Name");
    }

    [Test, Order(2)]
    public void ToggleHidden_DoesNotCrash()
    {
        ClickMenuPath("View", "Show Hidden");
        AssertAlive("show hidden on");
        ClickMenuPath("View", "Show Hidden");
        AssertAlive("show hidden off");
    }

    /// <summary>Regression coverage for a real gap: <c>CommandIds.ToggleShowExtensionInName</c> was
    /// registered but never wired to any menu item, so this toggle was only reachable via the
    /// Settings checkbox - now also a View menu item, verified here by checking that "alpha.txt"'s
    /// displayed name in the list actually flips between "alpha.txt" and "alpha".</summary>
    [Test, Order(7)]
    public void ToggleShowExtensionInName_TogglesTheNameColumnDisplay()
    {
        bool AlphaShownWithExtension() =>
            MainWindow!.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                .Any(e => string.Equals(e.Name, "alpha.txt", StringComparison.OrdinalIgnoreCase));

        var before = AlphaShownWithExtension();

        ClickMenuPath("View", "Show extension in name");
        Retry.WhileTrue(() => AlphaShownWithExtension() == before, TimeSpan.FromSeconds(3));
        Assert.That(AlphaShownWithExtension(), Is.Not.EqualTo(before),
            "Toggling should flip whether the extension shows in the Name column");
        AssertAlive("toggle show-extension-in-name");

        // Leave the setting back where it started (persisted to settings across relaunch).
        ClickMenuPath("View", "Show extension in name");
        Retry.WhileFalse(() => AlphaShownWithExtension() == before, TimeSpan.FromSeconds(3));
        AssertAlive("restore show-extension-in-name");
    }

    /// <summary>
    /// Ctrl+P has no dialog to confirm it landed, unlike most other raw-key actions here - drive it
    /// off the observable side effect instead (subdir/nested.txt only shows up in the flat/recursive
    /// listing), checking first so it's idempotent, and retrying the press since a single key press
    /// occasionally doesn't land even with the window freshly focused.
    /// </summary>
    private void SetFlatView(bool desired) =>
        Retry.WhileFalse(() =>
        {
            if (NestedTxtVisible() == desired) return true;
            MainWindow!.Focus();
            using (Keyboard.Pressing(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL))
                Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_P);
            Thread.Sleep(200);
            return NestedTxtVisible() == desired;
        }, TimeSpan.FromSeconds(8));

    [Test, Order(3)]
    public void ToggleFlatView_DoesNotCrash()
    {
        Assert.That(NestedTxtVisible(), Is.False, "Precondition: flat view should start off");
        try
        {
            SetFlatView(true);
            AssertAlive("flat view on");
            Assert.That(NestedTxtVisible(), Is.True, "Flat view should now show subdir/nested.txt");
        }
        finally
        {
            // FlatView is persisted to settings - if left stuck on here (e.g. the assert above
            // throws), every later test in this fixture would relaunch into a recursive listing,
            // which SelectItemByName tolerates but which would still be a confusing state to debug.
            SetFlatView(false);
        }
        AssertAlive("flat view off");
        Assert.That(NestedTxtVisible(), Is.False, "Flat view should be back off - later tests assume a flat listing");
    }

    [Test, Order(4)]
    public void SelectionCommands_DoNotCrash()
    {
        ClickMenuPath("Selection", "Select All");
        AssertAlive("SelectAll");
        ClickMenuPath("Selection", "Deselect All");
        AssertAlive("DeselectAll");
        ClickMenuPath("Selection", "Invert Selection");
        AssertAlive("InvertSelection");
        ClickMenuPath("Selection", "Deselect All");
    }

    [Test, Order(5)]
    public void SelectGroupAndDeselectGroup_DoNotCrash()
    {
        // Hotkey-only (Ctrl+NumPad+ / Ctrl+NumPad-) - no menu item exists for either.
        RespondToOpenModal(
            PressUntilModalAppears(() =>
            {
                using (Keyboard.Pressing(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL))
                    Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ADD);
            }),
            "*.txt");
        AssertAlive("SelectGroup");

        RespondToOpenModal(
            PressUntilModalAppears(() =>
            {
                using (Keyboard.Pressing(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL))
                    Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.SUBTRACT);
            }),
            "*.txt");
        AssertAlive("DeselectGroup");
    }

    [Test, Order(6)]
    public void SwapPanelsAndTargetEqualSource_DoNotCrash()
    {
        ClickMenuPath("Commands", "Swap Panels");
        AssertAlive("SwapPanels");
        ClickMenuPath("Commands", "Swap Panels"); // swap back
        ClickMenuPath("Commands", "Target = Source");
        AssertAlive("TargetEqualSource");
    }

    // ── File operations (sandbox only) ─────────────────────────────────

    [Test, Order(10)]
    public void MakeDir_CreatesFolder()
    {
        ClickMenuPath("File", "Make Dir");
        RespondToInputDialog("newfolder1");
        AssertAlive("MakeDir");
        Retry.WhileFalse(() => Directory.Exists(Path.Combine(_sandbox.FullName, "newfolder1")), TimeSpan.FromSeconds(3));
        Assert.That(Directory.Exists(Path.Combine(_sandbox.FullName, "newfolder1")), Is.True);
    }

    [Test, Order(11)]
    public void Rename_RenamesFile()
    {
        SelectItemByName("alpha.txt");
        ClickMenuPath("File", "Rename");
        RespondToInputDialog("alpha_renamed.txt");
        AssertAlive("Rename");
        Retry.WhileFalse(() => File.Exists(Path.Combine(_sandbox.FullName, "alpha_renamed.txt")), TimeSpan.FromSeconds(3));
        Assert.That(File.Exists(Path.Combine(_sandbox.FullName, "alpha_renamed.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(_sandbox.FullName, "alpha.txt")), Is.False);
    }

    [Test, Order(12)]
    public void MultiRenameDialog_OpensAndCancels()
    {
        // MultiRename() no-ops on an empty selection, and the cursor sits on ".." right after a
        // fresh navigation - a real file must be selected first or no dialog opens at all.
        SelectItemByName("beta.txt");
        ClickMenuPath("Commands", "Multi-Rename…");
        var dlg = WaitForModal(TimeSpan.FromSeconds(5));
        AssertAlive("MultiRename opened");

        var cancelBtn = dlg.FindFirstDescendant(cf => cf.ByName("Cancel").Or(cf.ByName("Отмена")))?.AsButton();
        if (cancelBtn != null) cancelBtn.Invoke();
        else Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);

        Retry.WhileTrue(() => MainWindow!.ModalWindows.Length > 0, TimeSpan.FromSeconds(5));
        AssertAlive("MultiRename closed");
    }

    [Test, Order(13)]
    public void Copy_CopiesFileToOtherPanel()
    {
        var destDir = Directory.CreateTempSubdirectory("cc_full_exploration_dest_");
        try
        {
            MainWindow!.Focus();
            Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.TAB); // switch to the other (inactive) panel
            NavigateActivePanelTo(destDir.FullName);
            MainWindow!.Focus();
            Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.TAB); // switch back to the sandbox panel

            SelectItemByName("beta.txt");
            var confirmDlg = PressUntilModalAppears(() => Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.F5));
            var okBtn = confirmDlg.FindFirstDescendant(cf => cf.ByName("OK").Or(cf.ByName("ОК")))?.AsButton();
            Assert.That(okBtn, Is.Not.Null, "Copy confirm OK button not found");
            okBtn!.Invoke();

            Retry.WhileTrue(() => File.Exists(Path.Combine(destDir.FullName, "beta.txt")) == false, TimeSpan.FromSeconds(5));
            AssertAlive("Copy");
            Assert.That(File.Exists(Path.Combine(destDir.FullName, "beta.txt")), Is.True, "beta.txt should have been copied");
            Assert.That(File.Exists(Path.Combine(_sandbox.FullName, "beta.txt")), Is.True, "Copy must not remove the source");
        }
        finally
        {
            try { Directory.Delete(destDir.FullName, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Test, Order(14)]
    public void Move_MovesFileToOtherPanel()
    {
        var destDir = Directory.CreateTempSubdirectory("cc_full_exploration_dest_");
        try
        {
            MainWindow!.Focus();
            Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.TAB);
            NavigateActivePanelTo(destDir.FullName);
            MainWindow!.Focus();
            Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.TAB);
            NavigateActivePanelTo(_sandbox.FullName); // Copy_ test above navigated the other panel away from sandbox's own listing state

            SelectItemByName("beta.txt");
            var confirmDlg = PressUntilModalAppears(() => Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.F6));
            var okBtn = confirmDlg.FindFirstDescendant(cf => cf.ByName("OK").Or(cf.ByName("ОК")))?.AsButton();
            Assert.That(okBtn, Is.Not.Null, "Move confirm OK button not found");
            okBtn!.Invoke();

            Retry.WhileTrue(() => File.Exists(Path.Combine(destDir.FullName, "beta.txt")) == false, TimeSpan.FromSeconds(5));
            AssertAlive("Move");
            Assert.That(File.Exists(Path.Combine(destDir.FullName, "beta.txt")), Is.True, "beta.txt should have been moved");
            Assert.That(File.Exists(Path.Combine(_sandbox.FullName, "beta.txt")), Is.False, "Move must remove the source");
        }
        finally
        {
            try { Directory.Delete(destDir.FullName, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Test, Order(15)]
    public void Delete_SendsFileToRecycleBin()
    {
        NavigateActivePanelTo(_sandbox.FullName);
        SelectItemByName("newfolder1"); // empty dir created earlier - safe to delete
        var confirmDlg = PressUntilModalAppears(() => Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.F8));
        var yesBtn = confirmDlg.FindFirstDescendant(cf => cf.ByName("Yes").Or(cf.ByName("Да")))?.AsButton();
        Assert.That(yesBtn, Is.Not.Null, "Delete confirm Yes button not found");
        yesBtn!.Invoke();

        Retry.WhileTrue(() => Directory.Exists(Path.Combine(_sandbox.FullName, "newfolder1")), TimeSpan.FromSeconds(5));
        AssertAlive("Delete");
        Assert.That(Directory.Exists(Path.Combine(_sandbox.FullName, "newfolder1")), Is.False);
    }
}
