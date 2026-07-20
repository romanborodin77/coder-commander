using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// Менеджер плагинов: загрузка, выгрузка, включение/выключение DLL-расширений.
/// Plugin manager: loading, unloading, enabling/disabling DLL extensions.
/// Сканирует каталог plugins/, загружает сборки через AssemblyLoadContext (изоляция),
/// находит классы, реализующие IPlugin, и управляет их жизненным циклом.
/// Scans the plugins/ directory, loads assemblies via AssemblyLoadContext (isolation),
/// finds classes implementing IPlugin, and manages their lifecycle.
/// </summary>
public sealed class PluginManager
{
    /// <summary>Глобальный экземпляр менеджера. / Global manager instance.</summary>
    public static PluginManager Instance { get; } = new();

    private readonly List<PluginInfo> _plugins = new();
    private readonly Dictionary<string, AssemblyLoadContext> _contexts = new();
    private readonly object _lock = new();

    private CommandEngine? _commands;
    private ViewModels.MainViewModel? _mainVm;

    /// <summary>Путь к каталогу плагинов. / Path to the plugins directory.</summary>
    public static string PluginsDirectory => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "plugins");

    /// <summary>Событие изменения списка плагинов. / Event raised when the plugin list changes.</summary>
    public event EventHandler? PluginsChanged;

    private PluginManager() { }

    /// <summary>
    /// Инициализирует менеджер плагинами ссылками на CommandEngine и MainViewModel.
    /// Initializes the plugin manager with references to CommandEngine and MainViewModel.
    /// </summary>
    /// <param name="commands">Движок быстрых команд. / Quick command engine.</param>
    /// <param name="mainVm">Главная ViewModel. / Main ViewModel.</param>
    public void Initialize(CommandEngine commands, ViewModels.MainViewModel mainVm)
    {
        _commands = commands;
        _mainVm = mainVm;
    }

    /// <summary>
    /// Асинхронно загружает все плагины из каталога plugins/.
    /// Asynchronously loads all plugins from the plugins/ directory.
    /// </summary>
    public Task LoadPluginsAsync()
    {
        return Task.Run(() =>
        {
            var settings = SettingsService.Load();
            var enabledIds = new HashSet<string>(settings.EnabledPlugins, StringComparer.OrdinalIgnoreCase);

            var dir = PluginsDirectory;
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
                return;
            }

            var dlls = Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories);
            lock (_lock)
            {
                foreach (var dll in dlls)
                {
                    try
                    {
                        var id = Path.GetFileNameWithoutExtension(dll);
                        var isEnabled = enabledIds.Contains(id);
                        LoadPluginFromDll(dll, isEnabled);
                    }
                    catch (Exception ex)
                    {
                        var info = new PluginInfo
                        {
                            Id = Path.GetFileNameWithoutExtension(dll),
                            Name = Path.GetFileNameWithoutExtension(dll),
                            DllPath = dll,
                            LoadError = ex.Message
                        };
                        _plugins.Add(info);
                        LogService.Error($"Failed to load plugin: {dll}", nameof(PluginManager), ex);
                    }
                }
            }

            PluginsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// Загружает один плагин из DLL-файла.
    /// Loads a single plugin from a DLL file.
    /// </summary>
    private void LoadPluginFromDll(string dllPath, bool isEnabled)
    {
        var id = Path.GetFileNameWithoutExtension(dllPath);

        var context = new AssemblyLoadContext($"Plugin_{id}", isCollectible: true);
        Assembly assembly;
        try
        {
            using var fs = File.OpenRead(dllPath);
            assembly = context.LoadFromStream(fs);
        }
        catch
        {
            context.Unload();
            throw;
        }

        var pluginType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        if (pluginType is null)
        {
            context.Unload();
            LogService.Warn($"No IPlugin implementation found in: {dllPath}", nameof(PluginManager));
            return;
        }

        var instance = (IPlugin?)Activator.CreateInstance(pluginType);
        if (instance is null)
        {
            context.Unload();
            LogService.Warn($"Failed to create instance of: {pluginType.FullName}", nameof(PluginManager));
            return;
        }

        var info = new PluginInfo
        {
            Id = id,
            Name = instance.Name ?? id,
            Version = instance.Version ?? "1.0.0",
            Author = instance.Author ?? "",
            Description = instance.Description ?? "",
            DllPath = dllPath,
            IsEnabled = isEnabled,
            Instance = instance
        };

        _contexts[id] = context;
        _plugins.Add(info);

        if (isEnabled && _commands is not null && _mainVm is not null)
        {
            try
            {
                var host = new PluginHost(_commands, _mainVm);
                instance.Initialize(host);
                LogService.Info($"Plugin initialized: {info.Name}", nameof(PluginManager));
            }
            catch (Exception ex)
            {
                info.LoadError = ex.Message;
                LogService.Error($"Plugin init failed: {info.Name}", nameof(PluginManager), ex);
            }
        }
    }

    /// <summary>
    /// Выгружает плагин по идентификатору.
    /// Unloads a plugin by identifier.
    /// </summary>
    public void UnloadPlugin(string id)
    {
        lock (_lock)
        {
            var info = _plugins.FirstOrDefault(p => p.Id == id);
            if (info is null) return;

            try { info.Instance?.Shutdown(); }
            catch (Exception ex) { LogService.Error($"Plugin shutdown error: {info.Name}", nameof(PluginManager), ex); }

            info.Instance = null;
            info.IsEnabled = false;

            if (_contexts.TryGetValue(id, out var ctx))
            {
                ctx.Unload();
                _contexts.Remove(id);
            }

            var settings = SettingsService.Load();
            settings.EnabledPlugins.Remove(id);
            SettingsService.Save(settings);
        }
        PluginsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Перезагружает плагин по идентификатору.
    /// Reloads a plugin by identifier.
    /// </summary>
    public void ReloadPlugin(string id)
    {
        lock (_lock)
        {
            var info = _plugins.FirstOrDefault(p => p.Id == id);
            if (info is null) return;

            try { info.Instance?.Shutdown(); } catch { }

            if (_contexts.TryGetValue(id, out var ctx))
            {
                ctx.Unload();
                _contexts.Remove(id);
            }

            _plugins.Remove(info);

            try
            {
                LoadPluginFromDll(info.DllPath, true);
                LogService.Info($"Plugin reloaded: {id}", nameof(PluginManager));
            }
            catch (Exception ex)
            {
                LogService.Error($"Plugin reload failed: {id}", nameof(PluginManager), ex);
            }
        }
        PluginsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Возвращает список всех обнаруженных плагинов.
    /// Returns the list of all discovered plugins.
    /// </summary>
    public IReadOnlyList<PluginInfo> GetPlugins()
    {
        lock (_lock) return _plugins.ToList().AsReadOnly();
    }

    /// <summary>
    /// Включает плагин: загружает экземпляр и вызывает Initialize.
    /// Enables a plugin: loads the instance and calls Initialize.
    /// </summary>
    public void EnablePlugin(string id)
    {
        lock (_lock)
        {
            var info = _plugins.FirstOrDefault(p => p.Id == id);
            if (info is null || info.IsEnabled) return;

            if (info.Instance is null)
            {
                try
                {
                    var context = new AssemblyLoadContext($"Plugin_{id}", isCollectible: true);
                    using var fs = File.OpenRead(info.DllPath);
                    var assembly = context.LoadFromStream(fs);
                    _contexts[id] = context;

                    var pluginType = assembly.GetTypes()
                        .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                    if (pluginType is not null)
                        info.Instance = (IPlugin?)Activator.CreateInstance(pluginType);
                }
                catch (Exception ex)
                {
                    info.LoadError = ex.Message;
                    LogService.Error($"Plugin enable failed: {id}", nameof(PluginManager), ex);
                    return;
                }
            }

            if (info.Instance is not null && _commands is not null && _mainVm is not null)
            {
                try
                {
                    var host = new PluginHost(_commands, _mainVm);
                    info.Instance.Initialize(host);
                    info.IsEnabled = true;
                    info.LoadError = null;

                    var settings = SettingsService.Load();
                    if (!settings.EnabledPlugins.Contains(id))
                        settings.EnabledPlugins.Add(id);
                    SettingsService.Save(settings);

                    LogService.Info($"Plugin enabled: {info.Name}", nameof(PluginManager));
                }
                catch (Exception ex)
                {
                    info.LoadError = ex.Message;
                    LogService.Error($"Plugin init failed: {info.Name}", nameof(PluginManager), ex);
                }
            }
        }
        PluginsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Выключает плагин: вызывает Shutdown и выгружает сборку.
    /// Disables a plugin: calls Shutdown and unloads the assembly.
    /// </summary>
    public void DisablePlugin(string id) => UnloadPlugin(id);
}
