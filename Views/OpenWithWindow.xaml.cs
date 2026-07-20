using System.Windows;
using System.Windows.Input;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Модальное окно «Открыть как» (ph5.5): выбор ассоциированного приложения для файла
/// из списка с иконками, возможностью ручного выбора .exe и установки по умолчанию.
/// Modal "Open With" window (ph5.5): choosing an associated application for a file
/// from a list with icons, manual .exe selection, and default setting.
/// </summary>
public partial class OpenWithWindow : Window
{
    /// <summary>
    /// ViewModel окна «Открыть как». / "Open With" window ViewModel.
    /// </summary>
    private readonly OpenWithViewModel _vm;

    /// <summary>
    /// Результат выбора: путь к приложению или null при отмене.
    /// Selection result: path to the application or null if cancelled.
    /// </summary>
    public string? ResultAppPath => _vm.ResultAppPath;

    /// <summary>
    /// Конструктор окна «Открыть как»: инициализирует компоненты, устанавливает DataContext.
    /// "Open With" window constructor: initializes components, sets DataContext.
    /// </summary>
    /// <param name="filePath">Путь к файлу. / Path to the file.</param>
    public OpenWithWindow(string filePath)
    {
        InitializeComponent();

        _vm = new OpenWithViewModel();
        DataContext = _vm;

        _vm.LoadApps(filePath);
    }

    /// <summary>
    /// Обработчик перетаскивания заголовка окна. / Title bar drag handler.
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    /// <summary>
    /// Обработчик кнопки закрытия. / Close button handler.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.CancelCommand.Execute(this);
    }
}
