using System.ComponentModel;
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
        SubscribeToPanel(panel);
    }

    /// <summary>
    /// Подписывается на изменения свойств панели для обновления заголовка.
    /// Subscribes to panel property changes for title updates.
    /// </summary>
    private void SubscribeToPanel(PanelViewModel panel)
    {
        panel.PropertyChanged += OnPanelPropertyChanged;
    }

    /// <summary>
    /// Отписывается от изменений свойств панели.
    /// Unsubscribes from panel property changes.
    /// </summary>
    private void UnsubscribeFromPanel(PanelViewModel? panel)
    {
        if (panel is null) return;
        panel.PropertyChanged -= OnPanelPropertyChanged;
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelViewModel.CurrentPath))
            UpdateTitle();
    }

    /// <summary>
    /// Вызывается при замене панели: отписывается от старой и подписывается на новую.
    /// Called when Panel changes: unsubscribes from old, subscribes to new.
    /// </summary>
    partial void OnPanelChanged(PanelViewModel? oldValue, PanelViewModel newValue)
    {
        UnsubscribeFromPanel(oldValue);
        SubscribeToPanel(newValue);
        UpdateTitle();
    }

    /// <summary>
    /// Обновляет заголовок вкладки, извлекая имя последнего сегмента пути.
    /// Для облачных путей (cloud://) показывает имя профиля.
    /// Updates the tab title by extracting the last path segment name.
    /// For cloud paths (cloud://), shows the profile name.
    /// </summary>
    private void UpdateTitle()
    {
        var path = Panel.CurrentPath;
        if (string.IsNullOrEmpty(path)) return;

        // Облачные пути: cloud://profileId/... → имя профиля.
        if (path.StartsWith("cloud://", StringComparison.OrdinalIgnoreCase))
        {
            var profileId = ExtractProfileId(path);
            var profileName = FindProfileName(profileId);
            TabTitle = string.IsNullOrEmpty(profileName) ? profileId : profileName;
            return;
        }

        var trimmed = path.TrimEnd('\\');
        if (trimmed.Length == 0) { TabTitle = path; return; }
        var name = Path.GetFileName(trimmed);
        TabTitle = string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>Извлекает profileId из cloud:// пути. / Extracts profileId from cloud:// path.</summary>
    private static string ExtractProfileId(string cloudPath)
    {
        var withoutScheme = "cloud://".Length;
        var slash = cloudPath.IndexOf('/', withoutScheme);
        return slash > withoutScheme ? cloudPath[withoutScheme..slash] : cloudPath[withoutScheme..];
    }

    /// <summary>Находит имя профиля по ID. / Finds profile name by ID.</summary>
    private static string FindProfileName(string profileId)
    {
        if (string.IsNullOrEmpty(profileId)) return "";
        try
        {
            var profiles = new Services.CloudStorageService().GetProfiles();
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            return profile?.Name ?? "";
        }
        catch
        {
            return "";
        }
    }
}
