using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Диалог создания символьных/жёстких ссылок.
/// Dialog for creating symbolic/hard links.
/// </summary>
public partial class CreateLinkWindow : Window
{
    /// <summary>
    /// Конструктор для одного файла.
    /// Constructor for a single file.
    /// </summary>
    public CreateLinkWindow(string targetPath, bool isHardlink, bool isDirectory, string targetFolder)
    {
        InitializeComponent();
        var vm = new CreateLinkViewModel(targetPath, isHardlink, isDirectory, targetFolder);
        DataContext = vm;
        vm.RequestClose = success => DialogResult = success;
        Loaded += (_, _) =>
        {
            if (!vm.IsMultiMode && LinkNameBox is not null)
            {
                LinkNameBox.Focus();
                LinkNameBox.SelectAll();
            }
        };
    }

    /// <summary>
    /// Конструктор для нескольких файлов.
    /// Constructor for multiple files.
    /// </summary>
    public CreateLinkWindow(List<(string Path, bool IsDir)> files, bool isHardlink, string targetFolder)
    {
        InitializeComponent();
        var vm = new CreateLinkViewModel(files, isHardlink, targetFolder);
        DataContext = vm;
        vm.RequestClose = success => DialogResult = success;
    }

    /// <summary>Перетаскивание окна за заголовок. / Drag the window by its title bar.</summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
