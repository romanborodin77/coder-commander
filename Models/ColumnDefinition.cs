using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoderCommander.Models;

/// <summary>
/// Определение колонки в панели файлов: ключ, заголовок, ширина, видимость, порядок.
/// Column definition for file panel: key, header, width, visibility, order.
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
    /// Список всех доступных колонок по умолчанию.
    /// List of all available columns by default.
    /// </summary>
    public static List<ColumnDefinition> AllColumns { get; } = new()
    {
        new ColumnDefinition("Name", "Имя", 200, isRequired: true),
        new ColumnDefinition("Extension", "Расширение", 80),
        new ColumnDefinition("Size", "Размер", 90, isRightAligned: true, isMonospace: true),
        new ColumnDefinition("ModifiedDate", "Дата изм.", 130, isMonospace: true),
        new ColumnDefinition("CreatedDate", "Дата созд.", 130, isMonospace: true),
        new ColumnDefinition("Attributes", "Атрибуты", 80, isMonospace: true),
        new ColumnDefinition("Type", "Тип", 80)
    };

    /// <summary>
    /// Создаёт глубокую копию определения колонки.
    /// Creates a deep copy of the column definition.
    /// </summary>
    public ColumnDefinition Clone() => new(Key, Header, Width, IsRequired, IsRightAligned, IsMonospace)
    {
        IsVisible = IsVisible,
        DisplayIndex = DisplayIndex
    };
}
