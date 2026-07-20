using CoderCommander.Services;

namespace CoderCommander.Models;

/// <summary>
/// Реестр действий горячих клавиш: ID → (категория, описание).
/// Registry of hotkey actions: ID → (category, description).
/// </summary>
public static class HotkeyActionRegistry
{
    /// <summary>
    /// Возвращает список действий по умолчанию.
    /// Returns the list of default hotkey bindings.
    /// </summary>
    public static List<HotkeyItem> GetDefaultBindings() => SettingsService.GetDefaultHotkeys();

    /// <summary>
    /// Возвращает локализованное название действия по ID.
    /// Returns the localized display name for the given action ID.
    /// </summary>
    public static string GetDisplayName(string action) => action switch
    {
        "File.Rename" => "Переименовать",
        "File.View" => "Просмотр",
        "File.Edit" => "Редактировать",
        "File.Copy" => "Копировать",
        "File.Move" => "Переместить",
        "File.CreateFolder" => "Создать папку",
        "File.Delete" => "Удалить",
        "File.Search" => "Поиск файлов",
        "Panel.DirectoryTreeLeft" => "Дерево каталогов (левая)",
        "Panel.DirectoryTreeRight" => "Дерево каталогов (правая)",
        _ => action
    };
}
