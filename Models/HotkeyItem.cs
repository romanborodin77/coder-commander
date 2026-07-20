using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoderCommander.Models;

/// <summary>
/// Описание горячей клавиши: действие (ID) + ключ + модификаторы.
/// Describes a hotkey binding: action (ID) + key + modifiers.
/// Наследует ObservableObject для живого обновления DataGrid после захвата клавиши.
/// Inherits ObservableObject so the DataGrid refreshes live after key capture.
/// </summary>
public partial class HotkeyItem : ObservableObject
{
    /// <summary>
    /// Уникальный идентификатор действия (например "File.Copy").
    /// Unique action identifier (e.g. "File.Copy").
    /// </summary>
    [ObservableProperty] private string _action = "";

    /// <summary>
    /// Отображаемое название действия (локализовано).
    /// Display name of the action (localized).
    /// </summary>
    [ObservableProperty] private string _displayName = "";

    /// <summary>
    /// Клавиша (например "F5", "Delete").
    /// Key name (e.g. "F5", "Delete").
    /// </summary>
    [ObservableProperty] private string _key = "";

    /// <summary>
    /// Модификаторы через "+": "Ctrl", "Alt", "Shift" или комбинация "Ctrl+Shift".
    /// "+"-separated modifiers: "Ctrl", "Alt", "Shift" or combination "Ctrl+Shift".
    /// Пустая строка = без модификаторов. / Empty string = no modifiers.
    /// </summary>
    [ObservableProperty] private string _modifiers = "";

    /// <summary>
    /// Категория для группировки в UI.
    /// Category for grouping in UI.
    /// </summary>
    [ObservableProperty] private string _category = "";

    /// <summary>
    /// Описание действия (подсказка).
    /// Action description (tooltip).
    /// </summary>
    [ObservableProperty] private string _description = "";

    /// <summary>
    /// Полная комбинация для отображения (например "Ctrl+Shift+S"). Не сериализуется.
    /// Full combination for display (e.g. "Ctrl+Shift+S"). Not serialized.
    /// </summary>
    [JsonIgnore]
    public string DisplayGesture => string.IsNullOrEmpty(Modifiers)
        ? Key
        : $"{Modifiers}+{Key}";

    /// <summary>Уведомить UI об изменении DisplayGesture при смене клавиши. / Notify DisplayGesture change on key change.</summary>
    partial void OnKeyChanged(string value) => OnPropertyChanged(nameof(DisplayGesture));

    /// <summary>Уведомить UI об изменении DisplayGesture при смене модификаторов. / Notify DisplayGesture change on modifiers change.</summary>
    partial void OnModifiersChanged(string value) => OnPropertyChanged(nameof(DisplayGesture));
}