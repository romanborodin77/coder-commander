using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel: команда открытия диалога поиска (ph2.1 / exp.yml).
/// Partial ViewModel: command to open the search dialog (ph2.1).
/// Точка интеграции: пункт меню «Поиск…» в MainWindow.xaml биндится на <c>SearchCommand</c>;
/// начальная папка берётся из активной панели.
/// Integration point: the "Поиск…" menu item in MainWindow.xaml binds to SearchCommand;
/// the initial folder comes from the active panel.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Открывает модальный диалог поиска по содержимому (grep) для активной панели.
    /// Opens the modal content-search (grep) dialog for the active panel.
    /// </summary>
    [RelayCommand]
    private void Search()
    {
        ActivePanel.SaveFocus();
        var dlg = new SearchDialog { Owner = Application.Current.MainWindow };
        if (dlg.DataContext is SearchDialogViewModel vm)
        {
            vm.RootPath = ActivePanel.CurrentPath;
            // Двойной клик по результату открывает файл в редакторе.
            // Double-click on a result opens the file in the editor.
            vm.OpenFileRequest = p =>
            {
                try
                {
                    var content = Task.Run(() => File.ReadAllTextAsync(p)).GetAwaiter().GetResult();
                    OpenEditorRequest?.Invoke(p, content);
                }
                catch (Exception ex) { StatusText = string.Format(L10n("Status.Error"), ex.Message); }
            };
            // Кнопка «Показать результаты в панели» -> виртуальный источник (ph2.2).
            // "Show results in panel" button -> virtual source (ph2.2).
            vm.ShowInPanelRequest = results => ShowSearchResultsCommand.Execute(results);
        }
        dlg.ShowDialog();
        ActivePanel.RestoreFocus();
    }
}
