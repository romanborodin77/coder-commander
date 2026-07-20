using System.Windows;
using System.Windows.Input;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Окно менеджера плагинов: список DLL-расширений, включение/выключение, перезагрузка.
/// Plugin manager window: list of DLL extensions, enable/disable, reload.
/// </summary>
public partial class PluginManagerWindow : Window
{
    private readonly PluginManagerViewModel _vm;

    /// <summary>
    /// Конструктор окна менеджера плагинов.
    /// Plugin manager window constructor.
    /// </summary>
    public PluginManagerWindow()
    {
        InitializeComponent();
        _vm = new PluginManagerViewModel();
        _vm.RefreshPlugins();
        DataContext = _vm;
    }

    /// <summary>
    /// Закрытие окна. / Close the window.
    /// </summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Перетаскивание окна за заголовок. / Drag the window by the title bar.
    /// </summary>
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button) return;
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
