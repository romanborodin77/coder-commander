using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// ????? ???? ??????????: ??????, ??????? ??? ?????????.
/// Theme mode: dark, light, or system.
/// </summary>
public enum ThemeMode
{
    /// <summary>?????? ????. / Dark theme.</summary>
    Dark,
    /// <summary>??????? ????. / Light theme.</summary>
    Light,
    /// <summary>????????? ???? (???????????? ?? ??????? Windows). / System theme (detected from Windows registry).</summary>
    System
}

/// <summary>
/// ?????? ???????? ??????????, ??????????? ? JSON.
/// Application settings model, persisted in JSON.
/// </summary>
public class AppSettings
{
    //???? ? ??????? ??? / Theme appearance

    /// <summary>
    /// ????? ???? ??????????. / Theme mode.
    /// </summary>
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    /// <summary>
    /// ??? ????? ?????????? (????????, "en", "ru"). / Interface language code (e.g., "en", "ru").
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// ?????? ???????? ????. / Main window width.
    /// </summary>
    public double WindowWidth { get; set; } = 1500;

    /// <summary>
    /// ?????? ???????? ????. / Main window height.
    /// </summary>
    public double WindowHeight { get; set; } = 850;

    /// <summary>
    /// ???? ?????? ????? ?????? (0..1). / Left panel width fraction (0..1).
    /// </summary>
    public double LeftPanelWidth { get; set; } = 0.5;

    /// <summary>
    /// ?????????? ??????? ?????. / Show hidden files.
    /// </summary>
    public bool ShowHidden { get; set; }

    /// <summary>
    /// ????????? ???????? ????. / Last opened path.
    /// </summary>
    public string LastPath { get; set; } = "";

    //???????? / Editor

    /// <summary>
    /// ????? ?????????. / Editor font family.
    /// </summary>
    public string EditorFontFamily { get; set; } = "Cascadia Code";

    /// <summary>
    /// ?????? ?????? ?????????. / Editor font size.
    /// </summary>
    public double EditorFontSize { get; set; } = 14;

    /// <summary>
    /// ?????????? ?????? ?????. / Show line numbers.
    /// </summary>
    public bool EditorShowLineNumbers { get; set; } = true;

    /// <summary>
    /// ??????? ????? ? ?????????. / Editor word wrap.
    /// </summary>
    public bool EditorWordWrap { get; set; }

    //???????? / Terminal

    /// <summary>
    /// ???????? ????????? (cmd, powershell). / Terminal shell (cmd, powershell).
    /// </summary>
    public string TerminalShell { get; set; } = "cmd";

    /// <summary>
    /// ?????? ?????? ????????? ? ????????. / Terminal panel height in pixels.
    /// </summary>
    public double TerminalPanelHeight { get; set; } = 300;

    /// <summary>
    /// ????? ?????????. / Terminal font family.
    /// </summary>
    public string TerminalFontFamily { get; set; } = "Consolas";

    /// <summary>
    /// ?????? ?????? ?????????. / Terminal font size.
    /// </summary>
    public double TerminalFontSize { get; set; } = 12;

    //????????? / Behavior

    /// <summary>
    /// ??????????? ????????????? ??? ????????. / Confirm delete.
    /// </summary>
    public bool ConfirmDelete { get; set; } = true;

    /// <summary>
    /// ??????????? ????????????? ??? ??????????. / Confirm overwrite.
    /// </summary>
    public bool ConfirmOverwrite { get; set; } = true;

    /// <summary>
    /// ??????????? ?????????????? ? ?????????????. / Auto-refresh panel contents.
    /// </summary>
    public bool AutoRefresh { get; set; } = true;

    /// <summary>
    /// ???????? ?????????????? ? ?????????????. / Auto-refresh interval in milliseconds.
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

    //???????? / Bookmarks

    /// <summary>
    /// ?????? ???????? (????????? ?????). / List of bookmarks (favorite directories).
    /// </summary>
    public List<BookmarkItem> Bookmarks { get; set; } = new();

    //???????? / Panel Tabs (ph5.9)

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
}

/// <summary>
/// ?????? ???????? ? ?????????? ???????? ?????????? ? JSON-????.
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
    /// ????????? ????????? ?? ????? (? ????????????) ??? ?????????? ????????? ?? ?????????.
    /// Loads settings from file (with caching) or returns default settings.
    /// </summary>
    /// <returns>??????????? ??? ????????? ?? ?????????. / Loaded or default settings.</returns>
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
            // ??? ?????? ?????? ? ????????? ?? ?????????.
            // On read error ? use default settings.
        }

        snapshot = new AppSettings();
        _current = snapshot;
        return snapshot;
    }

    /// <summary>
    /// ????????? ????????? ? JSON-????.
    /// Saves settings to JSON file.
    /// </summary>
    /// <param name="settings">?????? ? ???????????. / Settings object.</param>
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
            // ??? ?????? ?????? ? ????? ??????????.
            // On write error ? silently ignore.
        }
    }

    /// <summary>
    /// ?????????? ?????????? ????: ???? ??????? System, ?????????? ???? ?? ??????? Windows.
    /// Returns effective theme: if System is selected, detects theme from Windows registry.
    /// </summary>
    /// <returns>?????? ??? ??????? ????. / Dark or light theme.</returns>
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
            // Fallback ??? ?????? ???????.
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
