using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace CoderCommander.UiTests;

/// <summary>
/// ChecksumForm and SyncDirsForm both run their work on a background Task while modal, and used to
/// touch disposed controls (including inside their own catch blocks) if the user closed the dialog
/// before that background work finished. These drive a real, slow-enough background op and close
/// the dialog mid-flight to confirm the app survives.
/// </summary>
public class BackgroundOperationCloseTests : UiTestBase
{
    private DirectoryInfo _tempDir = null!;

    [SetUp]
    public override void Launch()
    {
        base.Launch();
        _tempDir = Directory.CreateTempSubdirectory("cc_bgop_test_");
    }

    [TearDown]
    public override void Cleanup()
    {
        base.Cleanup();
        try { Directory.Delete(_tempDir.FullName, recursive: true); } catch { /* best-effort */ }
    }

    [Test]
    public void ClosingChecksumDialogDuringCalculation_DoesNotCrash()
    {
        // Large enough that SHA-256 over it takes a visible amount of time regardless of machine
        // speed, giving a reliable window to close the dialog mid-calculation.
        var bigFile = Path.Combine(_tempDir.FullName, "big.bin");
        var rng = new Random(42);
        var buffer = new byte[1024 * 1024];
        using (var fs = File.Create(bigFile))
        {
            for (var i = 0; i < 150; i++)
            {
                rng.NextBytes(buffer);
                fs.Write(buffer);
            }
        }

        NavigateActivePanelTo(_tempDir.FullName);
        MainWindow!.Focus();
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.DOWN); // move the cursor off ".." onto big.bin

        ClickMenuPath("Commands|Команды", "Checksum…|Рассчитать контрольную сумму…");
        var checksumDlg = WaitForModal(TimeSpan.FromSeconds(5));

        Thread.Sleep(50); // let the calculation actually start
        var closeBtn = checksumDlg.FindFirstDescendant(cf => cf.ByName("Close").Or(cf.ByName("Закрыть")))?.AsButton();
        Assert.That(closeBtn, Is.Not.Null, "Close button not found");
        closeBtn!.Invoke();

        Thread.Sleep(1500); // let any lingering background continuation try to touch the (disposed) form
        Assert.That(MainWindow!.IsAvailable, Is.True, "Main window should still be alive");
        Assert.That(MainWindow!.ModalWindows.Length, Is.EqualTo(0), "Checksum dialog should be closed");
    }

    [Test]
    public void ClosingSyncDirsDialogDuringScan_DoesNotCrash()
    {
        // Enough files that the recursive directory walk (done twice - once per side) takes a
        // visible amount of time. Pointing both sides at the same tree keeps setup simple; the
        // diff result itself doesn't matter for this test, only that the scan is still running
        // when we close the dialog.
        for (var i = 0; i < 3000; i++)
            File.WriteAllText(Path.Combine(_tempDir.FullName, $"f{i}.txt"), "x");

        NavigateActivePanelTo(_tempDir.FullName);

        ClickMenuPath("Commands|Команды", "Synchronize Dirs…|Синхронизация каталогов…");
        var syncDlg = WaitForModal(TimeSpan.FromSeconds(5));

        var compareBtn = syncDlg.FindFirstDescendant(cf => cf.ByName("Compare").Or(cf.ByName("Сравнить")))?.AsButton();
        Assert.That(compareBtn, Is.Not.Null, "Compare button not found");
        compareBtn!.Invoke();

        Thread.Sleep(50); // let the scan actually start
        syncDlg.Close();

        Thread.Sleep(1500);
        Assert.That(MainWindow!.IsAvailable, Is.True, "Main window should still be alive");
        Assert.That(MainWindow!.ModalWindows.Length, Is.EqualTo(0), "SyncDirs dialog should be closed");
    }
}
