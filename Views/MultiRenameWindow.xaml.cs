using System.Collections.Generic;
using System.Windows;
using CoderCommander.Services;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Окно мульти-переименования (ph2.3, exp.yml).
/// Multi-rename window (ph2.3). Модальное; ShowDialog() блокирует главное окно.
/// Modal; ShowDialog() blocks the main window.
/// </summary>
public partial class MultiRenameWindow : Window
{
    private readonly MultiRenameViewModel _vm;

    /// <summary>Создаёт окно для набора исходных файлов. / Creates the window for a set of source files.</summary>
    /// <param name="files">Выделенные элементы активной панели. / Selected items of the active panel.</param>
    public MultiRenameWindow(IReadOnlyList<MultiRenameEngine.SourceFile> files)
    {
        InitializeComponent();
        _vm = new MultiRenameViewModel(files);
        DataContext = _vm;
        _vm.ApplyCompleted += () => { DialogResult = true; Close(); };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => DragMove();

    private void ModeMask_Checked(object sender, RoutedEventArgs e) => _vm.UseRegexMode = false;
    private void ModeRegex_Checked(object sender, RoutedEventArgs e) => _vm.UseRegexMode = true;
}
