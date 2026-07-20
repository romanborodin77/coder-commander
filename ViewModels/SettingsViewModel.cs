using System.ComponentModel;
using System.Runtime.CompilerServices;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// Модель представления для диалога настроек приложения. Управляет внешним видом, редактором, терминалом и поведением.
/// ViewModel for the application settings dialog. Manages appearance, editor, terminal, and behavior settings.
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly AppSettings _settings;

    /// <summary>
    /// Загружает текущие настройки из хранилища и заполняет свойства ViewModel.
    /// Loads current settings from storage and populates the ViewModel properties.
    /// </summary>
    public SettingsViewModel()
    {
        _settings = SettingsService.Load();
        HotkeysVM = new HotkeyViewModel();
        LoadFromSettings(_settings);
    }

    /// <summary>
    /// ViewModel вкладки «Горячие клавиши». / Hotkeys tab ViewModel.
    /// </summary>
    public HotkeyViewModel HotkeysVM { get; }

    /// <summary>
    /// Возвращает внутренний объект настроек для сохранения.
    /// Returns the internal settings object for saving.
    /// </summary>
    public AppSettings GetSettings() => _settings;

    // ========== Загрузка/сохранение ==========

    /// <summary>
    /// Список доступных языков для ComboBox.
    /// Available languages for the ComboBox.
    /// </summary>
    public List<(string Code, string DisplayName)> AvailableLanguages { get; } = LocalizationService.GetAvailableLanguages();

    /// <summary>
    /// Загружает значения из указанного объекта AppSettings во все свойства ViewModel.
    /// Loads values from the specified AppSettings object into all ViewModel properties.
    /// </summary>
    /// <param name="s">Объект настроек для загрузки.</param>
    public void LoadFromSettings(AppSettings s)
    {
        LanguageIndex = GetLanguageIndex(s.Language);
        ThemeIndex = s.Theme switch
        {
            ThemeMode.Dark => 0,
            ThemeMode.Light => 1,
            ThemeMode.System => 2,
            _ => 0
        };
        ShowHidden = s.ShowHidden;
        EditorFontFamily = s.EditorFontFamily;
        EditorFontSizeIndex = GetFontSizeIndex(s.EditorFontSize);
        EditorShowLineNumbers = s.EditorShowLineNumbers;
        EditorWordWrap = s.EditorWordWrap;
        TerminalShellIndex = s.TerminalShell == "cmd" ? 0 : 1;
        TerminalFontFamily = s.TerminalFontFamily;
        TerminalFontSizeIndex = GetTerminalFontSizeIndex(s.TerminalFontSize);
        TerminalPanelHeight = s.TerminalPanelHeight;
        ConfirmDelete = s.ConfirmDelete;
        ConfirmOverwrite = s.ConfirmOverwrite;
        AutoRefresh = s.AutoRefresh;
        AutoRefreshIntervalIndex = GetAutoRefreshIndex(s.AutoRefreshInterval);
        PanelFontFamily = s.PanelFontFamily;
        PanelFontSizeIndex = GetPanelFontSizeIndex(s.PanelFontSize);
        DefaultOverwritePolicyIndex = GetOverwritePolicyIndex(s.DefaultOverwritePolicy);
        BufferSizeIndex = GetBufferSizeIndex(s.CopyBufferSizeKB);
        CopyAttributes = s.CopyAttributes;
        CopyTimestamps = s.CopyTimestamps;
        ReserveDiskSpace = s.ReserveDiskSpace;
        HotkeysVM.LoadFrom(s);
    }

    /// <summary>
    /// Сохраняет текущие значения свойств ViewModel в указанный объект AppSettings.
    /// Saves current ViewModel property values into the specified AppSettings object.
    /// </summary>
    /// <param name="s">Объект настроек для заполнения.</param>
    public void SaveToSettings(AppSettings s)
    {
        s.Language = GetLanguageFromIndex(LanguageIndex);
        s.Theme = ThemeIndex switch
        {
            0 => ThemeMode.Dark,
            1 => ThemeMode.Light,
            2 => ThemeMode.System,
            _ => ThemeMode.Dark
        };
        s.ShowHidden = ShowHidden;
        s.EditorFontFamily = EditorFontFamily;
        s.EditorFontSize = GetFontSizeFromIndex(EditorFontSizeIndex);
        s.EditorShowLineNumbers = EditorShowLineNumbers;
        s.EditorWordWrap = EditorWordWrap;
        s.TerminalShell = TerminalShellIndex == 0 ? "cmd" : "powershell";
        s.TerminalFontFamily = TerminalFontFamily;
        s.TerminalFontSize = GetTerminalFontSizeFromIndex(TerminalFontSizeIndex);
        s.TerminalPanelHeight = TerminalPanelHeight;
        s.ConfirmDelete = ConfirmDelete;
        s.ConfirmOverwrite = ConfirmOverwrite;
        s.AutoRefresh = AutoRefresh;
        s.AutoRefreshInterval = GetAutoRefreshFromIndex(AutoRefreshIntervalIndex);
        s.PanelFontFamily = PanelFontFamily;
        s.PanelFontSize = GetPanelFontSizeFromIndex(PanelFontSizeIndex);
        s.DefaultOverwritePolicy = GetOverwritePolicyFromIndex(DefaultOverwritePolicyIndex);
        s.CopyBufferSizeKB = GetBufferSizeFromIndex(BufferSizeIndex);
        s.CopyAttributes = CopyAttributes;
        s.CopyTimestamps = CopyTimestamps;
        s.ReserveDiskSpace = ReserveDiskSpace;
        HotkeysVM.SaveTo(s);
    }

    /// <summary>
    /// Сбрасывает все настройки к значениям по умолчанию.
    /// Resets all settings to their default values.
    /// </summary>
    public void ResetToDefaults()
    {
        LoadFromSettings(new AppSettings());
        HotkeysVM.ResetToDefaults();
    }

    // ========== Конвертеры индексов ==========

    private static int GetLanguageIndex(string code)
    {
        var langs = LocalizationService.GetAvailableLanguages();
        for (int i = 0; i < langs.Count; i++)
            if (langs[i].Code == code) return i;
        return 0; // English by default
    }

    private static string GetLanguageFromIndex(int idx)
    {
        var langs = LocalizationService.GetAvailableLanguages();
        if (idx >= 0 && idx < langs.Count) return langs[idx].Code;
        return "en";
    }

    private static int GetFontSizeIndex(double size) => size switch
    {
        10 => 0, 11 => 1, 12 => 2, 13 => 3, 14 => 4, 15 => 5, 16 => 6, 18 => 7, 20 => 8, _ => 4
    };
    private static double GetFontSizeFromIndex(int idx) => idx switch
    {
        0 => 10, 1 => 11, 2 => 12, 3 => 13, 4 => 14, 5 => 15, 6 => 16, 7 => 18, 8 => 20, _ => 14
    };
    private static int GetTerminalFontSizeIndex(double size) => size switch
    {
        10 => 0, 11 => 1, 12 => 2, 14 => 3, 16 => 4, _ => 2
    };
    private static double GetTerminalFontSizeFromIndex(int idx) => idx switch
    {
        0 => 10, 1 => 11, 2 => 12, 3 => 14, 4 => 16, _ => 12
    };
    private static int GetAutoRefreshIndex(int ms) => ms switch
    {
        1000 => 0, 2000 => 1, 3000 => 2, 5000 => 3, _ => 1
    };
    private static int GetAutoRefreshFromIndex(int idx) => idx switch
    {
        0 => 1000, 1 => 2000, 2 => 3000, 3 => 5000, _ => 2000
    };
    private static int GetPanelFontSizeIndex(double size) => size switch
    {
        11 => 0, 12 => 1, 13 => 2, 14 => 3, 15 => 4, 16 => 5, 17 => 6, _ => 2
    };
    private static double GetPanelFontSizeFromIndex(int idx) => idx switch
    {
        0 => 11, 1 => 12, 2 => 13, 3 => 14, 4 => 15, 5 => 16, 6 => 17, _ => 13
    };

    private static int GetOverwritePolicyIndex(string policy) => policy switch
    {
        "Ask" => 0, "Always" => 1, "Never" => 2, "OverwriteOlder" => 3, "OverwriteSmaller" => 4, "AutoRename" => 5, _ => 0
    };
    private static string GetOverwritePolicyFromIndex(int idx) => idx switch
    {
        0 => "Ask", 1 => "Always", 2 => "Never", 3 => "OverwriteOlder", 4 => "OverwriteSmaller", 5 => "AutoRename", _ => "Ask"
    };
    private static int GetBufferSizeIndex(int kb) => kb switch
    {
        256 => 0, 512 => 1, 1024 => 2, 2048 => 3, 4096 => 4, _ => 2
    };
    private static int GetBufferSizeFromIndex(int idx) => idx switch
    {
        0 => 256, 1 => 512, 2 => 1024, 3 => 2048, 4 => 4096, _ => 1024
    };

    // ========== Binding-свойства ==========

    /// <summary>Индекс выбранного языка (0=English, 1=Русский, ...).</summary>
    private int _languageIndex;
    public int LanguageIndex { get => _languageIndex; set { _languageIndex = value; OnPropertyChanged(); } }

    /// <summary>Индекс выбранной темы (0=Тёмная, 1=Светлая, 2=Системная).</summary>
    private int _themeIndex;
    public int ThemeIndex { get => _themeIndex; set { _themeIndex = value; OnPropertyChanged(); } }

    /// <summary>Показывать скрытые файлы по умолчанию.</summary>
    private bool _showHidden;
    public bool ShowHidden { get => _showHidden; set { _showHidden = value; OnPropertyChanged(); } }

    /// <summary>Шрифт редактора кода.</summary>
    private string _editorFontFamily = "Cascadia Code";
    public string EditorFontFamily { get => _editorFontFamily; set { _editorFontFamily = value; OnPropertyChanged(); } }

    /// <summary>Индекс размера шрифта редактора (0-8, от 10 до 20).</summary>
    private int _editorFontSizeIndex = 4;
    public int EditorFontSizeIndex { get => _editorFontSizeIndex; set { _editorFontSizeIndex = value; OnPropertyChanged(); } }

    /// <summary>Показывать номера строк в редакторе.</summary>
    private bool _editorShowLineNumbers = true;
    public bool EditorShowLineNumbers { get => _editorShowLineNumbers; set { _editorShowLineNumbers = value; OnPropertyChanged(); } }

    /// <summary>Перенос строк в редакторе.</summary>
    private bool _editorWordWrap;
    public bool EditorWordWrap { get => _editorWordWrap; set { _editorWordWrap = value; OnPropertyChanged(); } }

    /// <summary>Индекс оболочки терминала (0=cmd, 1=powershell).</summary>
    private int _terminalShellIndex;
    public int TerminalShellIndex { get => _terminalShellIndex; set { _terminalShellIndex = value; OnPropertyChanged(); } }

    /// <summary>Шрифт терминала.</summary>
    private string _terminalFontFamily = "Consolas";
    public string TerminalFontFamily { get => _terminalFontFamily; set { _terminalFontFamily = value; OnPropertyChanged(); } }

    /// <summary>Индекс размера шрифта терминала (0-4, от 10 до 16).</summary>
    private int _terminalFontSizeIndex = 2;
    public int TerminalFontSizeIndex { get => _terminalFontSizeIndex; set { _terminalFontSizeIndex = value; OnPropertyChanged(); } }

    /// <summary>Высота панели терминала в пикселях.</summary>
    private double _terminalPanelHeight = 300;
    public double TerminalPanelHeight { get => _terminalPanelHeight; set { _terminalPanelHeight = value; OnPropertyChanged(); } }

    /// <summary>Запрашивать подтверждение при удалении.</summary>
    private bool _confirmDelete = true;
    public bool ConfirmDelete { get => _confirmDelete; set { _confirmDelete = value; OnPropertyChanged(); } }

    /// <summary>Запрашивать подтверждение при перезаписи файлов.</summary>
    private bool _confirmOverwrite = true;
    public bool ConfirmOverwrite { get => _confirmOverwrite; set { _confirmOverwrite = value; OnPropertyChanged(); } }

    /// <summary>Автоматически обновлять содержимое панелей.</summary>
    private bool _autoRefresh = true;
    public bool AutoRefresh { get => _autoRefresh; set { _autoRefresh = value; OnPropertyChanged(); } }

    /// <summary>Индекс интервала автообновления (0-3: 1с, 2с, 3с, 5с).</summary>
    private int _autoRefreshIntervalIndex = 1;
    public int AutoRefreshIntervalIndex { get => _autoRefreshIntervalIndex; set { _autoRefreshIntervalIndex = value; OnPropertyChanged(); } }

    /// <summary>Шрифт списка файлов в панелях. / Panel file list font family.</summary>
    private string _panelFontFamily = "Segoe UI Variable, Segoe UI";
    public string PanelFontFamily { get => _panelFontFamily; set { _panelFontFamily = value; OnPropertyChanged(); } }

    /// <summary>Индекс размера шрифта панели (0-6: 11–17). / Panel font size index.</summary>
    private int _panelFontSizeIndex = 2;
    public int PanelFontSizeIndex { get => _panelFontSizeIndex; set { _panelFontSizeIndex = value; OnPropertyChanged(); } }

    // ═══════════════════════════════════════════
    // FILE OPERATIONS (ph9.5)
    // ═══════════════════════════════════════════

    /// <summary>Индекс политики перезаписи (0=Ask, 1=Always, 2=Never, 3=Older, 4=Smaller, 5=AutoRename).</summary>
    private int _defaultOverwritePolicyIndex;
    public int DefaultOverwritePolicyIndex { get => _defaultOverwritePolicyIndex; set { _defaultOverwritePolicyIndex = value; OnPropertyChanged(); } }

    /// <summary>Индекс размера буфера (0=256, 1=512, 2=1024, 3=2048, 4=4096).</summary>
    private int _bufferSizeIndex = 2;
    public int BufferSizeIndex { get => _bufferSizeIndex; set { _bufferSizeIndex = value; OnPropertyChanged(); } }

    /// <summary>Копировать атрибуты файлов. / Copy file attributes.</summary>
    private bool _copyAttributes = true;
    public bool CopyAttributes { get => _copyAttributes; set { _copyAttributes = value; OnPropertyChanged(); } }

    /// <summary>Копировать временные метки. / Copy timestamps.</summary>
    private bool _copyTimestamps = true;
    public bool CopyTimestamps { get => _copyTimestamps; set { _copyTimestamps = value; OnPropertyChanged(); } }

    /// <summary>Резервировать место на диске. / Reserve disk space.</summary>
    private bool _reserveDiskSpace;
    public bool ReserveDiskSpace { get => _reserveDiskSpace; set { _reserveDiskSpace = value; OnPropertyChanged(); } }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
