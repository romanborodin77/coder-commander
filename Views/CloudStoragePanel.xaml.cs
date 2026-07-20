using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Code-behind панели облачных хранилищ.
/// Code-behind for the cloud storage panel.
/// </summary>
public partial class CloudStoragePanel : UserControl
{
    /// <summary>Конструктор. / Constructor.</summary>
    public CloudStoragePanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Обработчик двойного клика по элементу списка.
    /// Handles double-click on a list item.
    /// </summary>
    private void Item_Dbl(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox lb && lb.DataContext is CloudStorageViewModel vm)
            _ = vm.OpenItemCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Открывает гиперссылку в браузере по умолчанию.
    /// Opens a hyperlink in the default browser.
    /// </summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
