using System.Windows;
using System.Windows.Input;
using CoderCommander.Models;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Модальное окно дерева каталогов (ph5.6): навигация по файловой системе через TreeView.
/// Modal directory tree window (ph5.6): file system navigation via TreeView.
/// Alt+F1 — для левой панели, Alt+F2 — для правой.
/// Alt+F1 — for the left panel, Alt+F2 — for the right panel.
/// Кастомный titlebar (WindowStyle=None + WindowChrome), ленивая загрузка подпапок,
/// быстрый фильтр по набору букв, ESC = закрыть.
/// Custom titlebar (WindowStyle=None + WindowChrome), lazy loading of subdirectories,
/// quick filter by keystrokes, ESC = close.
/// </summary>
public partial class DirectoryTreeWindow : Window
{
    private readonly DirectoryTreeViewModel _vm;

    /// <summary>
    /// Конструктор окна: инициализирует компоненты, устанавливает DataContext,
    /// подписывается на закрытие.
    /// Constructor: initializes components, sets DataContext, subscribes to close requests.
    /// </summary>
    /// <param name="initialPath">Начальный путь (текущая папка панели). / Initial path (panel's current folder).</param>
    /// <param name="targetPanel">Целевая панель для навигации. / Target panel for navigation.</param>
    public DirectoryTreeWindow(string initialPath, PanelViewModel targetPanel)
    {
        InitializeComponent();

        _vm = new DirectoryTreeViewModel(initialPath, targetPanel);
        _vm.RequestClose = () => Dispatcher.BeginInvoke(new Action(() => { DialogResult = true; Close(); }));
        DataContext = _vm;

        Loaded += (_, _) => FilterBox.Focus();
    }

    /// <summary>
    /// Обработчик смены выделения в TreeView: передаёт выбор в ViewModel.
    /// Handles TreeView selection change: passes selection to ViewModel.
    /// </summary>
    private void DirTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is DirectoryTreeNode node)
            _vm.SelectedNode = node;
    }

    /// <summary>
    /// Обработчик двойного клика: выбирает узел и закрывает окно.
    /// Handles double-click: selects the node and closes the window.
    /// </summary>
    private void DirTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedNode is not null)
            _vm.SelectNodeCommand.Execute(_vm.SelectedNode);
    }

    // ═══════════════════════════════════════════
    // КАСТОМНЫЙ TITLEBAR / CUSTOM TITLEBAR
    // ═══════════════════════════════════════════

    /// <summary>
    /// Обработчик нажатия ЛКМ на titlebar: перетаскивание окна.
    /// Handles left mouse button press on the title bar: drag-move.
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            else
                DragMove();
        }
    }

    /// <summary>
    /// Обработчик кнопки «Закрыть»: закрывает окно.
    /// Handles the "Close" button: closes the window.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.CancelCommand.Execute(null);
    }
}
