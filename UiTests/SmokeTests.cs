using FlaUI.Core.Capturing;

namespace CoderCommander.UiTests;

public class SmokeTests : UiTestBase
{
    [Test]
    public void CanLaunchAndFindMainWindow()
    {
        Console.WriteLine($"Window title: {MainWindow!.Title}");

        var screenshotPath = Path.Combine(Path.GetTempPath(), "flaui_smoke.png");
        Capture.Element(MainWindow).ToFile(screenshotPath);
        Console.WriteLine($"Screenshot saved: {screenshotPath}");
    }
}
