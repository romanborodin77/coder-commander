using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Диалог копирования/перемещения с выбором целевой папки и настройками.
/// Copy/move dialog with destination folder selection and options.
/// </summary>
public partial class CopyMoveDialog : Window
{
    public CopyMoveDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => { DestinationBox.Focus(); DestinationBox.SelectAll(); };
    }

    /// <summary>
    /// Инициализирует диалог данными. / Initializes the dialog with data.
    /// </summary>
    /// <param name="isCopyMode">true = копирование, false = перемещение. / true = copy, false = move.</param>
    /// <param name="sourcePath">Исходный путь. / Source path.</param>
    /// <param name="destinationPath">Целевой путь. / Destination path.</param>
    /// <param name="files">Список файлов для расчёта объёма. / File list for size calculation.</param>
    public void Initialize(bool isCopyMode, string sourcePath, string destinationPath, IEnumerable<string> files)
    {
        var vm = new CopyMoveDialogViewModel(this);
        vm.IsCopyMode = isCopyMode;
        vm.Title = isCopyMode
            ? Services.LocalizationService.Current.GetString("CopyMove.Title.Copy")
            : Services.LocalizationService.Current.GetString("CopyMove.Title.Move");
        vm.SourcePath = sourcePath;
        vm.DestinationPath = destinationPath;
        vm.CalculateTotalSize(files);
        DataContext = vm;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
