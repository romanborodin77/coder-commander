using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace CoderCommander.UiTests;

/// <summary>
/// Exercises AboutForm through the real UI: Escape must close it (CancelButton fix), and clicking
/// its external links must not crash the app (UseShellExecute fix).
/// </summary>
public class AboutDialogTests : UiTestBase
{
    private Window OpenAboutDialog()
    {
        // Menu text is "&Help"/"&Справка" and "&About"/"&О программе" depending on language.
        ClickMenuPath("Help|Справка", "About|О программе");
        return WaitForModal(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void Escape_ClosesAboutDialog()
    {
        OpenAboutDialog();

        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);

        var closed = FlaUI.Core.Tools.Retry.WhileFalse(() => MainWindow!.ModalWindows.Length == 0, TimeSpan.FromSeconds(5)).Success;
        Assert.That(closed, Is.True, "About dialog should have closed on Escape");
        Assert.That(MainWindow!.IsAvailable, Is.True, "Main window should still be alive");
    }

    [Test]
    public void ClickingLicenseLink_DoesNotCrash()
    {
        var aboutWindow = OpenAboutDialog();

        var licenseLink = aboutWindow.FindFirstDescendant(cf => cf.ByName("MIT License"));
        Assert.That(licenseLink, Is.Not.Null, "MIT License link not found");

        // Click it - previously this threw an unhandled Win32Exception (missing UseShellExecute)
        // that would have popped the app's own "Fatal Error" dialog or the OS JIT-debug prompt.
        // (Side effect: this opens a real browser tab to opensource.org.)
        licenseLink!.Click();

        Thread.Sleep(1000);
        Assert.That(aboutWindow.IsAvailable, Is.True, "About dialog should still be open and responsive");
        Assert.That(MainWindow!.IsAvailable, Is.True, "Main window should still be alive");

        aboutWindow.Close();
    }
}
