using System;
using System.Collections.Generic;
using System.Linq;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// Сервис управления макросами: загрузка, сохранение, CRUD-операции.
/// Macro management service: load, save, CRUD operations.
/// Макросы хранятся в settings.json (раздел "Macros").
/// Macros are stored in settings.json (section "Macros").
/// </summary>
public sealed class MacroService
{
    /// <summary>
    /// Синглтон сервиса. / Service singleton.
    /// </summary>
    public static MacroService Current { get; } = new();

    /// <summary>
    /// Событие при изменении списка макросов. / Fired when the macro list changes.
    /// </summary>
    public event EventHandler? MacrosChanged;

    /// <summary>
    /// Возвращает все макросы из настроек. / Returns all macros from settings.
    /// </summary>
    public IReadOnlyList<MacroItem> GetAll()
    {
        return SettingsService.Load().Macros;
    }

    /// <summary>
    /// Возвращает макрос по ID или null. / Returns the macro by ID or null.
    /// </summary>
    /// <param name="id">Идентификатор макроса. / Macro identifier.</param>
    public MacroItem? GetById(string id)
    {
        return SettingsService.Load().Macros.FirstOrDefault(m => m.Id == id);
    }

    /// <summary>
    /// Находит макрос по горячей клавише (если включён). / Finds a macro by hotkey (if enabled).
    /// </summary>
    /// <param name="hotkey">Горячая клавиша (например "Ctrl+Shift+M"). / Hotkey (e.g. "Ctrl+Shift+M").</param>
    public MacroItem? FindByHotkey(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return null;
        return SettingsService.Load().Macros.FirstOrDefault(m =>
            m.IsEnabled && string.Equals(m.Hotkey, hotkey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Добавляет макрос в список. / Adds a macro to the list.
    /// </summary>
    /// <param name="macro">Макрос для добавления. / Macro to add.</param>
    public void Add(MacroItem macro)
    {
        SettingsService.Load().Macros.Add(macro);
        MacrosChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Удаляет макрос по ID. / Removes a macro by ID.
    /// </summary>
    /// <param name="id">Идентификатор макроса. / Macro identifier.</param>
    /// <returns>true, если макрос найден и удалён. / true if the macro was found and removed.</returns>
    public bool Delete(string id)
    {
        var settings = SettingsService.Load();
        var idx = settings.Macros.FindIndex(m => m.Id == id);
        if (idx < 0) return false;
        settings.Macros.RemoveAt(idx);
        MacrosChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Сохраняет текущие настройки (включая макросы) в файл. / Persists current settings (including macros) to file.
    /// </summary>
    public void Save()
    {
        SettingsService.Save(SettingsService.Load());
        MacrosChanged?.Invoke(this, EventArgs.Empty);
    }
}
