namespace CoderCommander.Models;

/// <summary>
/// Метаданные плагина: идентификатор, информация, путь к DLL, состояние загрузки.
/// Plugin metadata: identifier, info, DLL path, load state.
/// </summary>
public class PluginInfo
{
    /// <summary>Уникальный идентификатор (GUID). / Unique identifier (GUID).</summary>
    public string Id { get; set; } = "";

    /// <summary>Название плагина. / Plugin name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Версия плагина. / Plugin version.</summary>
    public string Version { get; set; } = "";

    /// <summary>Автор плагина. / Plugin author.</summary>
    public string Author { get; set; } = "";

    /// <summary>Описание плагина. / Plugin description.</summary>
    public string Description { get; set; } = "";

    /// <summary>Путь к DLL-файлу плагина. / Path to the plugin DLL file.</summary>
    public string DllPath { get; set; } = "";

    /// <summary>Включён ли плагин (сохраняется в настройках). / Whether the plugin is enabled (persisted in settings).</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Загруженный экземпляр плагина (null, если не загружен). / Loaded plugin instance (null if not loaded).</summary>
    public IPlugin? Instance { get; set; }

    /// <summary>Сообщение об ошибке загрузки (пусто, если загружен успешно). / Load error message (empty if loaded successfully).</summary>
    public string? LoadError { get; set; }
}
