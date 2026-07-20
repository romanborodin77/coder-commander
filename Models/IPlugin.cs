namespace CoderCommander.Models;

/// <summary>
/// Базовый интерфейс плагина. Все DLL-расширения должны реализовывать этот интерфейс.
/// Base plugin interface. All DLL extensions must implement this interface.
/// </summary>
public interface IPlugin
{
    /// <summary>Человекочитаемое название плагина. / Human-readable plugin name.</summary>
    string Name { get; }

    /// <summary>Версия плагина. / Plugin version.</summary>
    string Version { get; }

    /// <summary>Автор плагина. / Plugin author.</summary>
    string Author { get; }

    /// <summary>Описание плагина. / Plugin description.</summary>
    string Description { get; }

    /// <summary>
    /// Вызывается при загрузке плагина. Плагин получает доступ к хосту для регистрации расширений.
    /// Called when the plugin is loaded. The plugin receives host access for registering extensions.
    /// </summary>
    /// <param name="host">Интерфейс хоста для регистрации расширений. / Host interface for registering extensions.</param>
    void Initialize(IPluginHost host);

    /// <summary>
    /// Вызывается при выгрузке плагина. Плагин должен освободить ресурсы.
    /// Called when the plugin is unloaded. The plugin should release resources.
    /// </summary>
    void Shutdown();
}
