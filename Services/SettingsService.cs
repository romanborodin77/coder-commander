using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// Режим темы: тёмная, светлая или системная.
/// Theme mode: dark, light, or system.
/// </summary>
public enum ThemeMode
{
    /// <summary>Тёмная тема. / Dark theme.</summary>
    Dark,
    /// <summary>Светлая тема. / Light theme.</summary>
    Light,
    /// <summary>Системная тема (определяется из реестра Windows). / System theme (detected from Windows registry).</summary>
    System
}

/// <summary>
/// Модель настроек приложения, хранимых в JSON.
/// Application settings model, persisted in JSON.
/// </summary>
public class AppSettings
{
    //Внешний вид / Theme appearance

    /// <summary>
    /// Режим темы. / Theme mode.
    /// </summary>
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    /// <summary>
    /// Код языка интерфейса (например, "en", "ru"). / Interface language code (e.g., "en", "ru").
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Ширина главного окна. / Main window width.
    /// </summary>
    public double WindowWidth { get; set; } = 1500;

    /// <summary>
    /// Высота главного окна. / Main window height.
    /// </summary>
    public double WindowHeight { get; set; } = 850;

    /// <summary>
    /// Доля ширины левой панели (0..1). / Left panel width fraction (0..1).
    /// </summary>
    public double LeftPanelWidth { get; set; } = 0.5;

    /// <summary>
    /// Показывать скрытые файлы. / Show hidden files.
    /// </summary>
    public bool ShowHidden { get; set; }

    /// <summary>
    /// Последний открытый путь. / Last opened path.
    /// </summary>
    public string LastPath { get; set; } = "";

    //Редактор / Editor

    /// <summary>
    /// Шрифт редактора. / Editor font family.
    /// </summary>
    public string EditorFontFamily { get; set; } = "Cascadia Code";

    /// <summary>
    /// Размер шрифта редактора. / Editor font size.
    /// </summary>
    public double EditorFontSize { get; set; } = 14;

    /// <summary>
    /// Показывать номера строк. / Show line numbers.
    /// </summary>
    public bool EditorShowLineNumbers { get; set; } = true;

    /// <summary>
    /// Перенос слов в редакторе. / Editor word wrap.
    /// </summary>
    public bool EditorWordWrap { get; set; }

    /// <summary>
    /// Ширина табуляции (количество пробелов). / Tab width (number of spaces).
    /// </summary>
    public int EditorTabWidth { get; set; } = 4;

    /// <summary>
    /// Использовать пробелы вместо табуляции. / Use spaces instead of tabs.
    /// </summary>
    public bool EditorUseSpaces { get; set; } = true;

    /// <summary>
    /// Показывать вертикальную линию-линейку (правая граница). / Show column ruler line.
    /// </summary>
    public bool EditorShowColumnRuler { get; set; }

    /// <summary>
    /// Позиция линейки (столбец). / Column ruler position.
    /// </summary>
    public int EditorColumnRulerPosition { get; set; } = 80;

    /// <summary>
    /// Показывать пробелы. / Show whitespace characters.
    /// </summary>
    public bool EditorShowSpaces { get; set; }

    /// <summary>
    /// Показывать табуляцию. / Show tab characters.
    /// </summary>
    public bool EditorShowTabs { get; set; }

    /// <summary>
    /// Показывать концы строк. / Show end-of-line characters.
    /// </summary>
    public bool EditorShowEndOfLine { get; set; }

    //Терминал / Terminal

    /// <summary>
    /// Тип оболочки (cmd, powershell). / Terminal shell (cmd, powershell).
    /// </summary>
    public string TerminalShell { get; set; } = "cmd";

    /// <summary>
    /// Высота панели терминала в пикселях. / Terminal panel height in pixels.
    /// </summary>
    public double TerminalPanelHeight { get; set; } = 300;

    /// <summary>
    /// Шрифт терминала. / Terminal font family.
    /// </summary>
    public string TerminalFontFamily { get; set; } = "Consolas";

    /// <summary>
    /// Размер шрифта терминала. / Terminal font size.
    /// </summary>
    public double TerminalFontSize { get; set; } = 12;

    //Поведение / Behavior

