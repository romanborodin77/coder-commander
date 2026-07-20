using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Диалог свойств файла/папки с редактированием атрибутов и меток времени.
/// File/folder properties dialog with attribute and timestamp editing.
/// </summary>
public partial class FilePropertiesWindow : Window
{
    public FilePropertiesWindow()
    {
        InitializeComponent();
        DataContext = new FilePropertiesWindowViewModel();
    }

    public static void ShowFor(string itemPath, bool isDirectory)
    {
        var w = new FilePropertiesWindow { Owner = Application.Current.MainWindow };
        var vm = (FilePropertiesWindowViewModel)w.DataContext;
        vm.RequestClose = () => { w.DialogResult = true; w.Close(); };
        _ = vm.LoadAsync(itemPath, isDirectory, CancellationToken.None);
        w.ShowDialog();
    }

    public static void ShowForMultiple(List<string> paths)
    {
        var w = new FilePropertiesWindow { Owner = Application.Current.MainWindow };
        var vm = (FilePropertiesWindowViewModel)w.DataContext;
        vm.RequestClose = () => { w.DialogResult = true; w.Close(); };
        _ = vm.LoadMultipleAsync(paths, CancellationToken.None);
        w.ShowDialog();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
