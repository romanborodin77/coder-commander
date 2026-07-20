using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.Converters;

/// <summary>
/// Конвертирует булево значение в глиф-иконку папки или файла из Segoe MDL2 Assets.
/// Converts a boolean value to a folder or file glyph icon from Segoe MDL2 Assets.
/// </summary>
public class BoolToIconConverter : IValueConverter
{
    /// <summary>
    /// Возвращает иконку папки (true) или файла (false).
    /// Returns folder (true) or file (false) icon.
    /// </summary>
    /// <param name="v">Булево значение — true для папки, false для файла / Boolean value — true for folder, false for file.</param>
    /// <param name="t">Тип целевого свойства (не используется) / Target property type (unused).</param>
    /// <param name="p">Параметр конвертера (не используется) / Converter parameter (unused).</param>
    /// <param name="c">Сведения о культуре (не используются) / Culture info (unused).</param>
    /// <returns>Символ-глиф папки "\uE8B7" или файла "\uE8A5" / Folder "\uE8B7" or file "\uE8A5" glyph character.</returns>
    public object Convert(object v, Type t, object p, CultureInfo c) => (bool)v ? "\uE8B7" : "\uE8A5"; // папка (Folder) / файл (Document)

    /// <summary>
    /// Обратное преобразование не поддерживается.
    /// Reverse conversion is not supported.
    /// </summary>
    /// <exception cref="NotSupportedException">Всегда выбрасывается / Always thrown.</exception>
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует DriveType в глиф-иконку диска из Segoe MDL2 Assets.
/// Converts DriveType to a drive glyph icon from Segoe MDL2 Assets.
/// </summary>
public class DriveTypeToIconConverter : IValueConverter
{
    /// <summary>
    /// Возвращает иконку в зависимости от типа диска.
    /// Returns an icon based on the drive type.
    /// </summary>
    /// <param name="v">Значение DriveType / DriveType value.</param>
    /// <param name="t">Тип целевого свойства (не используется) / Target property type (unused).</param>
    /// <param name="p">Параметр конвертера (не используется) / Converter parameter (unused).</param>
    /// <param name="c">Сведения о культуре (не используются) / Culture info (unused).</param>
    /// <returns>Символ-глиф диска из набора Segoe MDL2 Assets / Drive glyph character from Segoe MDL2 Assets.</returns>
    public object Convert(object v, Type t, object p, CultureInfo c) => v switch
    {
        DriveType.Removable => "\uE88E", // E88E USB — съёмный/USB-накопитель
        DriveType.Network => "\uE8CE",   // E8CE MapDrive — сетевой диск
        DriveType.CDRom => "\uE958",     // E958 StorageOptical — оптический (CD/DVD)
        _ => "\uEDA2"                     // EDA2 HardDrive — Fixed/SSD/Ram/Unknown
    };

    /// <summary>
    /// Обратное преобразование не поддерживается.
    /// Reverse conversion is not supported.
    /// </summary>
    /// <exception cref="NotSupportedException">Всегда выбрасывается / Always thrown.</exception>
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует булево значение в цвет кисти для иконки: папка — акцентный цвет, файл — основной текст.
/// Converts a boolean to a brush color for the icon: folder uses accent color, file uses primary text color.
/// </summary>
public class DirectoryColorConverter : IValueConverter
{
    /// <summary>
    /// Возвращает акцентную кисть для папки (true) или кисть основного текста для файла (false).
    /// Returns accent brush for folder (true) or foreground brush for file (false).
    /// </summary>
    /// <param name="v">Булево значение — true для папки, false для файла / Boolean — true for folder, false for file.</param>
    /// <param name="t">Тип целевого свойства (не используется) / Target property type (unused).</param>
    /// <param name="p">Параметр конвертера (не используется) / Converter parameter (unused).</param>
    /// <param name="c">Сведения о культуре (не используются) / Culture info (unused).</param>
    /// <returns>SolidColorBrush с акцентным цветом для папок или цветом текста для файлов / SolidColorBrush with accent color for folders or text color for files.</returns>
    public object Convert(object v, Type t, object p, CultureInfo c) => (bool)v
        ? (SolidColorBrush)(Application.Current.Resources["AccentBrush"]
            ?? new SolidColorBrush(Color.FromRgb(122, 139, 250)))
        : (SolidColorBrush)(Application.Current.Resources["FgLightBrush"]
            ?? new SolidColorBrush(Colors.White));