    /// <summary>
    /// Подтверждение удаления. / Confirm delete.
    /// </summary>
    public bool ConfirmDelete { get; set; } = true;

    /// <summary>
    /// Подтверждение перезаписи. / Confirm overwrite.
    /// </summary>
    public bool ConfirmOverwrite { get; set; } = true;

    /// <summary>
    /// Автообновление содержимого панелей. / Auto-refresh panel contents.
    /// </summary>
    public bool AutoRefresh { get; set; } = true;

    /// <summary>
    /// Интервал автообновления в миллисекундах. / Auto-refresh interval in milliseconds.
    /// </summary>
    public int AutoRefreshInterval { get; set; } = 2000;

    //══════════════ / Panel (ph6.5)

    /// <summary>
    /// Шрифт списка файлов в панелях. / Panel file list font family.
    /// </summary>
    public string PanelFontFamily { get; set; } = "Segoe UI Variable, Segoe UI";

    /// <summary>
    /// Размер шрифта списка файлов в панелях. / Panel file list font size.
    /// </summary>
    public double PanelFontSize { get; set; } = 13;

    //Закладки / Bookmarks

    /// <summary>
    /// Список закладок (любимые директории). / List of bookmarks (favorite directories).
    /// </summary>
    public List<BookmarkItem> Bookmarks { get; set; } = new();

    //Вкладки панелей / Panel Tabs (ph5.9)

    /// <summary>
    /// Пути левых вкладок для восстановления при старте. / Left tab paths to restore on startup.
    /// </summary>
    public List<string> LeftTabPaths { get; set; } = new();

    /// <summary>
    /// Пути правых вкладок для восстановления при старте. / Right tab paths to restore on startup.
    /// </summary>
    public List<string> RightTabPaths { get; set; } = new();

    // Горячие клавиши (ph6.1) / Hotkeys (ph6.1)

    /// <summary>
    /// Пользовательские привязки горячих клавиш. / Custom hotkey bindings.
    /// </summary>
    public List<HotkeyItem> Hotkeys { get; set; } = new();

    // Макросы (ph8.2) / Macros (ph8.2)

    /// <summary>
    /// Пользовательские макросы. / User-defined macros.
    /// </summary>
    public List<MacroItem> Macros { get; set; } = new();

    // Плагины (ph8.3) / Plugins (ph8.3)

    /// <summary>
    /// Идентификаторы включённых плагинов. / IDs of enabled plugins.
    /// </summary>
    public List<string> EnabledPlugins { get; set; } = new();

    // Облачные хранилища (ph8.4) / Cloud storage (ph8.4)

    /// <summary>
    /// Профили облачных хранилищ (S3, Azure, GDrive). / Cloud storage profiles (S3, Azure, GDrive).
    /// </summary>
    public List<CloudProfile> CloudProfiles { get; set; } = new();

    // Файловые операции / File operations

    /// <summary>
    /// Политика перезаписи по умолчанию: Ask, Always, Never, OverwriteOlder, OverwriteSmaller, AutoRename.
    /// Default overwrite policy: Ask, Always, Never, OverwriteOlder, OverwriteSmaller, AutoRename.
    /// </summary>
    public string DefaultOverwritePolicy { get; set; } = "Ask";

    /// <summary>
    /// Размер буфера копирования в КБ. / Copy buffer size in KB.
    /// </summary>
    public int CopyBufferSizeKB { get; set; } = 1024;

    /// <summary>
    /// Копировать атрибуты файлов. / Copy file attributes.
    /// </summary>
    public bool CopyAttributes { get; set; } = true;

    /// <summary>
    /// Копировать временные метки файлов. / Copy file timestamps.
    /// </summary>
    public bool CopyTimestamps { get; set; } = true;

    /// <summary>
    /// Резервировать место на диске перед копированием. / Reserve disk space before copying.
    /// </summary>
    public bool ReserveDiskSpace { get; set; }

    /// <summary>
    /// Копировать NTFS ACL (права доступа). / Copy NTFS permissions.
    /// </summary>
    public bool CopyNtfsPermissions { get; set; }

    // ═══════════════ Дополнительные настройки (ph9.6) ═══════════════

    // Внешний вид

