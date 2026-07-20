using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// Сервис управления конфигурацией колонок панели файлов: загрузка/сохранение, набор по умолчанию.
/// Service managing file panel column configuration: load/save, default set.
/// </summary>
public static class ColumnConfigService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoderCommander");
    private static readonly string ConfigPath = Path.Combine(SettingsDir, "columns.json");

    /// <summary>
    /// Текущий активный набор колонок (shared между панелями).
    /// Current active column set (shared between panels).
    /// </summary>
    public static ObservableCollection<ColumnDefinition> ActiveColumns { get; } = new();

    /// <summary>
    /// Событие, вызываемое при изменении набора колонок.
    /// Event fired when column set changes.
    /// </summary>
    public static event EventHandler? ColumnsChanged;

    /// <summary>
    /// Вызывает событие изменения колонок (для внешних вызовов).
    /// Raises columns changed event (for external callers).
    /// </summary>
    public static void RaiseColumnsChanged() => ColumnsChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>
    /// Возвращает набор колонок по умолчанию: Name(обяз.), Size, ModifiedDate.
    /// Returns default column set: Name (required), Size, ModifiedDate.
    /// </summary>
    public static List<ColumnDefinition> DefaultColumns()
    {
        return new List<ColumnDefinition>
        {
            ColumnDefinition.AllColumns[0].Clone(), // Name
            ColumnDefinition.AllColumns[2].Clone(), // Size
            ColumnDefinition.AllColumns[3].Clone(), // ModifiedDate
        };
    }

    /// <summary>
    /// Загружает конфигурацию колонок из JSON-файла или создаёт набор по умолчанию.
    /// Loads column configuration from JSON file or creates default set.
    /// </summary>
    public static void Load()
    {
        ActiveColumns.Clear();

        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var saved = JsonSerializer.Deserialize<List<ColumnDto>>(json);
                if (saved is { Count: > 0 })
                {
                    foreach (var dto in saved)
                    {
                        var template = ColumnDefinition.AllColumns.FirstOrDefault(c => c.Key == dto.Key);
                        var col = template is not null
                            ? template.Clone()
                            : new ColumnDefinition(dto.Key, dto.Key, 80);
                        col.Header = dto.Header ?? col.Header;
                        col.Width = dto.Width > 0 ? dto.Width : col.Width;
                        col.IsVisible = dto.IsVisible;
                        col.DisplayIndex = dto.DisplayIndex;
                        ActiveColumns.Add(col);
                    }

                    // Garantirium: Name всегда присутствует и первый
                    EnsureNameColumn();
                    ColumnsChanged?.Invoke(null, EventArgs.Empty);
                    return;
                }
            }
        }
        catch
        {
            // При ошибке — набор по умолчанию
        }

        ResetToDefault();
    }

    /// <summary>
    /// Сохраняет текущую конфигурацию колонок в JSON-файл.
    /// Saves current column configuration to JSON file.
    /// </summary>
    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var dtos = ActiveColumns.Select((c, i) => new ColumnDto
            {
                Key = c.Key,
                Header = c.Header,
                Width = c.Width,
                IsVisible = c.IsVisible,
                DisplayIndex = i
            }).ToList();
            var json = JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Тихо игнорируем ошибки записи
        }
    }

    /// <summary>
    /// Сбрасывает набор колонок к умолчанию и сохраняет.
    /// Resets column set to default and saves.
    /// </summary>
    public static void ResetToDefault()
    {
        ActiveColumns.Clear();
        foreach (var col in DefaultColumns())
            ActiveColumns.Add(col);
        Save();
        ColumnsChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Скрывает указанную колонку (кроме Name).
    /// Hides specified column (except Name).
    /// </summary>
    public static void HideColumn(string key)
    {
        var col = ActiveColumns.FirstOrDefault(c => c.Key == key);
        if (col is not null && !col.IsRequired)
        {
            col.IsVisible = false;
            Save();
            ColumnsChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Гарантирует наличие обязательной колонки Name на первой позиции.
    /// Ensures mandatory Name column exists at first position.
    /// </summary>
    private static void EnsureNameColumn()
    {
        if (!ActiveColumns.Any(c => c.Key == "Name"))
        {
            var nameCol = ColumnDefinition.AllColumns[0].Clone();
            ActiveColumns.Insert(0, nameCol);
        }
        else
        {
            var nameCol = ActiveColumns.First(c => c.Key == "Name");
            nameCol.IsVisible = true;
            var idx = ActiveColumns.IndexOf(nameCol);
            if (idx > 0)
            {
                ActiveColumns.RemoveAt(idx);
                ActiveColumns.Insert(0, nameCol);
            }
        }
    }

    /// <summary>
    /// DTO для JSON-сериализации колонки.
    /// DTO for JSON serialization of a column.
    /// </summary>
    private class ColumnDto
    {
        public string Key { get; set; } = "";
        public string? Header { get; set; }
        public double Width { get; set; }
        public bool IsVisible { get; set; } = true;
        public int DisplayIndex { get; set; }
    }
}
