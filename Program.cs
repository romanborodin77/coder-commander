using CoderCommander.Archives;
using CoderCommander.Archives.SharpCompress;
using CoderCommander.Archives.Tar;
using CoderCommander.Archives.Zip;
using CoderCommander.Services;
using CoderCommander.ViewModels;
using CoderCommander.Views;
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

        // UI-tree dump for the dotnet-debugger MCP server's layout checker (check_layout()) -
        // gated behind an env var so it never activates outside an automated debugging
        // session; F12 is unmodified because modifier-combo key injection through that
        // server's PostMessage-based input has been empirically unreliable.
        if (Environment.GetEnvironmentVariable("CODERCOMMANDER_UI_DEBUG") == "1")
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
        var mainForm = new MainForm(vm);
        mainForm.FormClosed += (_, _) => vm.Dispose();

        Application.Run(mainForm);
    }

    private static readonly string CrashLogPath = Path.Combine(Path.GetTempPath(), "CoderCommander_crash.log");

    private static void LogCrash(string msg)
    {
        try
        {
            // Full date, not just time-of-day: this file is never rotated and accumulates
            // entries across the app's entire history, so a bare HH:mm:ss.fff timestamp made
            // entries from different days indistinguishable when merged with app.log by time.
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\r\n");
        }
        catch (Exception ex)
        {
            // Log write failure — cannot do anything
            System.Diagnostics.Debug.WriteLine($"LogCrash failed: {ex}");
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
/// via <see cref="UiDumpService"/> - see Program.Main for the CODERCOMMANDER_UI_DEBUG gate.</summary>
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
