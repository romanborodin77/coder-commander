using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using CoderCommander.Services;

namespace CoderCommander.Converters;

/// <summary>
/// Элемент перечисления с локализованным отображаемым именем (для ComboBox).
/// An enum item with a localized display name (for ComboBox).
/// </summary>
public sealed class LocalizedEnumItem
{
    /// <summary>Значение перечисления. / Enum value.</summary>
    public Enum Value { get; }
    /// <summary>Отображаемое (локализованное) имя. / Display (localized) name.</summary>
    public string Display { get; }
    /// <summary>Создаёт элемент. / Creates the item.</summary>
    public LocalizedEnumItem(Enum value, string display) { Value = value; Display = display; }
    /// <summary>Возвращает локализованное имя (для ComboBox). / Returns the localized name (for ComboBox).</summary>
    public override string ToString() => Display;
}

/// <summary>
/// Преобразует значение перечисления в список <see cref="LocalizedEnumItem"/> для привязки к ComboBox.
/// Converts an enum value into a list of LocalizedEnumItem for ComboBox binding.
/// Параметр конвертера — префикс ключа локализации (например, "Sync.Mode"),
/// итоговый ключ: "&lt;префикс&gt;.&lt;ИмяЗначения&gt;".
/// Converter parameter is the localization key prefix (e.g. "Sync.Mode");
/// final key: "<prefix>.<EnumValueName>".
/// </summary>
public class EnumLocalizedConverter : IValueConverter
{
    /// <summary>Возвращает список элементов перечисления с локализованными именами. / Returns the enum items with localized names.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var enumType = value?.GetType();
        var prefix = parameter as string;
        if (enumType is null || !enumType.IsEnum) return new List<LocalizedEnumItem>();

        var list = new List<LocalizedEnumItem>();
        foreach (var name in Enum.GetNames(enumType))
        {
            var ev = Enum.Parse(enumType, name);
            var key = $"{(prefix ?? enumType.Name)}.{name}";
            var disp = LocalizationService.Current.GetString(key);
            list.Add(new LocalizedEnumItem((Enum)ev, disp));
        }
        return list;
    }

    /// <summary>Обратное преобразование: LocalizedEnumItem → enum value. / Reverse: LocalizedEnumItem → enum value.</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is LocalizedEnumItem item ? item.Value : Binding.DoNothing;
}
