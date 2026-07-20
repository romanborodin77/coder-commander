using System.Windows.Controls;
using System.Windows.Input;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Код-behind панели SFTP-браузера: обрабатывает двойной клик по элементу списка.
/// Code-behind for the SFTP browser panel: handles double-click on list items.
/// </summary>
public partial class SftpPanel : UserControl
{
    /// <summary>
    /// Конструктор, инициализирующий компоненты XAML.
    /// Constructor that initializes the XAML components.
    /// </summary>
    public SftpPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Обработчик двойного клика по элементу списка — открывает папку или скачивает файл через команду вью-модели.
    /// Handles double-click on a list item — opens a folder or downloads a file via the view-model command.
    /// </summary>
    /// <param name="sender">Источник события (ListBox). / Event source (ListBox).</param>
    /// <param name="e">Данные события мыши. / Mouse event data.</param>
    private void Item_Dbl(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox lb && lb.DataContext is SftpViewModel vm)
            _ = vm.OpenItemCommand.ExecuteAsync(null);
    }
}
