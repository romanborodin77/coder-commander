using System.Windows;
using System.Windows.Input;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Диалог настройки колонок панели файлов: два списка, перемещение, порядок, ширина, видимость.
/// Column settings dialog for file panel: two lists, move, order, width, visibility.
/// </summary>
public partial class ColumnSettingsWindow : Window
{
    /// <summary>
    /// Конструктор, инициализирующий XAML-компоненты и создающий ViewModel.
    /// Constructor initializes XAML components and creates ViewModel.
    /// </summary>
    public ColumnSettingsWindow()
    {
        InitializeComponent();
        DataContext = new ColumnSettingsViewModel();
    }

    /// <summary>
    /// Обработчик кнопки «Закрыть»: закрывает окно без сохранения.
    /// Handles "Close" button: closes window without saving.
    /// </summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Обработчик перетаскивания окна за заголовок (WindowChrome CaptionHeight=0).
    /// Handles window drag from title bar (WindowChrome CaptionHeight=0).
    /// </summary>
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            Close();
        else
            DragMove();
    }
}
