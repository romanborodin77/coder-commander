using System.Windows;
using System.Windows.Input;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Диалог поиска по файловой системе (ph2.1 / exp.yml): критерии + результаты.
/// File-system search dialog (ph2.1): criteria + results.
/// Двойной клик по результату открывает файл в редакторе через существующий механизм
/// <see cref="MainViewModel.OpenEditorRequest"/>.
/// Double-click on a result opens the file in the editor via the existing OpenEditorRequest.
/// </summary>
public partial class SearchDialog : Window
{
    public SearchDialog()
    {
        InitializeComponent();
        if (DataContext is SearchDialogViewModel vm)
            vm.OpenFileRequest = OpenInEditor;
    }

    /// <summary>
    /// При загрузке окна устанавливаем фокус на первое поле (маски имён).
    /// On window load, focus the first field (name masks).
    /// </summary>
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        NameMasksBox.Focus();
        NameMasksBox.SelectAll();
    }

    /// <summary>
    /// Enter — запуск поиска, Escape — закрытие диалога.
    /// Enter — start search, Escape — close dialog.
    /// </summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (DataContext is SearchDialogViewModel vm && !vm.IsRunning)
            {
                vm.SearchCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Открывает файл в редакторе (через <see cref="MainViewModel.OpenEditorRequest"/>).
    /// Opens the file in the editor via OpenEditorRequest.
    /// </summary>
    private void OpenInEditor(string path)
    {
        try
        {
            var content = System.IO.File.ReadAllText(path);
            if (Application.Current.MainWindow?.DataContext is MainViewModel mvm)
                mvm.OpenEditorRequest?.Invoke(path, content);
        }
        catch (Exception ex)
        {
            StyledMessageBoxWindow.Show(string.Format(LocalizationService.Current.GetString("Error.OpenFile"), ex.Message), LocalizationService.Current.GetString("Error.Title"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is SearchResult r && DataContext is SearchDialogViewModel vm)
            vm.OpenResultCommand.Execute(r);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
