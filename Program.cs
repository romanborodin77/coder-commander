using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.FileSystem.Remote;
using CoderCommander.FileSystem.Remote.Ftp;
using CoderCommander.FileSystem.Remote.Sftp;
using CoderCommander.Archives.SharpCompress;
using CoderCommander.Archives.Tar;
using CoderCommander.Archives.Zip;
using CoderCommander.Services;
using CoderCommander.ViewModels;
using CoderCommander.Views;
using CoderCommander.Viewers;
using CoderCommander.Viewers.Formats;
using System.Text;

namespace CoderCommander;

/// <summary>
/// WinForms application entry point.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ArchiveFormatRegistry.Register(ZipArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(TarArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(TarGzArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(SevenZipArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(RarArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(TarBz2ArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(TarXzArchiveFormat.Instance);

        FileSystemProviderRegistry.Register(WebDavProvider.Instance);
        FileSystemProviderRegistry.Register(FtpProvider.Instance);
        FileSystemProviderRegistry.Register(SftpProvider.Instance);

        // Universal formats first (registration order = toolbar button order), then matched
        // formats - see ViewerFormatRegistry's own doc comment for the extension/signature
        // detection rules this feeds.
        ViewerFormatRegistry.Register(TextViewerFormat.Instance);
        ViewerFormatRegistry.Register(AsciiViewerFormat.Instance);
        ViewerFormatRegistry.Register(BinaryViewerFormat.Instance);
        ViewerFormatRegistry.Register(HexViewerFormat.Instance);
        ViewerFormatRegistry.Register(ImageViewerFormat.Instance);
        ViewerFormatRegistry.Register(CsvViewerFormat.Instance);
        ViewerFormatRegistry.Register(MarkdownViewerFormat.Instance);
        ViewerFormatRegistry.Register(HtmlViewerFormat.Instance);
        ViewerFormatRegistry.Register(PdfViewerFormat.Instance);
        ViewerFormatRegistry.Register(MediaViewerFormat.Instance);
        ViewerFormatRegistry.Register(OfficeWordViewerFormat.Instance);
        ViewerFormatRegistry.Register(OfficeSheetViewerFormat.Instance);
        ViewerFormatRegistry.Register(OfficeSlidesViewerFormat.Instance);

        // Best-effort cleanup of any viewer/materialize temp-session folder a previous run left
        // behind (crash, kill -9, a killed UiTests host) - see ViewerTempSession.SweepOrphans's own
        // doc comment; "materialize" is PanelViewModel's own per-panel session category (archives
        // browsed/packed/unpacked from a non-local container).
        ViewerTempSession.SweepOrphans();
        TempSessionRoot.SweepOrphans("materialize");

        LogCrash("=== App startup ===");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogCrash("UNHANDLED: " + args.ExceptionObject);
            try
            {
                MessageBox.Show(args.ExceptionObject?.ToString(), "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                LogCrash("MessageBox failed: " + ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("UNOBSERVED TASK: " + args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.FirstChanceException += (_, args) => LogFirstChanceException(args.Exception);

        // UI-tree dump for fast layout inspection (JSON, not a screenshot) - gated behind an env
        // var so it never activates outside a debug-mode launch; F12 is unmodified because
        // modifier-combo key injection through UI-automation tooling has been empirically
        // unreliable (see DiagnosticCommandChannel's own doc comment for the same reasoning).
        if (Environment.GetEnvironmentVariable(DiagnosticCommandChannel.EnvironmentVariable) == "1")
            Application.AddMessageFilter(new UiDumpMessageFilter());

        ApplicationConfiguration.Initialize();

        // Apply theme
        ThemeService.ApplyTheme(SettingsService.GetEffectiveTheme());

        // Load saved language
        var lang = SettingsService.Load().Language;
        if (!string.IsNullOrEmpty(lang))
            LocalizationService.Current.LoadLanguage(lang);

        // Create ViewModel and main form
        var vm = new MainViewModel();
        using var mainForm = new MainForm(vm);
        mainForm.FormClosed += (_, _) => vm.Dispose();

        // invoke_command channel for automated UI tests - see DiagnosticCommandChannel's own doc
        // comment. Gated by the same env var as the UI-tree dump above; a no-op call outside a
        // debug-mode launch.
        DiagnosticCommandChannel.Start(vm, mainForm);
        mainForm.FormClosed += (_, _) => DiagnosticCommandChannel.Stop();

        Application.Run(mainForm);
    }

    private static readonly string CrashLogPath = Path.Combine(Path.GetTempPath(), "CoderCommander_crash.log");

    /// <summary>Crash log is rotated to <c>.old</c> once it passes this size, the same one-generation
    /// scheme <see cref="LogService"/> already uses for app.log - previously this file was never
    /// rotated at all and grew without bound for as long as %TEMP% kept it around, which on a
    /// machine that runs the app for months (or crashes in a loop) is an unbounded-size file
    /// nothing ever cleans up.</summary>
    private const long MaxCrashLogSizeBytes = 5 * 1024 * 1024; // 5 MB, matching LogService

    private static void LogCrash(string msg)
    {
        try
        {
            RotateCrashLogIfTooLarge();

            // Full date, not just time-of-day: even with rotation, one generation can still span
            // more than a day, so a bare HH:mm:ss.fff timestamp would make entries from different
            // days indistinguishable when merged with app.log by time.
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\r\n");
        }
        catch (Exception ex)
        {
            // Log write failure — cannot do anything
            System.Diagnostics.Debug.WriteLine($"LogCrash failed: {ex}");
        }
    }

    /// <summary>Best-effort, deliberately as simple as <see cref="LogCrash"/> itself has to be:
    /// this runs on the crash path, potentially before the app has reached any themed or otherwise
    /// working state, so a failure here must never throw back into the caller - a failed rotation
    /// should cost nothing worse than an unrotated log entry.</summary>
    private static void RotateCrashLogIfTooLarge()
    {
        try
        {
            Utils.LogRotation.RotateIfTooLarge(CrashLogPath, MaxCrashLogSizeBytes);
        }
        catch
        {
            // Best-effort - a failed rotation must not stop crash logging, which is itself the
            // last-resort diagnostic when everything else has already gone wrong.
        }
    }

    // Throttles FirstChanceException logging: a caught-and-swallowed exception thrown in a
    // tight loop (e.g. a per-item try/catch during a large copy) would otherwise flood
    // app.log with a full stack trace per iteration. Keyed by exception type + originating
    // stack frame, one log line per key per window.
    private static readonly Dictionary<string, DateTime> _firstChanceLastLogged = new();
    private static readonly TimeSpan _firstChanceThrottleWindow = TimeSpan.FromSeconds(2);
    private static readonly object _firstChanceLock = new();

    private static void LogFirstChanceException(Exception ex)
    {
        try
        {
            var key = ex.GetType().FullName + "|" + (ex.StackTrace?.Split('\n').FirstOrDefault() ?? "");
            lock (_firstChanceLock)
            {
                var now = DateTime.Now;
                if (_firstChanceLastLogged.TryGetValue(key, out var last) && now - last < _firstChanceThrottleWindow)
                    return;
                _firstChanceLastLogged[key] = now;
            }
            LogService.Debug($"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", "FirstChance");
        }
        catch
        {
            // Logging a first-chance exception must never itself throw back into the
            // exception machinery it's observing.
        }
    }
}

/// <summary>Watches for an unmodified F12 keydown and dumps the active form's control tree
/// via <see cref="UiDumpService"/> - see Program.Main for the debug-mode env var gate.</summary>
internal sealed class UiDumpMessageFilter : IMessageFilter
{
    private const int WM_KEYDOWN = 0x0100;

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg == WM_KEYDOWN && (Keys)m.WParam.ToInt32() == Keys.F12)
            UiDumpService.DumpActiveFormToFile();
        return false;
    }
}
