using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace CoderCommander.Views;

/// <summary>
/// Модальное окно прогресса файловой операции.
/// Показывает два ProgressBar (файл + общий), скорость, ETA, управление паузой/отменой.
/// Modal window for file operation progress.
/// Shows two ProgressBar (file + overall), speed, ETA, pause/cancel controls.
/// </summary>
public partial class OperationDialogWindow : Window
{
    /// <summary>
    /// Создаёт окно прогресса операции. / Creates the operation progress window.
    /// </summary>
    public OperationDialogWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    /// <summary>
    /// Обработчик нажатия левой кнопки мыши на заголовке: перетаскивание окна.
    /// Handles left mouse button press on title bar: drag-move.
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    /// <summary>
    /// Обработчик кнопки «Закрыть»: отменяет операцию и закрывает диалог.
    /// Handles the "Close" button: cancels the operation and closes the dialog.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.OperationDialogViewModel vm && !vm.IsComplete)
            vm.CancelCommand.Execute(null);
        Close();
    }

    /// <summary>
    /// При закрытии окна (Alt+F4 и т.д.) — отменяем операцию.
    /// On window close (Alt+F4 etc.) — cancel the operation.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is ViewModels.OperationDialogViewModel vm && !vm.IsComplete)
            vm.CancelCommand.Execute(null);
    }
}