    /// <summary>
    /// Обратное преобразование не поддерживается.
    /// Reverse conversion is not supported.
    /// </summary>
    /// <exception cref="NotSupportedException">Всегда выбрасывается / Always thrown.</exception>
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует булево значение в Visibility: true → Visible, false → Collapsed.
/// Converts a boolean to Visibility: true → Visible, false → Collapsed.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Возвращает Visible (true) или Collapsed (false).
    /// Returns Visible (true) or Collapsed (false).
    /// </summary>
    /// <param name="v">Булево значение / Boolean value.</param>
    /// <param name="t">Тип целевого свойства (не используется) / Target property type (unused).</param>
    /// <param name="p">Параметр конвертера (не используется) / Converter parameter (unused).</param>
    /// <param name="c">Сведения о культуре (не используются) / Culture info (unused).</param>
    /// <returns>Visibility.Visible или Visibility.Collapsed / Visibility.Visible or Visibility.Collapsed.</returns>
    public object Convert(object v, Type t, object p, CultureInfo c) => (bool)v ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Обратное преобразование не поддерживается.
    /// Reverse conversion is not supported.
    /// </summary>
    /// <exception cref="NotSupportedException">Всегда выбрасывается / Always thrown.</exception>
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует булево значение в Visibility с инверсией: true → Collapsed, false → Visible.
/// Converts a boolean to Visibility inverted: true → Collapsed, false → Visible.
/// </summary>
public class BoolToVisibilityInverseConverter : IValueConverter
{
    /// <summary>
    /// Возвращает Collapsed (true) или Visible (false).
    /// Returns Collapsed (true) or Visible (false).
    /// </summary>
    /// <param name="v">Булево значение / Boolean value.</param>
    /// <param name="t">Тип целевого свойства (не используется) / Target property type (unused).</param>
    /// <param name="p">Параметр конвертера (не используется) / Converter parameter (unused).</param>
    /// <param name="c">Сведения о культуре (не используются) / Culture info (unused).</param>
    /// <returns>Visibility.Collapsed или Visibility.Visible / Visibility.Collapsed or Visibility.Visible.</returns>
    public object Convert(object v, Type t, object p, CultureInfo c) => (bool)v ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Обратное преобразование не поддерживается.
    /// Reverse conversion is not supported.
    /// </summary>
    /// <exception cref="NotSupportedException">Всегда выбрасывается / Always thrown.</exception>
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Инвертирует булево значение: true → false, false → true.
/// Inverts a boolean value: true → false, false → true.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is bool b ? !b : v;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => v is bool b ? !b : v;
}

/// <summary>
/// Конвертирует GitState в цвет кисти для отображения статуса файла.
/// Converts GitState to a brush color for displaying file status.
/// </summary>
public class GitStateToBrushConverter : IValueConverter
{
    /// <summary>
    /// Возвращает цвет кисти в зависимости от состояния Git.
    /// Returns a brush color based on the Git state.
    /// </summary>
    /// <param name="v">Значение GitState / GitState value.</param>
    /// <param name="t">Тип целевого свойства (не используется) / Target property type (unused).</param>
    /// <param name="p">Параметр конвертера (не используется) / Converter parameter (unused).</param>
    /// <param name="c">Сведения о культуре (не используются) / Culture info (unused).</param>
    /// <returns>SolidColorBrush с цветом, соответствующим статусу Git / SolidColorBrush with the color matching the Git status.</returns>
    public object Convert(object v, Type t, object p, CultureInfo c) => (GitState)v switch
    {
        GitState.Modified => new SolidColorBrush(Color.FromRgb(230, 170, 0)),
        GitState.Added => new SolidColorBrush(Color.FromRgb(78, 154, 6)),
        GitState.Deleted => new SolidColorBrush(Color.FromRgb(200, 40, 40)),
        GitState.Untracked => new SolidColorBrush(Color.FromRgb(120, 120, 120)),
        GitState.Conflicted => new SolidColorBrush(Color.FromRgb(220, 60, 60)),
        _ => Application.Current.Resources["FgLightBrush"]
    };

