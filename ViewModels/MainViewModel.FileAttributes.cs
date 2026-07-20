using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel главного окна: редактор атрибутов/меток времени и создание
/// жёстких/символических ссылок. EditAttributesCommand открывает FilePropertiesWindow.
/// Partial MainViewModel: attribute/timestamp editor and hard/symlink creation.
/// EditAttributesCommand opens FilePropertiesWindow.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Открывает диалог свойств и редактирования атрибутов для выделенных элементов.
    /// Opens the properties and attribute editing dialog for the selected items.
    /// </summary>
    [RelayCommand]
    private void EditAttributes()
    {
        var items = GetAttributeTargets();
        if (items.Count == 0) { StatusText = L10n("Attr.NoSelection"); return; }
        ShowPropertiesWindowForItems(items);
    }

    /// <summary>
    /// Открывает диалог создания жёсткой ссылки (Hardlink) через CreateLinkWindow.
    /// Opens the hard link creation dialog via CreateLinkWindow.
    /// </summary>
    [RelayCommand]
    private void CreateHardlink()
    {
        var items = GetAttributeTargets();
        if (items.Count == 0) { StatusText = L10n("Attr.NoSelection"); return; }
        ShowCreateLinkWindowForSelection(items, isHardlink: true);
    }

    /// <summary>
    /// Открывает диалог создания символической ссылки (Symlink) через CreateLinkWindow.
    /// Opens the symbolic link creation dialog via CreateLinkWindow.
    /// </summary>
    [RelayCommand]
    private void CreateSymlink()
    {
        var items = GetAttributeTargets();
        if (items.Count == 0) { StatusText = L10n("Attr.NoSelection"); return; }
        ShowCreateLinkWindowForSelection(items, isHardlink: false);
    }

    /// <summary>
    /// Возвращает выделенные файлы и папки активной панели (без «..»).
    /// Returns selected files and folders of the active panel (no "..").
    /// </summary>
    private List<string> GetAttributeTargets()
        => ActivePanel.GetSelectionOrCurrent()
            .Where(i => !i.IsParent)
            .Select(i => i.FullPath)
            .ToList();

    /// <summary>
    /// Показывает модальный диалог свойств/атрибутов и обновляет панель после закрытия.
    /// Shows the modal properties/attributes dialog and refreshes the panel after it closes.
    /// </summary>
    private void ShowPropertiesWindowForItems(List<string> paths)
    {
        ActivePanel.SaveFocus();
        if (paths.Count == 1)
        {
            var path = paths[0];
            var isDir = Directory.Exists(path) && !File.Exists(path);
            FilePropertiesWindow.ShowFor(path, isDir);
        }
        else
        {
            FilePropertiesWindow.ShowForMultiple(paths);
        }
        ActivePanel.RestoreFocus();
        _ = ActivePanel.RefreshAsync();
        _ = (ActivePanel == LeftPanel ? RightPanel : LeftPanel).RefreshAsync();
    }

    /// <summary>
    /// Показывает модальный диалог создания ссылки для выделенных элементов.
    /// Shows the modal link creation dialog for the selected items.
    /// </summary>
    private void ShowCreateLinkWindowForSelection(List<string> paths, bool isHardlink)
    {
        var targetFolder = ActivePanel.CurrentPath;
        CreateLinkWindow window;

        if (paths.Count == 1)
        {
            var path = paths[0];
            var isDir = Directory.Exists(path) && !File.Exists(path);
            window = new CreateLinkWindow(path, isHardlink, isDir, targetFolder);
        }
        else
        {
            var files = paths.Select(p =>
            {
                var isDir = Directory.Exists(p) && !File.Exists(p);
                return (p, isDir);
            }).ToList();
            window = new CreateLinkWindow(files, isHardlink, targetFolder);
        }

        window.Owner = Application.Current.MainWindow;
        ActivePanel.SaveFocus();
        var result = window.ShowDialog();
        ActivePanel.RestoreFocus();

        if (result == true)
        {
            _ = ActivePanel.RefreshAsync();
            _ = (ActivePanel == LeftPanel ? RightPanel : LeftPanel).RefreshAsync();
        }
    }
}
