using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel: команда «Открыть как» (ph5.5, exp.yml).
/// Partial ViewModel: "Open With" command (ph5.5).
/// Точка интеграции: пункт контекстного меню «Открыть как…» в FilePanel.xaml,
/// горячая клавиша Shift+Enter. Команда открывает <see cref="OpenWithWindow"/>
/// для выделенного файла активной панели.
/// Integration point: context menu item "Open With..." in FilePanel.xaml,
/// hotkey Shift+Enter. Command opens OpenWithWindow for the selected file
/// in the active panel.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Открывает диалог «Открыть как» для выделенного файла активной панели.
    /// Opens the "Open With" dialog for the selected file in the active panel.
    /// </summary>
    [RelayCommand]
    private void OpenWith()
    {
        var item = ActivePanel.SelectedItem;
        if (item is null || item.IsParent || item.IsDirectory) return;

        var dialog = new OpenWithWindow(item.FullPath)
        {
            Owner = Application.Current.MainWindow
        };

        ActivePanel.SaveFocus();
        if (dialog.ShowDialog() == true && dialog.ResultAppPath is not null)
        {
            OpenWithService.OpenFile(item.FullPath, dialog.ResultAppPath);
            StatusText = string.Format(
                LocalizationService.Current.GetString("OpenWith.Opened"),
                System.IO.Path.GetFileName(dialog.ResultAppPath),
                item.Name);
        }
        ActivePanel.RestoreFocus();
    }
}
