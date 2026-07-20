using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace CoderCommander.UiTests;

/// <summary>Shared launch/teardown and menu-navigation helpers for FlaUI tests driving the real app.</summary>
public abstract class UiTestBase
{
    protected static readonly string ExePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "bin", "Debug", "net8.0-windows", "CoderCommander.exe"));

    protected Application? App;
    protected UIA3Automation? Automation;
    protected Window? MainWindow;

    [SetUp]
    public virtual void Launch()
    {
        // Defensive: a previous test's Cleanup() doing a best-effort Kill() doesn't guarantee the
        // OS finished tearing that process down before this SetUp runs - starting a second
        // instance while one lingers makes them fight over window focus, which silently breaks
        // menu clicks (and everything downstream) in the new one.
        foreach (var stray in System.Diagnostics.Process.GetProcessesByName("CoderCommander"))
        {
            try { stray.Kill(); stray.WaitForExit(3000); } catch { /* best-effort */ }
        }

        Assert.That(File.Exists(ExePath), Is.True, $"Executable not found: {ExePath}. Run 'dotnet build' in the repo root first.");
        App = Application.Launch(ExePath);
        Automation = new UIA3Automation();
        MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(15));
        Assert.That(MainWindow, Is.Not.Null, "Main window did not appear");

        // GetMainWindow() returning doesn't guarantee the window is focused/interactive yet -
        // clicking a top-level menu item too early can silently fail to open its dropdown.
        MainWindow!.Focus();
        Thread.Sleep(300);
    }

    [TearDown]
    public virtual void Cleanup()
    {
        try
        {
            if (App != null && !App.HasExited)
            {
                App.Close();
                // Close() (WM_CLOSE) can be swallowed by a lingering modal dialog or a slow
                // shutdown path; without waiting here, the next test's Launch() can start a second
                // instance while this one is still alive, and the two fight over window focus -
                // menu clicks in the new instance then silently do nothing.
                var exited = Retry.WhileFalse(() => App.HasExited, TimeSpan.FromSeconds(5)).Success;
                if (!exited) App.Kill();
            }
        }
        catch { /* best-effort */ }
        finally
        {
            Automation?.Dispose();
        }
    }

    /// <summary>
    /// Clicks down a chain of menu items. Each level accepts '|'-separated alternatives so a test
    /// doesn't care which UI language is currently active (e.g. "Help|Справка").
    /// </summary>
    protected void ClickMenuPath(params string[] namesPerLevel)
    {
        var menuBar = MainWindow!.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuBar))?.AsMenu();
        Assert.That(menuBar, Is.Not.Null, "Main menu bar not found");

        AutomationElement current = menuBar!;
        foreach (var namesAtThisLevel in namesPerLevel)
        {
            var options = namesAtThisLevel.Split('|');
            AutomationElement? found = null;
            // Clicking a menu item to open its dropdown is not synchronous with the dropdown's
            // children appearing in the UIA tree - retry rather than trusting a single lookup.
            Retry.WhileNull(() =>
            {
                foreach (var name in options)
                {
                    found = current.FindFirstDescendant(cf => cf.ByName(name));
                    if (found != null) return found;
                }
                return null;
            }, TimeSpan.FromSeconds(3));
            Assert.That(found, Is.Not.Null, $"Menu item not found, tried: {namesAtThisLevel}");
            found!.AsMenuItem().Click();
            current = found;
        }
    }

    /// <summary>Waits for the main window's first modal child (a ShowDialog()-opened form).</summary>
    protected Window WaitForModal(TimeSpan timeout)
    {
        Window? dlg = null;
        Retry.WhileNull(() => dlg = MainWindow!.ModalWindows.FirstOrDefault(), timeout);
        Assert.That(dlg, Is.Not.Null, "Expected modal dialog did not appear");
        return dlg!;
    }

    protected void AssertAlive([System.Runtime.CompilerServices.CallerMemberName] string step = "") =>
        Assert.That(MainWindow!.IsAvailable, Is.True, $"Main window died after: {step}");

    /// <summary>Fills the single textbox of an already-open InputDialogForm-style modal and clicks OK.</summary>
    protected void RespondToOpenModal(Window dlg, string text)
    {
        var textBox = dlg.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit))?.AsTextBox();
        Assert.That(textBox, Is.Not.Null, "Input dialog textbox not found");
        textBox!.Text = text;
        var okButton = dlg.FindFirstDescendant(cf => cf.ByName("OK").Or(cf.ByName("ОК")))?.AsButton();
        Assert.That(okButton, Is.Not.Null, "OK button not found");
        okButton!.Invoke();
        Retry.WhileTrue(() => MainWindow!.ModalWindows.Length > 0, TimeSpan.FromSeconds(5));
    }

    /// <summary>Waits for an InputDialogForm-style modal to appear, fills its textbox, and clicks OK.</summary>
    protected void RespondToInputDialog(string text, TimeSpan? timeout = null) =>
        RespondToOpenModal(WaitForModal(timeout ?? TimeSpan.FromSeconds(5)), text);

    /// <summary>
    /// Runs <paramref name="pressAction"/> (typically one or more raw Keyboard.Press calls) and
    /// waits for a modal to appear, retrying the whole action a few times if it doesn't - a single
    /// raw key press occasionally goes nowhere even with the window freshly focused (observed
    /// empirically with Ctrl+G and F5/F6/F8 alike), and a spurious repeat is harmless here since
    /// it either re-opens the same not-yet-open dialog or is a no-op once it's already open.
    /// </summary>
    protected Window PressUntilModalAppears(Action pressAction, int maxAttempts = 5)
    {
        Window? dlg = null;
        for (var attempt = 0; attempt < maxAttempts && dlg == null; attempt++)
        {
            MainWindow!.Focus();
            pressAction();
            Retry.WhileNull(() => dlg = MainWindow!.ModalWindows.FirstOrDefault(), TimeSpan.FromSeconds(1));
        }
        Assert.That(dlg, Is.Not.Null, "Expected modal dialog did not appear after retrying");
        return dlg!;
    }

    /// <summary>Navigates the active panel via Ctrl+G ("Change Directory") - works regardless of
    /// whatever path either panel currently happens to have restored from settings.</summary>
    protected void NavigateActivePanelTo(string path)
    {
        var dlg = PressUntilModalAppears(() =>
        {
            using (Keyboard.Pressing(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL))
                Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_G);
        });
        RespondToOpenModal(dlg, path);
    }

    /// <summary>
    /// Selects a file-list row by its file name rather than by numeric cursor position - sort order
    /// (DirectoriesFirst, sort column/direction) is persisted to settings and read back on every
    /// relaunch, so a hardcoded row index silently picks the wrong item whenever an earlier test in
    /// the run left that persisted state different from what the index assumed (observed in
    /// practice, not just theoretical). Matches either the full name or the extension-less stem,
    /// since the Name column drops the extension when ShowExtensionInName is off.
    /// </summary>
    protected void SelectItemByName(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        AutomationElement? item = null;
        Retry.WhileNull(() => item = MainWindow!.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(e.Name, stem, StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(3));
        Assert.That(item, Is.Not.Null, $"List item not found: {name}");
        item!.Click();
    }

    /// <summary>Waits for any top-level window of this process matching <paramref name="predicate"/> -
    /// needed for non-modal dialogs (Form.Show(), e.g. SearchDialogForm) that don't show up in
    /// ModalWindows.</summary>
    protected Window WaitForTopLevelWindow(Func<Window, bool> predicate, TimeSpan timeout)
    {
        Window? found = null;
        Retry.WhileNull(() => found = App!.GetAllTopLevelWindows(Automation!).FirstOrDefault(predicate), timeout);
        Assert.That(found, Is.Not.Null, "Expected top-level window did not appear");
        return found!;
    }

    protected bool AnyTopLevelWindow(Func<Window, bool> predicate) =>
        App!.GetAllTopLevelWindows(Automation!).Any(predicate);
}
