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
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n");
        }
        catch (Exception ex)
        {
            // Log write failure — cannot do anything
            System.Diagnostics.Debug.WriteLine($"LogCrash failed: {ex}");
        }
    }
}
