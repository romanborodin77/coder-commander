using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander;

/// <summary>
/// Главный класс приложения WPF. Управляет запуском, обработкой исключений и переключением темы.
/// Main WPF application class. Manages startup, exception handling, and theme switching.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Путь к файлу журнала аварийных сбоев во временной папке.
    /// Path to the crash log file in the temp folder.
    /// </summary>
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "CoderCommander_crash.log");

    /// <summary>
    /// Записывает сообщение в журнал аварийных сбоев с временной меткой.
    /// Writes a message to the crash log with a timestamp.
    /// </summary>
    /// <param name="msg">Текст сообщения / Message text.</param>
    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + msg + "\r\n"); } catch { }
    }

    /// <summary>
    /// Вызывается при запуске приложения. Настраивает глобальные обработчики исключений и применяет сохранённую тему.
    /// Called when the application starts. Sets up global exception handlers and applies the saved theme.
    /// </summary>
    /// <param name="e">Аргументы события запуска / Startup event arguments.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log("=== App startup ===");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log("UNHANDLED: " + args.ExceptionObject);
            try { MessageBox.Show(args.ExceptionObject?.ToString(), "Fatal (AppDomain)"); } catch { }
        };
        DispatcherUnhandledException += (_, args) =>
        {
            Log("DISPATCHER: " + args.Exception);
            try { MessageBox.Show(args.Exception.ToString(), "Необработанное исключение"); } catch { }
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("UNOBSERVED TASK: " + args.Exception);
            args.SetObserved();
        };

        // Загружаем сохранённую тему
        ApplyTheme(SettingsService.GetEffectiveTheme());

        // Регистрируем провайдеры содержимого по умолчанию (Local FS + Archive)
        // Register default content providers (Local FS + Archive)
        ContentProviderRegistry.Instance.RegisterDefaults();

        // Загружаем сохранённый язык / Load saved language
        var lang = SettingsService.Load().Language;
        LocalizationService.Current.LoadLanguage(lang);

        // Применяем шрифт панели (ph6.5) / Apply panel font
        var settings = SettingsService.Load();
        Resources["PanelFontFamily"] = new System.Windows.Media.FontFamily(settings.PanelFontFamily);
        Resources["PanelFontSize"] = settings.PanelFontSize;
    }

    /// <summary>
    /// Вызывается после смены темы, чтобы код-behind элементы (редактор и т.п.)
    /// могли переприменить кисти, недоступные через DynamicResource.
    /// Raised after a theme change so code-behind elements (editor, etc.)
    /// can re-apply brushes that are not accessible via DynamicResource.
    /// </summary>
    public event EventHandler? ThemeChanged;

    /// <summary>
    /// Кэшированный экземпляр словаря ресурсов текущей темы.
    /// Cached resource dictionary instance for the current theme.
    /// </summary>
    private ResourceDictionary? _themeDict;

    /// <summary>
    /// Применяет тему (Dark/Light/System), загружая соответствующий ResourceDictionary.
    /// Словарь меняется «на месте» через .Source — это надёжно пересчитывает ВСЕ DynamicResource
    /// во всём визуальном дереве (фоны, границы, текст, панели).
    /// Applies a theme (Dark/Light/System) by loading the corresponding ResourceDictionary.
    /// The dictionary is updated in-place via .Source, which reliably recalculates ALL DynamicResource
    /// references throughout the visual tree (backgrounds, borders, text, panels).
    /// </summary>
    /// <param name="theme">Режим темы: Dark, Light или System / Theme mode: Dark, Light, or System.</param>
    public void ApplyTheme(ThemeMode theme)
    {
        var effective = theme switch
        {
            ThemeMode.Light => ThemeMode.Light,
            ThemeMode.System => GetSystemTheme(),
            _ => ThemeMode.Dark
        };

        var uri = effective == ThemeMode.Light
            ? new Uri("Resources/Themes/Light.xaml", UriKind.Relative)
            : new Uri("Resources/Themes/Dark.xaml", UriKind.Relative);

        // Переиспользуем один и тот же экземпляр словаря и только меняем его Source.
        // Это гарантирует пересчёт всех DynamicResource-ссылок, в отличие от Clear()+Add(),
        // которое в ряде случаев не обновляет уже отрисованные элементы.
        _themeDict ??= new ResourceDictionary();
        if (!Resources.MergedDictionaries.Contains(_themeDict))
            Resources.MergedDictionaries.Add(_themeDict);
        _themeDict.Source = uri;

        // Обновляем singleton-кисти для ControlTemplate
        // Update singleton brushes for ControlTemplate
        ThemeColors.Update(effective);

        // Уведомляем подписчиков (редактор, панели), чтобы обновить кисти из кода.
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Определяет текущую системную тему Windows из реестра (Personalize\AppsUseLightTheme).
    /// Determines the current Windows system theme from the registry (Personalize\AppsUseLightTheme).
    /// </summary>
    /// <returns>ThemeMode.Dark или ThemeMode.Light в зависимости от системной настройки / ThemeMode.Dark or ThemeMode.Light based on the system setting.</returns>
    private static ThemeMode GetSystemTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
                return v == 0 ? ThemeMode.Dark : ThemeMode.Light;
        }
        catch { }
        return ThemeMode.Dark;
    }
}
