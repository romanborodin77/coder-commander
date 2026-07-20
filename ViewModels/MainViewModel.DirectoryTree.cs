using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel: команда «Дерево каталогов» (ph5.6, Alt+F1/F2).
/// Partial ViewModel: "Directory tree" command (ph5.6, Alt+F1/F2).
/// Открывает <see cref="DirectoryTreeWindow"/> для левой (Alt+F1) или правой (Alt+F2)
/// панели через делегат <see cref="OpenDirectoryTreeRequest"/>.
/// Opens DirectoryTreeWindow for the left (Alt+F1) or right (Alt+F2) panel
/// via the OpenDirectoryTreeRequest delegate.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Запрос на открытие окна дерева каталогов (делегируется View).
    /// Request to open the directory tree window (delegated to the View).
    /// Параметры: начальный путь, целевая панель.
    /// Parameters: initial path, target panel.
    /// </summary>
    public Action<string, PanelViewModel>? OpenDirectoryTreeRequest;

    /// <summary>
    /// Открывает окно дерева каталогов для левой панели (Alt+F1).
    /// Opens the directory tree window for the left panel (Alt+F1).
    /// </summary>
    [RelayCommand]
    private void OpenDirectoryTreeLeft()
    {
        OpenDirectoryTreeRequest?.Invoke(LeftPanel.CurrentPath, LeftPanel);
    }

    /// <summary>
    /// Открывает окно дерева каталогов для правой панели (Alt+F2).
    /// Opens the directory tree window for the right panel (Alt+F2).
    /// </summary>
    [RelayCommand]
    private void OpenDirectoryTreeRight()
    {
        OpenDirectoryTreeRequest?.Invoke(RightPanel.CurrentPath, RightPanel);
    }
}
