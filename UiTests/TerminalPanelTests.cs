using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace CoderCommander.UiTests;

/// <summary>
/// FlaUI coverage for the embedded terminal panel: F9 visibility toggle, Ctrl+T tab creation
/// (through the real <c>SelectShellDialog</c>), Ctrl+W closing it, and an actual command round
/// trip through a real cmd.exe/PowerShell child process.
/// <para>
/// These tests drive the real app against the real settings.json - <c>TerminalVisible</c> and
/// <c>OpenTerminalTabs</c> persist across launches and get written on close, so every test
/// restores the panel to whatever visibility it found at <see cref="Launch"/> and closes any tab
/// it created before <see cref="Cleanup"/> lets the app exit. Otherwise running this suite would
/// silently change what terminal state the next real launch resumes into.
/// </para>
/// <para>
/// The panel's shared output/input controls (<c>_sharedContent</c>, holding the RichTextBox and
/// TextBox) only get attached to the visual tree once a <c>ThemedTabPage</c> is created for an
/// actual tab (see <c>EmbeddedTerminalPanel.OnTabCreated</c>) - before that, F9 alone only exposes
/// the empty tab strip and its "+" button, confirmed via a throwaway diagnostic dump. So tests that
/// need the input/output boxes create a tab first.
/// </para>
/// </summary>
public class TerminalPanelTests : UiTestBase
{
    private bool _terminalWasVisibleAtStart;

    [SetUp]
    public override void Launch()
    {
        base.Launch();
        _terminalWasVisibleAtStart = TerminalPanelVisible();
    }

    [TearDown]
    public override void Cleanup()
    {
        try
        {
            CloseAnyOpenTab();
            if (TerminalPanelVisible() != _terminalWasVisibleAtStart)
            {
                MainWindow!.Focus();
                Keyboard.Press(VirtualKeyShort.F9);
                Thread.Sleep(200);
            }
        }
        catch { /* best effort - app process is killed regardless below */ }
        base.Cleanup();
    }

    /// <summary>The "+" new-tab button is unique across the whole app and only present while the
    /// terminal panel is visible - simpler and more reliable than reading RichTextBox/TextBox
    /// content, which only exist once a tab has been created (see class remarks).</summary>
    private bool TerminalPanelVisible() =>
        MainWindow!.FindFirstDescendant(cf => cf.ByName("+")) != null;

    private void EnsureTerminalPanelVisible()
    {
        if (TerminalPanelVisible()) return;
        MainWindow!.Focus();
        Keyboard.Press(VirtualKeyShort.F9);
        Retry.WhileFalse(TerminalPanelVisible, TimeSpan.FromSeconds(3));
    }

    /// <summary>Creates a tab via Ctrl+T, accepting the default shell selection in the dialog that
    /// opens (<see cref="ThemedForm.AcceptButton"/> is wired to it, so clicking OK/pressing Enter
    /// both work - this uses the OK button directly to avoid depending on dialog focus).</summary>
    private void CreateTabWithDefaultShell()
    {
        var dlg = PressUntilModalAppears(() =>
        {
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                Keyboard.Press(VirtualKeyShort.KEY_T);
        });

        var okButton = dlg.FindFirstDescendant(cf => cf.ByName("OK").Or(cf.ByName("ОК")))?.AsButton();
        Assert.That(okButton, Is.Not.Null, "OK button not found in the shell-selection dialog");
        okButton!.Invoke();

        Retry.WhileTrue(() => MainWindow!.ModalWindows.Length > 0, TimeSpan.FromSeconds(5));
    }

    private void CloseAnyOpenTab()
    {
        if (!TerminalPanelVisible()) return;
        MainWindow!.Focus();
        using (Keyboard.Pressing(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL))
            Keyboard.Press(VirtualKeyShort.KEY_W);
        Thread.Sleep(200);
    }

