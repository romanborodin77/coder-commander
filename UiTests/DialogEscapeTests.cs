using FlaUI.Core.Input;
using FlaUI.Core.Tools;

namespace CoderCommander.UiTests;

/// <summary>Confirms Escape closes dialogs that previously had no CancelButton wired up.</summary>
public class DialogEscapeTests : UiTestBase
{
    [Test]
    public void Escape_ClosesBookmarksDialog()
    {
        ClickMenuPath("Configuration|Конфигурация", "Bookmarks…|Закладки…");
        WaitForModal(TimeSpan.FromSeconds(5));

        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);

        var closed = Retry.WhileFalse(() => MainWindow!.ModalWindows.Length == 0, TimeSpan.FromSeconds(5)).Success;
        Assert.That(closed, Is.True, "Bookmarks dialog should have closed on Escape");
        Assert.That(MainWindow!.IsAvailable, Is.True);
    }

}
