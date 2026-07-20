using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel менеджера плагинов: список, включение/выключение, перезагрузка.
/// Plugin manager ViewModel: list, enable/disable, reload.
/// </summary>
public partial class PluginManagerViewModel : ObservableObject
{
    /// <summary>Список обнаруженных плагинов. / List of discovered plugins.</summary>
    [ObservableProperty]
    private ObservableCollection<PluginInfo> _plugins = new();

    /// <summary>Выбранный плагин в списке. / Selected plugin in the list.</summary>
    [ObservableProperty]
    private PluginInfo? _selectedPlugin;

    /// <summary>
    /// Загружает список плагинов из менеджера.
    /// Loads the plugin list from the manager.
    /// </summary>
    public void RefreshPlugins()
    {
        var list = PluginManager.Instance.GetPlugins();
        Plugins = new ObservableCollection<PluginInfo>(list);
    }

    /// <summary>
    /// Включает выбранный плагин. / Enables the selected plugin.
    /// </summary>
    [RelayCommand]
    private void EnablePlugin()
    {
        if (SelectedPlugin is null) return;
        PluginManager.Instance.EnablePlugin(SelectedPlugin.Id);
        RefreshPlugins();
        SelectedPlugin = Plugins.FirstOrDefault(p => p.Id == SelectedPlugin?.Id);
    }

    /// <summary>
    /// Выключает выбранный плагин. / Disables the selected plugin.
    /// </summary>
    [RelayCommand]
    private void DisablePlugin()
    {
        if (SelectedPlugin is null) return;
        PluginManager.Instance.DisablePlugin(SelectedPlugin.Id);
        RefreshPlugins();
        SelectedPlugin = Plugins.FirstOrDefault(p => p.Id == SelectedPlugin?.Id);
    }

    /// <summary>
    /// Перезагружает выбранный плагин. / Reloads the selected plugin.
    /// </summary>
    [RelayCommand]
    private void ReloadPlugin()
    {
        if (SelectedPlugin is null) return;
        var id = SelectedPlugin.Id;
        PluginManager.Instance.ReloadPlugin(id);
        RefreshPlugins();
        SelectedPlugin = Plugins.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>
    /// Открывает каталог плагинов в проводнике Windows.
    /// Opens the plugins folder in Windows Explorer.
    /// </summary>
    [RelayCommand]
    private void OpenPluginFolder()
    {
        var dir = PluginManager.PluginsDirectory;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        Process.Start("explorer.exe", dir);
    }

    /// <summary>
    /// Перечитывает каталог плагинов. / Re-scans the plugins directory.
    /// </summary>
    [RelayCommand]
    private async Task RefreshPluginsAsync()
    {
        await PluginManager.Instance.LoadPluginsAsync();
        RefreshPlugins();
    }
}