    /// <summary>
    /// Обратное преобразование не поддерживается.
    /// Reverse conversion is not supported.
    /// </summary>
    /// <exception cref="NotSupportedException">Всегда выбрасывается / Always thrown.</exception>
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Сравнивает строковое значение с параметром конвертера без учёта регистра. Возвращает true, если равны.
/// Compares a string value with the converter parameter case-insensitively. Returns true if equal.
/// </summary>
public class StrEqConverter : IValueConverter
{
    /// <summary>
    /// Сравнивает значение с параметром без учёта регистра.
    /// Compares the value with the parameter case-insensitively.
    /// </summary>
    /// <param name="v">Строковое значение / String value.</param>
    /// <param name="t">Тип целевого свойства (не используется) / Target property type (unused).</param>
    /// <param name="p">Строка для сравнения / String to compare against.</param>
    /// <param name="c">Сведения о культуре (не используются) / Culture info (unused).</param>
    /// <returns>true, если строки равны без учёта регистра; иначе false / true if strings are equal case-insensitively; otherwise false.</returns>
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is string s && p is string par && s.Equals(par, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Обратное преобразование: возвращает параметр, если значение true; иначе Binding.DoNothing.
    /// Reverse conversion: returns the parameter if value is true; otherwise Binding.DoNothing.
    /// </summary>
    /// <param name="v">Булево значение / Boolean value.</param>
    /// <param name="t">Тип целевого свойства (не используется) / Target property type (unused).</param>
    /// <param name="p">Строка, которая будет возвращена при значении true / String to return when value is true.</param>
    /// <param name="c">Сведения о культуре (не используются) / Culture info (unused).</param>
    /// <returns>Параметр при true; Binding.DoNothing при false / Parameter when true; Binding.DoNothing when false.</returns>
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
    {
        if (v is bool b && b && p is string par) return par;
        return Binding.DoNothing;
    }
}

/// <summary>
/// Преобразует строку пути в список сегментов для отображения Breadcrumb (навигационной цепочки).
/// Converts a path string into a list of segments for breadcrumb display.
/// </summary>
public class PathToBreadcrumbsConverter : IValueConverter
{
    /// <summary>
    /// Разделяет путь на компоненты по обратной косой черте.
    /// Splits the path into components by backslash.
    /// </summary>
    /// <param name="v">Строка пути / Path string.</param>
    /// <param name="t">Тип целевого свойства (не используется) / Target property type (unused).</param>
    /// <param name="p">Параметр конвертера (не используется) / Converter parameter (unused).</param>
    /// <param name="c">Сведения о культуре (не используются) / Culture info (unused).</param>
    /// <returns>Список сегментов пути / List of path segments.</returns>
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is string path && !string.IsNullOrEmpty(path))
        {
            var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? new List<string>() : new List<string>(parts);
        }
        return new List<string>();
    }

    /// <summary>
    /// Обратное преобразование не поддерживается.
    /// Reverse conversion is not supported.
    /// </summary>
    /// <exception cref="NotSupportedException">Всегда выбрасывается / Always thrown.</exception>
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Преобразует размер в байтах в человекочитаемый формат: 1.5 КБ, 2.3 МБ и т.д.
/// Converts a byte size into a human-readable format: 1.5 KB, 2.3 MB, etc.
/// </summary>
public class FileSizeConverter : IValueConverter
{
    /// <summary>
    /// Единицы измерения размера: байты, килобайты, мегабайты, гигабайты, терабайты.
    /// Size units: bytes, kilobytes, megabytes, gigabytes, terabytes.
    /// </summary>
    private static readonly string[] SizeUnits = { "Б", "КБ", "МБ", "ГБ", "ТБ" };

    /// <summary>
    /// Преобразует числовое значение (long) в строку с единицей измерения.
    /// Converts a numeric value (long) into a string with a unit of measurement.
    /// </summary>
    /// <param name="value">Размер в байтах / Size in bytes.</param>
    /// <param name="targetType">Тип целевого свойства (не используется) / Target property type (unused).</param>
    /// <param name="parameter">Параметр конвертера (не используется) / Converter parameter (unused).</param>
    /// <param name="culture">Сведения о культуре (не используются) / Culture info (unused).</param>
    /// <returns>Строка вида "1.5 КБ" или "—" при некорректном значении / String like "1.5 КБ" or "—" on invalid value.</returns>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes) return "—";
        if (bytes == 0) return "0 Б";
        int unitIndex = 0;
        double size = bytes;
        while (size >= 1024 && unitIndex < SizeUnits.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return unitIndex == 0
            ? $"{size:F0} {SizeUnits[unitIndex]}"
            : $"{size:F1} {SizeUnits[unitIndex]}";
    }

    /// <summary>
    /// Обратное преобразование не поддерживается.
    /// Reverse conversion is not supported.
    /// </summary>
    /// <exception cref="NotSupportedException">Всегда выбрасывается / Always thrown.</exception>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Считает количество файлов и папок в коллекции FileSystemItem (исключая элемент "..").
/// Counts files and directories in a FileSystemItem collection (excluding the ".." entry).
/// </summary>
public class ItemsCountConverter : IValueConverter
{
    /// <summary>
    /// Подсчитывает файлы и папки в перечисляемой коллекции.
    /// Counts files and folders in an enumerable collection.
    /// </summary>
    /// <param name="v">Коллекция элементов FileSystemItem / Collection of FileSystemItem.</param>
    /// <param name="t">Тип целевого свойства (не используется) / Target property type (unused).</param>
    /// <param name="p">Параметр конвертера (не используется) / Converter parameter (unused).</param>
    /// <param name="c">Сведения о культуре (не используются) / Culture info (unused).</param>
    /// <returns>Строка вида "5 файлов, 3 папок" / String like "5 файлов, 3 папок".</returns>
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is System.Collections.IEnumerable items)
        {
            int files = 0, dirs = 0;
            foreach (var item in items)
            {
                if (item is FileSystemItem fsi && !fsi.IsParent)
                {
                    if (fsi.IsDirectory) dirs++;
                    else files++;
                }
            }
            return string.Format(Services.LocalizationService.Current.GetString("Status.FilesDirs"), files, dirs);
        }
        return string.Format(Services.LocalizationService.Current.GetString("Status.FilesDirs"), 0, 0);
    }