    /// <summary>Прозрачность главного окна (0.5..1.0). / Main window opacity.</summary>
    public double WindowOpacity { get; set; } = 1.0;

    /// <summary>Показывать полный путь в заголовке. / Show full path in title bar.</summary>
    public bool ShowFullPathInTitle { get; set; } = true;

    /// <summary>Показывать размер файлов в панелях. / Show file sizes in panels.</summary>
    public bool ShowFileSize { get; set; } = true;

    /// <summary>Показывать дату модификации. / Show modification date.</summary>
    public bool ShowModificationDate { get; set; } = true;

    /// <summary>Показывать атрибуты файлов. / Show file attributes.</summary>
    public bool ShowFileAttributes { get; set; } = true;

    /// <summary>Сортировка папок сверху. / Folders first sorting.</summary>
    public bool SortFoldersFirst { get; set; } = true;

    // Панели

    /// <summary>Разделитель путей — последний открытый. / Last active panel (0=left, 1=right).</summary>
    public int ActivePanel { get; set; }

    /// <summary>Показывать дерево каталогов. / Show directory tree.</summary>
    public bool ShowDirectoryTree { get; set; }

    /// <summary>Открывать папки двойным кликом. / Open folders with double-click.</summary>
    public bool DoubleClickOpenFolder { get; set; } = true;

    /// <summary>Показывать скрытые папки. / Show hidden folders.</summary>
    public bool ShowHiddenFolders { get; set; } = true;

    // Терминал

    /// <summary>Высота панели терминала по умолчанию. / Default terminal panel height.</summary>
    public double DefaultTerminalHeight { get; set; } = 300;

    /// <summary>Количество строк прокрутки терминала. / Terminal scrollback lines.</summary>
    public int TerminalScrollbackLines { get; set; } = 9999;

    /// <summary>Курсор терминала (block/underscore/vertical-bar). / Terminal cursor style.</summary>
    public string TerminalCursorStyle { get; set; } = "block";

    // Редактор

    /// <summary>Подсвечивать текущую строку. / Highlight current line.</summary>
    public bool EditorHighlightCurrentLine { get; set; } = true;

    /// <summary>Подсвечивать парные скобки. / Highlight matching brackets.</summary>
    public bool EditorHighlightBrackets { get; set; } = true;

    /// <summary>Автодополнение скобок. / Auto-close brackets.</summary>
    public bool EditorAutoCloseBrackets { get; set; } = true;

    /// <summary>Автодополнение кавычек. / Auto-close quotes.</summary>
    public bool EditorAutoCloseQuotes { get; set; }

    /// <summary>Минимальная длина слова для подсветки. / Min word length for highlight.</summary>
    public int EditorMinHighlightLength { get; set; } = 2;

    /// <summary>Размер отступа в пикселях (left margin). / Editor indent size in pixels.</summary>
    public int EditorIndentSize { get; set; } = 4;

    // Поведение

    /// <summary>Показывать панель инструментов. / Show toolbar.</summary>
    public bool ShowToolbar { get; set; } = true;

    /// <summary>Показывать строку состояния. / Show status bar.</summary>
    public bool ShowStatusBar { get; set; } = true;

    /// <summary>Показывать путь в заголовке панели. / Show path in panel title.</summary>
    public bool ShowPathInPanelTitle { get; set; } = true;

    /// <summary>Запоминать последние пути. / Remember last paths.</summary>
    public bool RememberLastPaths { get; set; } = true;

    /// <summary>Количество последних путей. / Number of recent paths.</summary>
    public int RecentPathsCount { get; set; } = 10;

    /// <summary>Двойной клик — открыть файл. / Double-click opens file.</summary>
    public bool DoubleClickOpenFile { get; set; } = true;

    /// <summary>Показывать полные пути в панелях. / Show full paths in panels.</summary>
    public bool ShowFullPathsInPanels { get; set; }

    // Файловые операции

    /// <summary>Проверять целостность после копирования. / Verify after copy.</summary>
    public bool VerifyAfterCopy { get; set; }

    /// <summary>Прерывать при ошибке. / Abort on error.</summary>
    public bool AbortOnError { get; set; } = true;

