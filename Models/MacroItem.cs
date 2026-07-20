using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoderCommander.Models;

/// <summary>
/// Модель макроса: имя, описание, горячая клавиша и список шагов.
/// Macro model: name, description, hotkey, and step list.
/// </summary>
public partial class MacroItem : ObservableObject
{
    /// <summary>
    /// Имя макроса. / Macro name.
    /// </summary>
    [ObservableProperty]
    private string _name = "";

    /// <summary>
    /// Описание макроса. / Macro description.
    /// </summary>
    [ObservableProperty]
    private string _description = "";

    /// <summary>
    /// Горячая клавиша (например "Ctrl+Shift+M"). / Hotkey (e.g. "Ctrl+Shift+M").
    /// </summary>
    [ObservableProperty]
    private string _hotkey = "";

    /// <summary>
    /// Список шагов макроса. / Macro step list.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MacroStep> _steps = new();

    /// <summary>
    /// Активен ли макрос. / Whether the macro is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>
    /// Уникальный идентификатор макроса. / Unique macro identifier.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Создаёт пустой макрос.
    /// Creates an empty macro.
    /// </summary>
    public MacroItem()
    {
        Id = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Создаёт макрос с указанным именем.
    /// Creates a macro with the specified name.
    /// </summary>
    /// <param name="name">Имя макроса. / Macro name.</param>
    public MacroItem(string name)
    {
        Id = Guid.NewGuid().ToString("N");
        _name = name;
    }

    /// <summary>
    /// Отображение макроса: имя + хоткей. / Macro display: name + hotkey.
    /// </summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrEmpty(Hotkey)
        ? Name
        : $"{Name} ({Hotkey})";
}