    /// <summary>
    /// Обратное преобразование не поддерживается.
    /// Reverse conversion is not supported.
    /// </summary>
    /// <exception cref="NotSupportedException">Всегда выбрасывается / Always thrown.</exception>
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Возвращает Visible, если целочисленная длина больше 0; иначе Collapsed.
/// Returns Visible if the integer length is greater than 0; otherwise Collapsed.
/// </summary>
public class LengthToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Преобразует длину (int) в Visibility: >0 → Visible, иначе Collapsed.
    /// Converts length (int) to Visibility: >0 → Visible, otherwise Collapsed.
    /// </summary>
    /// <param name="v">Целочисленное значение длины / Integer length value.</param>
    /// <param name="t">Тип целевого свойства (не используется) / Target property type (unused).</param>
    /// <param name="p">Параметр конвертера (не используется) / Converter parameter (unused).</param>
    /// <param name="c">Сведения о культуре (не используются) / Culture info (unused).</param>
    /// <returns>Visibility.Visible при len > 0; иначе Visibility.Collapsed / Visibility.Visible if len > 0; otherwise Visibility.Collapsed.</returns>
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is int len && len > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Обратное преобразование не поддерживается.
    /// Reverse conversion is not supported.
    /// </summary>
    /// <exception cref="NotSupportedException">Всегда выбрасывается / Always thrown.</exception>
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Возвращает Visible, если целочисленная длина равна 0; иначе Collapsed (инверсия LengthToVisibilityConverter).
/// Returns Visible if the integer length is 0; otherwise Collapsed (inverse of LengthToVisibilityConverter).
/// </summary>
public class LengthToVisibilityInverseConverter : IValueConverter
{
    /// <summary>
    /// Преобразует длину (int) в Visibility: ==0 → Visible, иначе Collapsed.
    /// Converts length (int) to Visibility: ==0 → Visible, otherwise Collapsed.
    /// </summary>
    /// <param name="v">Целочисленное значение длины / Integer length value.</param>
    /// <param name="t">Тип целевого свойства (не используется) / Target property type (unused).</param>
    /// <param name="p">Параметр конвертера (не используется) / Converter parameter (unused).</param>
    /// <param name="c">Сведения о культуре (не используются) / Culture info (unused).</param>
    /// <returns>Visibility.Visible при len == 0; иначе Visibility.Collapsed / Visibility.Visible if len == 0; otherwise Visibility.Collapsed.</returns>
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is int len && len == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Обратное преобразование не поддерживается.
    /// Reverse conversion is not supported.
    /// </summary>
    /// <exception cref="NotSupportedException">Всегда выбрасывается / Always thrown.</exception>
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует пустую строку в Visibility.Collapsed, непустую — в Visible.
/// Converts an empty string to Collapsed, non-empty to Visible.
/// </summary>
public class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует статус операции в очереди в цвет для UI.
/// Converts queued operation status to a brush color for UI.
/// Running = Accent, Completed = Ok, Failed = Err, Cancelled = Warning, Queued = Dim.
/// </summary>
public class QueueStatusToColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is Models.QueuedOperationStatus status)
        {
            return status switch
            {
                Models.QueuedOperationStatus.Running => Application.Current.TryFindResource("AccentBrush") ?? Brushes.DodgerBlue,
                Models.QueuedOperationStatus.Completed => Application.Current.TryFindResource("OkBrush") ?? Brushes.LimeGreen,
                Models.QueuedOperationStatus.Failed => Application.Current.TryFindResource("ErrBrush") ?? Brushes.OrangeRed,
                Models.QueuedOperationStatus.Cancelled => Brushes.Gold,
                _ => Application.Current.TryFindResource("FgDimBrush") ?? Brushes.Gray,
            };
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует статус операции в очереди в локализованную строку.
/// Converts queued operation status to a localized string.
/// </summary>
public class QueueStatusToTextConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is Models.QueuedOperationStatus status)
        {
            var L = Services.LocalizationService.Current;
            return status switch
            {
                Models.QueuedOperationStatus.Running => L.GetString("OpQueue.Status.Running"),
                Models.QueuedOperationStatus.Completed => L.GetString("OpQueue.Status.Completed"),
                Models.QueuedOperationStatus.Failed => L.GetString("OpQueue.Status.Failed"),
                Models.QueuedOperationStatus.Cancelled => L.GetString("OpQueue.Status.Cancelled"),
                _ => L.GetString("OpQueue.Status.Queued"),
            };
        }
        return "";
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
