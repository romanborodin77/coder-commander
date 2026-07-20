using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoderCommander.Models;

/// <summary>
/// Один шаг макроса: имя команды и параметры.
/// A single macro step: command name and parameters.
/// </summary>
public partial class MacroStep : ObservableObject
{
    /// <summary>
    /// Имя команды (например "app.copy"). / Command name (e.g. "app.copy").
    /// </summary>
    [ObservableProperty]
    private string _commandName = "";

    /// <summary>
    /// Параметры команды (ключ → значение). / Command parameters (key → value).
    /// </summary>
    [ObservableProperty]
    private Dictionary<string, string> _params = new();

    /// <summary>
    /// Порядок выполнения шага. / Step execution order.
    /// </summary>
    [ObservableProperty]
    private int _order;

    /// <summary>
    /// Параметры как строка для отображения (key=value;key2=value2). Не сериализуется.
    /// Parameters as display string (key=value;key2=value2). Not serialized.
    /// </summary>
    [JsonIgnore]
    public string ParamsDisplay => string.Join(";", Params.Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>
    /// Создаёт пустой шаг макроса.
    /// Creates an empty macro step.
    /// </summary>
    public MacroStep() { }

    /// <summary>
    /// Создаёт шаг макроса с указанными именем команды и порядком.
    /// Creates a macro step with the specified command name and order.
    /// </summary>
    /// <param name="commandName">Имя команды. / Command name.</param>
    /// <param name="order">Порядок выполнения. / Execution order.</param>
    public MacroStep(string commandName, int order)
    {
        _commandName = commandName;
        _order = order;
    }
}
