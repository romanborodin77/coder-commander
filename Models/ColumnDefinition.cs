using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CoderCommander.Services;

// ReSharper disable once RedundantUsingDirective — используется для enum значений в SortDirection

namespace CoderCommander.Models;

/// <summary>
/// Определение колонки в панели файлов: ключ, заголовок, ширина, видимость, порядок, сортировка.
/// Column definition for file panel: key, header, width, visibility, order, sort direction.
/// </summary>
public partial class ColumnDefinition : ObservableObject
{
    /// <summary>
    /// Уникальный ключ колонки (Name, Extension, Size, ModifiedDate, CreatedDate, Attributes, Type).
    /// Unique column key (Name, Extension, Size, ModifiedDate, CreatedDate, Attributes, Type).
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Отображаемый заголовок колонки (локализуемый).
    /// Display header for the column (localizable).
    /// </summary>
    [ObservableProperty]
    private string _header = string.Empty;

    /// <summary>
    /// Ширина колонки в пикселях.
    /// Column width in pixels.
    /// </summary>
    [ObservableProperty]
    private double _width;

    /// <summary>
    /// Видимость колонки.
    /// Column visibility.
    /// </summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>
    /// Порядок отображения колонки (DisplayIndex).
    /// Display order of the column.
    /// </summary>
    [ObservableProperty]
    private int _displayIndex;

    /// <summary>
    /// Является ли колонка обязательной (Name — всегда первая, не скрывается).
    /// Whether column is mandatory (Name is always first, cannot be hidden).
    /// </summary>
    public bool IsRequired { get; }

    /// <summary>
    /// Является ли колонка выравниваемой по правому краю (Size).
    /// Whether column is right-aligned (Size).
    /// </summary>
    public bool IsRightAligned { get; }

    /// <summary>
    /// Является ли колонка моноширинной (Size, Attributes).
    /// Whether column uses monospace font (Size, Attributes).
    /// </summary>
    public bool IsMonospace { get; }

    /// <summary>
    /// Направление сортировки: None (не сортировать), Ascending, Descending.
    /// Sort direction: None (no sort), Ascending, Descending.
    /// </summary>
    [ObservableProperty]
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    /// <summary>
    /// Создаёт экземпляр ColumnDefinition.
    /// Creates ColumnDefinition instance.
    /// </summary>
    /// <param name="key">Уникальный ключ / Unique key.</param>
    /// <param name="header">Заголовок / Header text.</param>
    /// <param name="width">Ширина по умолчанию / Default width.</param>
    /// <param name="isRequired">Обязательная колонка / Required column.</param>
    /// <param name="isRightAligned">Выравнивание по правому краю / Right-aligned.</param>
    /// <param name="isMonospace">Моноширинный шрифт / Monospace font.</param>
    public ColumnDefinition(
        string key,
        string header,
        double width,
        bool isRequired = false,
        bool isRightAligned = false,
        bool isMonospace = false)
    {
        Key = key;
        _header = header;
        _width = width;
        IsRequired = isRequired;
        IsRightAligned = isRightAligned;
        IsMonospace = isMonospace;
    }

    /// <summary>
    /// Возвращает список всех доступных колонок с локализованными заголовками.
    /// Returns all available columns with localized headers.
    /// </summary>
    public static List<ColumnDefinition> AllColumns => new()
    {
        new ColumnDefinition("Name", LocalizationService.Current.GetString("Columns.Name"), 200, isRequired: true),
        new ColumnDefinition("Extension", LocalizationService.Current.GetString("Columns.Extension"), 80),
        new ColumnDefinition("Size", LocalizationService.Current.GetString("Columns.Size"), 90, isRightAligned: true, isMonospace: true),
        new ColumnDefinition("ModifiedDate", LocalizationService.Current.GetString("Columns.Modified"), 130, isMonospace: true),
        new ColumnDefinition("CreatedDate", LocalizationService.Current.GetString("Columns.Created"), 130, isMonospace: true),
        new ColumnDefinition("Attributes", LocalizationService.Current.GetString("Columns.Attributes"), 80, isMonospace: true),
        new ColumnDefinition("Type", LocalizationService.Current.GetString("Columns.Type"), 80)
    };

    /// <summary>
    /// Создаёт глубокую копию определения колонки.
    /// Creates a deep copy of the column definition.
    /// </summary>
    public ColumnDefinition Clone() => new(Key, Header, Width, IsRequired, IsRightAligned, IsMonospace)
    {
        IsVisible = IsVisible,
        DisplayIndex = DisplayIndex,
        SortDirection = SortDirection
    };

    public override string ToString() => Header;

    /// <summary>
    /// Возвращает имя свойства FileSystemItem для сортировки по ключу колонки.
    /// Returns the FileSystemItem property name for sorting by this column key.
    /// </summary>
    public string GetSortPropertyName() => Key switch
    {
        "Name" => nameof(FileSystemItem.Name),
        "Extension" => nameof(FileSystemItem.Extension),
        "Size" => nameof(FileSystemItem.Size),
        "ModifiedDate" => nameof(FileSystemItem.Modified),
        "CreatedDate" => nameof(FileSystemItem.CreatedDate),
        "Attributes" => nameof(FileSystemItem.Attributes),
        "Type" => nameof(FileSystemItem.Extension),
        _ => nameof(FileSystemItem.Name)
    };
}
