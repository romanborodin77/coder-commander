using System.Collections.ObjectModel;
using System.ComponentModel;
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
    /// Ключ колонки, по которой выполняется сортировка (null/пусто = нет сортировки).
    /// Column key for the current sort (null/empty = no sort).
    /// </summary>
    public static string? SortedColumnKey { get; set; }

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
        SortedColumnKey = null;

        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                // Пробуем новый формат (объект-контейнер) или старый (плоский массив) — обратная совместимость.
                if (TryLoadNewFormat(json)) return;
                if (TryLoadLegacyFormat(json)) return;
            }
        }
        catch
        {
            // При ошибке — набор по умолчанию
        }

        ResetToDefault();
    }

    private static bool TryLoadNewFormat(string json)
    {
        try
        {
            var container = JsonSerializer.Deserialize<ColumnConfigDto>(json);
            if (container?.Columns is { Count: > 0 })
            {
                foreach (var dto in container.Columns)
                {
                    var col = CreateColumnFromDto(dto);
                    ActiveColumns.Add(col);
                }
                SortedColumnKey = container.SortedColumnKey;
                EnsureNameColumn();
                ColumnsChanged?.Invoke(null, EventArgs.Empty);
                return true;
            }
        }
        catch
        {
            // Не новый формат — пробуем legacy
        }
        return false;
    }

    private static bool TryLoadLegacyFormat(string json)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<List<ColumnDto>>(json);
            if (saved is { Count: > 0 })
            {
                foreach (var dto in saved)
                {
                    var col = CreateColumnFromDto(dto);
                    ActiveColumns.Add(col);
                }
                EnsureNameColumn();
                ColumnsChanged?.Invoke(null, EventArgs.Empty);
                return true;
            }
        }
        catch
        {
            // Не legacy — ошибка
        }
        return false;
    }

    private static ColumnDefinition CreateColumnFromDto(ColumnDto dto)
    {
        var template = ColumnDefinition.AllColumns.FirstOrDefault(c => c.Key == dto.Key);
        var col = template is not null
            ? template.Clone()
            : new ColumnDefinition(dto.Key, dto.Key, 80);
        // Header is always resolved from localized AllColumns, not from saved JSON
        col.Width = dto.Width > 0 ? dto.Width : col.Width;
        col.IsVisible = dto.IsVisible;
        col.DisplayIndex = dto.DisplayIndex;
        col.SortDirection = dto.SortDirection;
        return col;
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
            var container = new ColumnConfigDto
            {
                SortedColumnKey = SortedColumnKey,
                Columns = ActiveColumns.Select((c, i) => new ColumnDto
                {
                    Key = c.Key,
                    Header = c.Header,
                    Width = c.Width,
                    IsVisible = c.IsVisible,
                    DisplayIndex = i,
                    SortDirection = c.SortDirection
                }).ToList()
            };
            var json = JsonSerializer.Serialize(container, new JsonSerializerOptions { WriteIndented = true });
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
        SortedColumnKey = null;
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
        public ListSortDirection SortDirection { get; set; } = ListSortDirection.Ascending;
    }

    /// <summary>
    /// DTO-контейнер для хранения конфигурации колонок вместе с ключом сортировки.
    /// Container DTO storing column configuration along with the sorting column key.
    /// </summary>
    private class ColumnConfigDto
    {
        public string? SortedColumnKey { get; set; }
        public List<ColumnDto> Columns { get; set; } = new();
    }
}