    private AutomationElement? FindTerminalInputBox() =>
        MainWindow!.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit)).FirstOrDefault();

    private AutomationElement? FindTerminalOutputBox() =>
        MainWindow!.FindAllDescendants(cf => cf.ByControlType(ControlType.Document)).FirstOrDefault();

    private static string ReadDocumentText(AutomationElement doc) =>
        doc.Patterns.Text.Pattern.DocumentRange.GetText(-1);

    [Test]
    public void F9_TogglesThePanelVisibility()
    {
        var before = TerminalPanelVisible();

        MainWindow!.Focus();
        Keyboard.Press(VirtualKeyShort.F9);
        Retry.WhileTrue(() => TerminalPanelVisible() == before, TimeSpan.FromSeconds(3));
        Assert.That(TerminalPanelVisible(), Is.Not.EqualTo(before), "F9 should flip terminal panel visibility");

        Keyboard.Press(VirtualKeyShort.F9);
        Retry.WhileTrue(() => TerminalPanelVisible() != before, TimeSpan.FromSeconds(3));
        Assert.That(TerminalPanelVisible(), Is.EqualTo(before), "F9 again should flip it back");
    }

    [Test]
    public void CtrlT_OpensShellDialog_AndCreatesATabWithOutputAndInputControls()
    {
        CreateTabWithDefaultShell();

        Assert.That(TerminalPanelVisible(), Is.True, "Creating a tab should leave the terminal panel visible");

        AutomationElement? input = null;
        Retry.WhileNull(() => input = FindTerminalInputBox(), TimeSpan.FromSeconds(5));
        Assert.That(input, Is.Not.Null, "Terminal input box should exist once a tab is created");

        var output = FindTerminalOutputBox();
        Assert.That(output, Is.Not.Null, "Terminal output box should exist once a tab is created");

        AssertAlive("create terminal tab");
    }

    /// <summary>
    /// The output/input controls are shared across every tab by design (see class remarks) rather
    /// than torn down per tab, so closing the only open tab doesn't make them disappear - what
    /// actually proves Ctrl+W worked (rather than silently no-op'd or corrupted session-manager
    /// state) is that a brand new tab can still be created immediately afterward.
    /// </summary>
    [Test]
    public void CtrlW_ClosesTheActiveTab_AndANewOneCanStillBeCreatedAfterward()
    {
        CreateTabWithDefaultShell();
        AutomationElement? input = null;
        Retry.WhileNull(() => input = FindTerminalInputBox(), TimeSpan.FromSeconds(5));
        Assert.That(input, Is.Not.Null, "Tab should have been created first");

        MainWindow!.Focus();
        using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
            Keyboard.Press(VirtualKeyShort.KEY_W);
        Thread.Sleep(500);
        AssertAlive("close terminal tab");

        CreateTabWithDefaultShell();
        AutomationElement? inputAfter = null;
        Retry.WhileNull(() => inputAfter = FindTerminalInputBox(), TimeSpan.FromSeconds(5));
        Assert.That(inputAfter, Is.Not.Null, "Should be able to create a new tab right after closing the previous one");
    }

    /// <summary>The actual "does the command line work" check: types a real command into a real
    /// cmd.exe/PowerShell child process and confirms its output round-trips into the panel.</summary>
    [Test]
    public void TypingACommand_ActuallyRunsItAndShowsTheOutput()
    {
        CreateTabWithDefaultShell();

        AutomationElement? input = null;
        Retry.WhileNull(() => input = FindTerminalInputBox(), TimeSpan.FromSeconds(5));
        Assert.That(input, Is.Not.Null);

        var output = FindTerminalOutputBox();
        Assert.That(output, Is.Not.Null);

        const string marker = "cc-flaui-terminal-marker-12345";
        input!.Click();
        input.AsTextBox().Text = $"echo {marker}";
        Keyboard.Press(VirtualKeyShort.ENTER);

        Retry.WhileFalse(() => ReadDocumentText(output!).Contains(marker, StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(10));
        Assert.That(ReadDocumentText(output!), Does.Contain(marker),
            "Echoed marker should show up in the terminal output once the shell actually runs the command");
    }
}
