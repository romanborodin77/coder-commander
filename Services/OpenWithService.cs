using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// Сервис «Открыть как»: поиск ассоциированных приложений, запуск файлов,
/// хранение недавно использованных приложений.
/// "Open With" service: finding associated applications, launching files,
/// storing recently used applications.
/// </summary>
public static class OpenWithService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoderCommander");
    private static readonly string RecentAppsPath = Path.Combine(SettingsDir, "recent_apps.json");

    private const int MaxRecentApps = 10;

    /// <summary>
    /// Список недавно использованных приложений (полные пути к .exe).
    /// List of recently used applications (full paths to .exe).
    /// </summary>
    private static List<string> _recentApps = LoadRecentApps();

    /// <summary>
    /// Получает список ассоциированных приложений для указанного типа файла
    /// через реестр Windows (HKCR) и добавляет недавно использованные приложения.
    /// Gets a list of associated applications for the specified file type
    /// via Windows registry (HKCR) and appends recently used applications.
    /// </summary>
    /// <param name="filePath">Путь к файлу. / Path to the file.</param>
    /// <returns>Список доступных приложений. / List of available applications.</returns>
    public static List<OpenWithApp> GetAssociatedApps(string filePath)
    {
        var apps = new List<OpenWithApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) return apps;

            // 1. Определяем ProgId через HKCR\<ext>
            // 1. Determine ProgId via HKCR\<ext>
            using (var extKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(ext))
            {
                var progId = extKey?.GetValue(string.Empty)?.ToString();
                if (!string.IsNullOrEmpty(progId))
                {
                    // 2. Ищем shell\open\command для данного ProgId
                    // 2. Find shell\open\command for this ProgId
                    var cmdPath = $@"{progId}\shell\open\command";
                    using var cmdKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(cmdPath);
                    var cmd = cmdKey?.GetValue(string.Empty)?.ToString();

                    if (!string.IsNullOrEmpty(cmd))
                    {
                        var exePath = ExtractExePath(cmd);
                        if (!string.IsNullOrEmpty(exePath) && !seen.Contains(exePath))
                        {
                            seen.Add(exePath);
                            apps.Add(CreateApp(exePath));
                        }
                    }

                    // 3. Ищем все shell\<verb>\command для данного ProgId
                    // 3. Find all shell\<verb>\command for this ProgId
                    using var shellKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{progId}\shell");
                    if (shellKey != null)
                    {
                        foreach (var verbName in shellKey.GetSubKeyNames())
                        {
                            if (verbName.Equals("open", StringComparison.OrdinalIgnoreCase))
                                continue; // уже обработали

                            using var verbKey = shellKey.OpenSubKey($@"{verbName}\command");
                            var verbCmd = verbKey?.GetValue(string.Empty)?.ToString();
                            if (!string.IsNullOrEmpty(verbCmd))
                            {
                                var verbExe = ExtractExePath(verbCmd);
                                if (!string.IsNullOrEmpty(verbExe) && !seen.Contains(verbExe))
                                {
                                    seen.Add(verbExe);
                                    apps.Add(CreateApp(verbExe));
                                }
                            }
                        }
                    }

                    // 4. Проверяем openwithlist/openwithprogids для дополнительных приложений
                    // 4. Check openwithlist/openwithprogids for additional apps
                    using var openWithKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{progId}\OpenWithList");
                    if (openWithKey != null)
                    {
                        foreach (var subName in openWithKey.GetSubKeyNames())
                        {
                            if (subName.Length <= 4 && subName.Contains('.'))
                            {
                                // Это расширение, а не имя приложения
                                // This is an extension, not an app name
                                using var assocExt = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{subName}\shell\open\command");
                                var assocCmd = assocExt?.GetValue(string.Empty)?.ToString();
                                if (!string.IsNullOrEmpty(assocCmd))
                                {
                                    var assocExe = ExtractExePath(assocCmd);
                                    if (!string.IsNullOrEmpty(assocExe) && !seen.Contains(assocExe))
                                    {
                                        seen.Add(assocExe);
                                        apps.Add(CreateApp(assocExe));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 5. Добавляем стандартные текстовые редакторы, если файл текстовый
            // 5. Add standard text editors if the file is text
            if (FileService.IsTextFile(filePath))
            {
                TryAddCommonEditor("notepad.exe", "Блокнот / Notepad", seen, apps);
                TryAddCommonEditor("notepad++.exe", "Notepad++", seen, apps);
                TryAddCommonEditor("code.exe", "Visual Studio Code", seen, apps);
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to get associated apps for {filePath}", nameof(OpenWithService), ex);
        }

        // 6. Добавляем недавно использованные приложения в начало
        // 6. Add recently used applications to the top
        var recentApps = _recentApps
            .Where(p => !seen.Contains(p) && File.Exists(p))
            .Take(MaxRecentApps)
            .Select(CreateApp)
            .ToList();

        recentApps.Reverse(); // Последние использованные — первыми
        var result = recentApps.Concat(apps).ToList();

        return result;
    }

    /// <summary>
    /// Открывает файл указанным приложением.
    /// Opens the file with the specified application.
    /// </summary>
    /// <param name="filePath">Путь к файлу. / Path to the file.</param>
    /// <param name="appPath">Путь к приложению (.exe). / Path to the application (.exe).</param>
    public static void OpenFile(string filePath, string appPath)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = appPath,
                Arguments = $"\"{filePath}\"",
                UseShellExecute = false
            });
            // Не блокируем UI — запускаем fire-and-forget.
            // Do not block UI — fire-and-forget.
            AddRecentApp(appPath);
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to open {filePath} with {appPath}", nameof(OpenWithService), ex);
            // Fallback: shell execute
            try
            {
                using var fallback = Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                // Не блокируем UI — fire-and-forget.
                // Do not block UI — fire-and-forget.
            }
            catch
            {
                // ignore secondary failure
            }
        }
    }

    /// <summary>
    /// Добавляет приложение в список недавно использованных.
    /// Adds an application to the recently used list.
    /// </summary>
    /// <param name="appPath">Путь к .exe. / Path to .exe.</param>
    public static void AddRecentApp(string appPath)
    {
        if (string.IsNullOrWhiteSpace(appPath)) return;

        _recentApps.Remove(appPath); // убираем дубликат
        _recentApps.Add(appPath);

        // Ограничиваем размер списка
        if (_recentApps.Count > MaxRecentApps)
            _recentApps = _recentApps[^MaxRecentApps..];

        SaveRecentApps();
    }

    /// <summary>
    /// Извлекает путь к .exe из командной строки реестра (например, "\"C:\...\app.exe\" \"%1\"").
    /// Extracts the .exe path from a registry command string.
    /// </summary>
    private static string ExtractExePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;

        command = command.Trim();

        // Если команда начинается с кавычки — берём содержимое до закрывающей кавычки
        // If command starts with a quote — take content up to the closing quote
        if (command.StartsWith('"'))
        {
            var endQuote = command.IndexOf('"', 1);
            if (endQuote > 1)
                return command[1..endQuote];
        }

        // Иначе — первое слово до пробела (с учётом %1, %L1 и т.п.)
        // Otherwise — first word before space (accounting for %1, %L1, etc.)
        var spaceIdx = command.IndexOf(' ');
        return spaceIdx > 0 ? command[..spaceIdx] : command;
    }

    /// <summary>
    /// Создаёт экземпляр OpenWithApp, извлекая иконку из .exe.
    /// Creates an OpenWithApp instance, extracting the icon from the .exe.
    /// </summary>
    private static OpenWithApp CreateApp(string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath);
        ImageSource? icon = null;

        try
        {
            if (File.Exists(exePath))
            {
                using var iconReader = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (iconReader != null)
                {
                    icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        iconReader.Handle,
                        System.Windows.Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
            }
        }
        catch
        {
            // Иконка не извлечена — допустимо
            // Icon extraction failed — acceptable
        }

        return new OpenWithApp { Name = name, Path = exePath, Icon = icon };
    }

    /// <summary>
    /// Пытается добавить стандартный редактор, если он существует в PATH.
    /// Tries to add a standard editor if it exists in PATH.
    /// </summary>
    private static void TryAddCommonEditor(string exeName, string displayName,
        HashSet<string> seen, List<OpenWithApp> apps)
    {
        // Ищем в Program Files, Program Files (x86), LOCALAPPDATA
        // Search in Program Files, Program Files (x86), LOCALAPPDATA
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), exeName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), exeName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", exeName),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate) && !seen.Contains(candidate))
            {
                seen.Add(candidate);
                apps.Add(new OpenWithApp
                {
                    Name = displayName,
                    Path = candidate,
                    Icon = null // Иконка будет извлечена при необходимости
                });
                return;
            }
        }
    }

    /// <summary>
    /// Загружает список недавно использованных приложений из JSON-файла.
    /// Loads the recently used applications list from a JSON file.
    /// </summary>
    private static List<string> LoadRecentApps()
    {
        try
        {
            if (File.Exists(RecentAppsPath))
            {
                var json = File.ReadAllText(RecentAppsPath);
                return JsonSerializer.Deserialize<List<string>>(json) ?? [];
            }
        }
        catch { }
        return [];
    }

    /// <summary>
    /// Сохраняет список недавно использованных приложений в JSON-файл.
    /// Saves the recently used applications list to a JSON file.
    /// </summary>
    private static void SaveRecentApps()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(_recentApps, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(RecentAppsPath, json);
        }
        catch { }
    }
}