    /// <summary>Максимальная глубина рекурсии. / Max recursion depth.</summary>
    public int MaxRecursionDepth { get; set; } = 50;

    /// <summary>Показывать очередь операций автоматически. / Auto-show operation queue.</summary>
    public bool AutoShowQueue { get; set; } = true;
}

/// <summary>
/// Сервис загрузки и сохранения настроек приложения в JSON-файл.
/// Service for loading and saving application settings to JSON file.
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoderCommander");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");
    private static volatile AppSettings? _current;

    /// <summary>
    /// Загружает настройки из файла (с кэшированием) или возвращает настройки по умолчанию.
    /// Loads settings from file (with caching) or returns default settings.
    /// </summary>
    /// <returns>Загруженные или настройки по умолчанию. / Loaded or default settings.</returns>
    public static AppSettings Load()
    {
        var snapshot = _current;
        if (snapshot is not null) return snapshot;
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                snapshot = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                _current = snapshot;
                return snapshot;
            }
        }
        catch
        {
            // При ошибке чтения — используем настройки по умолчанию.
            // On read error — use default settings.
        }

        snapshot = new AppSettings();
        _current = snapshot;
        return snapshot;
    }

    /// <summary>
    /// Сохраняет настройки в JSON-файл.
    /// Saves settings to JSON file.
    /// </summary>
    /// <param name="settings">Объект настроек. / Settings object.</param>
    public static void Save(AppSettings settings)
    {
        _current = settings;
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, SettingsPath, overwrite: true);
        }
        catch
        {
            // При ошибке записи — молча игнорируем.
            // On write error — silently ignore.
        }
    }

    /// <summary>
    /// Возвращает действующую тему: если выбрана System, определяет тему из реестра Windows.
    /// Returns effective theme: if System is selected, detects theme from Windows registry.
    /// </summary>
    /// <returns>Тёмная или светлая тема. / Dark or light theme.</returns>
    public static ThemeMode GetEffectiveTheme()
    {
        var s = Load().Theme;
        if (s != ThemeMode.System) return s;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
                return v == 0 ? ThemeMode.Dark : ThemeMode.Light;
        }
        catch
        {
            // Fallback при ошибке реестра.
            // Fallback on registry error.
        }
        return ThemeMode.Dark;
    }

    /// <summary>
    /// Возвращает хоткеи по умолчанию (F2–F8 + Alt+F1/F2/F7).
    /// Returns default hotkeys (F2–F8 + Alt+F1/F2/F7).
    /// </summary>
    public static List<HotkeyItem> GetDefaultHotkeys() => new()
    {
        new() { Action = "File.Rename", Key = "F2", Category = "Файл", Description = "Переименовать файл" },
        new() { Action = "File.View", Key = "F3", Category = "Файл", Description = "Просмотр файла" },
        new() { Action = "File.Edit", Key = "F4", Category = "Файл", Description = "Редактировать файл" },
        new() { Action = "File.Copy", Key = "F5", Category = "Файл", Description = "Копировать" },
        new() { Action = "File.Move", Key = "F6", Category = "Файл", Description = "Переместить" },
        new() { Action = "File.CreateFolder", Key = "F7", Category = "Файл", Description = "Создать папку" },
        new() { Action = "File.Delete", Key = "F8", Category = "Файл", Description = "Удалить" },
        new() { Action = "File.Search", Key = "F7", Modifiers = "Alt", Category = "Файл", Description = "Поиск файлов" },
        new() { Action = "Panel.DirectoryTreeLeft", Key = "F1", Modifiers = "Alt", Category = "Панель", Description = "Дерево каталогов (левая панель)" },
        new() { Action = "Panel.DirectoryTreeRight", Key = "F2", Modifiers = "Alt", Category = "Панель", Description = "Дерево каталогов (правая панель)" },
    };

    /// <summary>
    /// Возвращает Effective-хоткеи: пользовательские или по умолчанию.
    /// Returns effective hotkeys: user-customized or defaults.
    /// </summary>
    public static List<HotkeyItem> GetEffectiveHotkeys()
    {
        var loaded = Load().Hotkeys;
        if (loaded == null || loaded.Count == 0)
            return GetDefaultHotkeys();
        return loaded;
    }
}
