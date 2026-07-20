using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CoderCommander.ViewModels;

namespace CoderCommander.Models;

/// <summary>
/// Модель вкладки панели файлового менеджера: обёртка над PanelViewModel
/// с динамическим заголовком (имя текущей папки) и меткой изменений.
/// Panel tab model: wraps a PanelViewModel with a dynamic title (folder name)
/// and a modification flag.
/// </summary>
public partial class TabViewModel : ObservableObject
{
    /// <summary>Панель, связанная с этой вкладкой. / The panel bound to this tab.</summary>
    [ObservableProperty] private PanelViewModel _panel;

    /// <summary>Заголовок вкладки (имя текущей папки). / Tab title (current folder name).</summary>
    [ObservableProperty] private string _tabTitle = "";

    /// <summary>
    /// Флаг: были ли изменения в панели (пока не используется, зарезервирован для будущего).
    /// Whether the panel has been modified (not yet used, reserved for the future).
    /// </summary>
    [ObservableProperty] private bool _isModified;

    /// <summary>
    /// Создаёт вкладку для указанной панели. Заголовок автоматически обновляется
    /// при навигации (Panel.CurrentPath → имя папки).
    /// Creates a tab for the given panel. The title auto-updates on navigation
    /// (Panel.CurrentPath → folder name).
    /// </summary>
    /// <param name="panel">Панель для вкладки. / The panel for this tab.</param>
    public TabViewModel(PanelViewModel panel)
    {
        _panel = panel;
        UpdateTitle();
        // При изменении пути панели обновляем заголовок вкладки
        // Update the tab title when the panel's current path changes
        panel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PanelViewModel.CurrentPath))
                UpdateTitle();
        };
    }

    /// <summary>
    /// Обновляет заголовок вкладки, извлекая имя последнего сегмента пути.
    /// Updates the tab title by extracting the last path segment name.
    /// </summary>
    private void UpdateTitle()
    {
        var path = Panel.CurrentPath.TrimEnd('\\');
        if (path.Length == 0) { TabTitle = Panel.CurrentPath; return; }
        var name = Path.GetFileName(path);
        TabTitle = string.IsNullOrEmpty(name) ? Panel.CurrentPath : name;
    }
}
