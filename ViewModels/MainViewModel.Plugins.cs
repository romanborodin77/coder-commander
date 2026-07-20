using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel: команда «Менеджер плагинов» (ph8.3).
/// Partial ViewModel: the "Plugin Manager" command (ph8.3).
/// Открывает окно управления плагинами.
/// Opens the plugin manager window.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Открывает окно управления плагинами.
    /// Opens the plugin manager window.
    /// </summary>
    [RelayCommand]
    private async Task OpenPluginManagerAsync()
    {
        await PluginManager.Instance.LoadPluginsAsync();
        ActivePanel.SaveFocus();
        var win = new PluginManagerWindow { Owner = Application.Current.MainWindow };
        win.ShowDialog();
        ActivePanel.RestoreFocus();
    }
}
