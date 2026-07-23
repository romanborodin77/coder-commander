using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// Сервис управления закладками (избранными папками). Загрузка/сохранение в settings.json.
/// Bookmarks management service. Loads/saves to settings.json.
/// </summary>
public sealed class BookmarkService
{
    /// <summary>Текущий экземпляр синглтона. / Singleton instance.</summary>
    public static BookmarkService Current { get; } = new();

    /// <summary>Коллекция закладок. / Bookmarks collection.</summary>
    public ObservableCollection<BookmarkItem> Bookmarks { get; } = new();

    /// <summary>Путь к файлу настроек. / Settings file path.</summary>
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoderCommander", "settings.json");

    // FIXED: SemaphoreSlim to prevent concurrent read-modify-write race in Save().
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    /// <summary>
    /// Добавляет закладку. Возвращает false, если путь уже существует.
    /// Adds a bookmark. Returns false if the path already exists.
    /// </summary>
    public bool Add(string name, string path)
    {
        // Проверка дубликата по пути (без учёта регистра).
        // Duplicate check by path (case-insensitive).
        foreach (var bm in Bookmarks)
        {
            if (string.Equals(bm.Path.TrimEnd('\\', '/'), path.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase))
                return false;
        }

        Bookmarks.Add(new BookmarkItem(name, path));
        Save();
        return true;
    }

    /// <summary>
    /// Удаляет закладку.
    /// Removes a bookmark.
    /// </summary>
    public void Remove(BookmarkItem bookmark)
    {
        if (Bookmarks.Remove(bookmark))
            Save();
    }

    /// <summary>
    /// Переименовывает закладку.
    /// Renames a bookmark.
    /// </summary>
    public void Rename(BookmarkItem bookmark, string newName)
    {
        bookmark.Name = newName;
        Save();
    }

    /// <summary>
    /// Перемещает закладку (drag-drop reorder).
    /// Moves a bookmark (drag-drop reorder).
    /// </summary>
    public void Reorder(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Bookmarks.Count ||
            newIndex < 0 || newIndex >= Bookmarks.Count ||
            oldIndex == newIndex)
            return;

        var item = Bookmarks[oldIndex];
        Bookmarks.RemoveAt(oldIndex);
        Bookmarks.Insert(newIndex, item);
        Save();
    }

    /// <summary>
    /// Сохраняет закладки в settings.json (с сохранением остальных полей).
    /// Saves bookmarks to settings.json (preserving other fields).
    /// </summary>
    // FIXED: Added SemaphoreSlim to prevent race conditions in concurrent read-modify-write.
    public void Save()
    {
        _saveLock.Wait();
        try
        {
            AppSettings? settings = null;

            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json);
            }

            settings ??= new AppSettings();
            settings.Bookmarks = new System.Collections.Generic.List<BookmarkItem>(Bookmarks);

            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var text = JsonSerializer.Serialize(settings, options);
            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, text);
            File.Move(tempPath, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            LogService.Error("Ошибка сохранения закладок: " + ex.Message, "Bookmarks", ex);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Загружает закладки из settings.json.
    /// Loads bookmarks from settings.json.
    /// </summary>
    public void Load()
    {
        List<BookmarkItem>? loaded = null;

        try
        {
            if (!File.Exists(SettingsPath)) return;

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);

            if (settings?.Bookmarks is { Count: > 0 })
                loaded = new List<BookmarkItem>(settings.Bookmarks);
        }
        catch (Exception ex)
        {
            LogService.Error("Ошибка загрузки закладок: " + ex.Message, "Bookmarks", ex);
        }

        if (loaded is null) return;
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            dispatcher.Invoke(() => ReplaceBookmarks(loaded));
        else
            ReplaceBookmarks(loaded);
    }

    private void ReplaceBookmarks(List<BookmarkItem> items)
    {
        Bookmarks.Clear();
        foreach (var bm in items)
            Bookmarks.Add(bm);
    }

    /// <summary>
    /// Экспорт закладок в JSON-файл. Формат: [{Name, Path, Icon, CreatedAt}].
    /// Exports bookmarks to a JSON file. Format: [{Name, Path, Icon, CreatedAt}].
    /// </summary>
    public async Task ExportAsync(string filePath)
    {
        try
        {
            List<BookmarkItem> snapshot;
            if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                snapshot = await dispatcher.InvokeAsync(() => new List<BookmarkItem>(Bookmarks));
            else
                snapshot = new List<BookmarkItem>(Bookmarks);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(snapshot, options);
            var tempPath = filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            LogService.Error("Ошибка экспорта закладок: " + ex.Message, "Bookmarks", ex);
            throw;
        }
    }

    /// <summary>
    /// Импорт закладок из JSON-файла с дедупликацией по Path.
    /// Imports bookmarks from a JSON file with deduplication by Path.
    /// Возвращает (added, skipped). / Returns (added, skipped).
    /// </summary>
    public async Task<(int Added, int Skipped)> ImportAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var imported = JsonSerializer.Deserialize<List<BookmarkItem>>(json);

        if (imported is not { Count: > 0 })
            return (0, 0);

        int added = 0, skipped = 0;

        try
        {
            Func<(int Added, int Skipped)> doImport = () =>
            {
                int a = 0, s = 0;
                foreach (var bm in imported)
                {
                    if (Add(bm.Name, bm.Path))
                        a++;
                    else
                        s++;
                }
                return (a, s);
            };

            if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                (added, skipped) = await dispatcher.InvokeAsync(doImport);
            else
                (added, skipped) = doImport();
        }
        catch (Exception ex)
        {
            LogService.Error("Ошибка импорта закладок: " + ex.Message, "Bookmarks", ex);
            throw;
        }

        return (added, skipped);
    }
}
