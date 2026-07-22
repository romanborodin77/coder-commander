using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Главное окно приложения: меню, тулбар, файловые панели, редактор/просмотрщик, терминал, git/docker/ssh/sftp вкладки.
/// Application main window: menu, toolbar, file panels, editor/viewer, terminal, git/docker/ssh/sftp tabs.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Ссылка на MainViewModel (DataContext окна). / Reference to the MainViewModel (window DataContext).</summary>
    private MainViewModel? _vm;

    /// <summary>
    /// Словарь HwndHost-контейнеров терминалов: tabId > TerminalHost.
    /// Dictionary of terminal HwndHost containers: tabId > TerminalHost.
    /// </summary>
    private readonly Dictionary<int, TerminalHost> _terminalHosts = new();

    /// <summary>
    /// Словарь обработчиков событий для корректной отписки при удалении вкладки.
    /// Dictionary of event handlers for proper unsubscription when a tab is removed.
    /// </summary>
    private readonly Dictionary<int, (EventHandler<IntPtr>? hostReady, SizeChangedEventHandler? sizeChanged)> _terminalHandlers = new();

    /// <summary>
    /// Открывает модальный диалог с сохранением и восстановлением фокуса активной панели.
    /// Opens a modal dialog with saving and restoring focus of the active panel.
    /// </summary>
    /// <param name="dialog">Диалоговое окно. / Dialog window.</param>
    /// <returns>Результат диалога. / Dialog result.</returns>
    private bool? ShowDialogWithFocus(Window dialog)
    {
        if (_vm?.ActivePanel is PanelViewModel panel)
        {
            panel.SaveFocus();
        }

        try
        {
            return dialog.ShowDialog();
        }
        finally
        {
            if (_vm?.ActivePanel is PanelViewModel panelAfter)
            {
                panelAfter.RestoreFocus();
            }
        }
    }

    /// <summary>
    /// Конструктор главного окна: инициализация XAML-компонентов и подписка на события Loaded/Closed.
    /// Main window constructor: initializes XAML components and subscribes to Loaded/Closed events.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// Обработчик события Loaded: сохраняет ViewModel, подписывается на события, устанавливает делегаты
    /// для редактора/просмотрщика и запускает авто-старт первого терминала.
    /// Handles the Loaded event: stores the ViewModel, subscribes to events, sets editor/viewer delegates,
    /// and initiates auto-start of the first terminal.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные маршрутизованного события. / Routed event data.</param>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        _vm = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.TerminalTabs.CollectionChanged += OnTerminalTabsChanged;
        vm.PropertyChanged += OnActiveTerminalTabChanged;

        // Устанавливаем делегаты для открытия редактора/просмотрщика
        vm.OpenEditorRequest = (path, content) => OpenEditor(path, content);
        vm.OpenViewerRequest = (path, content) => OpenViewer(path, content);
        vm.CloseEditorRequest = () => { }; // Заглушка для совместимости / Stub for compatibility

        // Устанавливаем делегат открытия окна дубликатов (ph2.4)
        vm.OpenDuplicatesRequest = (path) => OpenDuplicates(path);

        // Устанавливаем делегат открытия синхронизации папок (ph3.3)
        vm.OpenSyncDirsRequest = (left, right, selected) => OpenSyncDirs(left, right, selected);

        // Устанавливаем делегат открытия менеджера закладок (ph5.3)
        vm.OpenBookmarksRequest = () => OpenBookmarks();
        //Подписка завершена

            //Устанавливаем делегат открытия архивов (ph5.1)
            vm.OpenArchiveRequest = (mode, files, archivePath) => OpenArchive(mode, files, archivePath);

        // Устанавливаем делегат открытия дерева каталогов (ph5.6)
        vm.OpenDirectoryTreeRequest = (path, panel) => OpenDirectoryTree(path, panel);

        // Запускаем отложенный старт терминалов
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, EnsureTerminalShellsStarted);

        // Применяем динамические горячие клавиши (ph6.1)
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, ApplyHotkeys);
    }

    /// <summary>
    /// Обработчик события Closed: отписывается от событий, освобождает ресурсы VM и всех TerminalHost.
    /// Handles the Closed event: unsubscribes from events, disposes VM and all TerminalHost resources.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные события. / Event data.</param>
    private void OnClosed(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.TerminalTabs.CollectionChanged -= OnTerminalTabsChanged;
        _vm.PropertyChanged -= OnActiveTerminalTabChanged;
        _vm.Dispose();
        foreach (var th in _terminalHosts.Values) th.Dispose();
        _terminalHosts.Clear();
    }

    //========== Обработка изменений свойств ==========

    /// <summary>
    /// Обработчик изменения свойств MainViewModel: отслеживает видимость панели терминала,
    /// переключение вкладок нижней панели и сворачивание/разворачивание нижней панели.
    /// Handles MainViewModel property changes: tracks terminal panel visibility,
    /// bottom tab switching, and bottom panel expand/collapse.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные события изменения свойства. / Property changed event data.</param>
        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Защита от разыменования возможно-null ViewModel (ещё не инициализирована).
            // Guard against dereferencing a possibly-null view model (not initialized yet).
            if (_vm is null) return;

            // Если видимость панели терминала изменилась — синхронизируем TabItems и запускаем shell'ы
        if (e.PropertyName == nameof(MainViewModel.IsTerminalPanelVisible) && _vm?.IsTerminalPanelVisible == true)
        {
            SyncTerminalTabs();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, EnsureTerminalShellsStarted);
        }
        // Переключение вкладок нижней панели (editor/git/docker/ssh/sftp)
        else if (e.PropertyName == nameof(MainViewModel.SelectedTabIndex))
        {
            SyncTerminalTabs();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, EnsureTerminalShellsStarted);
        }
        // Сворачивание/разворачивание нижней панели терминала (из настроек / по вызову)
        else if (e.PropertyName == nameof(MainViewModel.IsBottomPanelVisible) && BottomRow is not null)
        {
                BottomRow.Height = _vm?.IsBottomPanelVisible ?? false
                    ? new GridLength(1, GridUnitType.Star)
                    : new GridLength(0);
        }
    }

    /// <summary>
    /// Обработчик смены активной вкладки терминала: синхронизирует вкладки, выбирает активную
    /// и запускает shell для новой вкладки после прохождения layout pass.
    /// Handles active terminal tab change: synchronizes tabs, selects the active one,
    /// and starts the shell for the new tab after the layout pass.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные события изменения свойства. / Property changed event data.</param>
    private void OnActiveTerminalTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ActiveTerminalTab)) return;
        SyncTerminalTabs();
        SelectActiveTerminalTab();
        // Запускаем shell для новой вкладки после прохождения layout pass
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, EnsureTerminalShellsStarted);
    }

    /// <summary>
    /// Обработчик изменения коллекции TerminalTabs (добавление/удаление): синхронизирует вкладки TabControl.
    /// Handles TerminalTabs collection changes (add/remove): synchronizes TabControl tabs.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные события изменения коллекции. / Notify collection changed event data.</param>
    private void OnTerminalTabsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        SyncTerminalTabs();
    }

    /// <summary>
    /// Синхронизирует TabControl с коллекцией TerminalTabs: создаёт новые вкладки, удаляет отсутствующие,
    /// устанавливает обработчики HostReady и SizeChanged.
    /// Synchronizes the TabControl with the TerminalTabs collection: creates new tabs, removes missing ones,
    /// sets up HostReady and SizeChanged handlers.
    /// </summary>
    private void SyncTerminalTabs()
    {
        if (_vm is null) return;

        var currentIds = new HashSet<int>(_vm.TerminalTabs.Select(t => t.Id));

        // Удаляем вкладки, которых больше нет
        var toRemove = _terminalHosts.Keys.Where(id => !currentIds.Contains(id)).ToList();
        foreach (var id in toRemove)
            RemoveTerminalTab(id);

        // Создаём новые вкладки
        foreach (var tab in _vm.TerminalTabs)
        {
            if (_terminalHosts.ContainsKey(tab.Id)) continue;

            var host = new TerminalHost();
            var border = new Border { Margin = new Thickness(4), Background = Brushes.Black, Child = host };

            var ti = new TabItem { Content = border, Header = CreateTabHeader(tab) };
            ti.SetResourceReference(StyleProperty, typeof(TabItem));

            // Fallback: если BuildWindowCore уже вызван до подписки на событие
            host.HostReady += hwnd =>
            {
                if (hwnd != IntPtr.Zero && _vm != null && !tab.IsShellStarting && 
                    !_vm.TerminalServices.ContainsKey(tab.Id))
                {
                    tab.IsShellStarting = true;
                    _ = StartShellForTabAsync(tab, hwnd);
                }
            };

            // Подписка на изменение размера для корректного ресайза
            border.SizeChanged += (_, e) =>
            {
                if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
                    _vm?.ResizeTerminal(tab.Id, (int)e.NewSize.Width, (int)e.NewSize.Height);
            };

            InnerTerminalTabs.Items.Add(ti);
            _terminalHosts[tab.Id] = host;

            // Fallback: если хост уже создан, но сервис ещё не запущен
            if (host.HostHandle != IntPtr.Zero && _vm != null && !tab.IsShellStarting && 
                !_vm.TerminalServices.ContainsKey(tab.Id))
            {
                tab.IsShellStarting = true;
                _ = StartShellForTabAsync(tab, host.HostHandle);
            }
        }

        SelectActiveTerminalTab();
    }

    /// <summary>
    /// Запускает shell для всех вкладок терминала, у которых ещё нет сервиса.
    /// Вызывается после прохождения layout-pass, когда HwndHost гарантированно построен.
    /// Starts a shell for all terminal tabs that do not yet have a service.
    /// Called after the layout pass completes, when HwndHost is guaranteed to be built.
    /// </summary>
    private void EnsureTerminalShellsStarted()
    {
        if (_vm is null) return;
        LogService.Debug("EnsureTerminalShellsStarted: checking terminals", "Terminal");

        foreach (var tab in _vm.TerminalTabs)
        {
            if (_terminalHosts.TryGetValue(tab.Id, out var host) && 
                host.HostHandle != IntPtr.Zero && 
                !tab.IsShellStarting && 
                !_vm.TerminalServices.ContainsKey(tab.Id))
            {
                LogService.Debug($"EnsureTerminalShellsStarted: starting shell for tab {tab.Id}", "Terminal");
                tab.IsShellStarting = true;
                _ = StartShellForTabAsync(tab, host.HostHandle);
            }
        }
    }

    /// <summary>
    /// Создаёт заголовок вкладки терминала с названием и кнопкой закрытия.
    /// Creates a terminal tab header with a title and a close button.
    /// </summary>
    /// <param name="tab">ViewModel вкладки терминала. / Terminal tab ViewModel.</param>
    /// <returns>UI-элемент заголовка (DockPanel с кнопкой и текстом). / Header UI element (DockPanel with button and text).</returns>
    private static UIElement CreateTabHeader(TerminalTabViewModel tab)
    {
        var dp = new DockPanel();
        var closeBtn = new Button
        {
            Content = "\u2715",
            FontSize = 10,
            Padding = new Thickness(3, 1, 3, 1),
            Margin = new Thickness(4, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = "Закрыть вкладку"
        };
        // Ссылка на VM через WeakReference
        var vmRef = new WeakReference<MainViewModel>((MainViewModel)Application.Current.MainWindow!.DataContext);
        closeBtn.Click += (_, _) =>
        {
            if (vmRef.TryGetTarget(out var vm))
                vm.CloseTerminalTabCommand.Execute(tab);
        };
        DockPanel.SetDock(closeBtn, Dock.Right);
        dp.Children.Add(closeBtn);
        dp.Children.Add(new TextBlock
        {
            Text = tab.Title,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Foreground = (Brush)(Application.Current.Resources["FgLightBrush"] ?? Brushes.White)
        });
        return dp;
    }

    /// <summary>
    /// Удаляет вкладку терминала по ID: удаляет TabItem, освобождает TerminalHost и удаляет из словаря.
    /// Removes a terminal tab by ID: removes the TabItem, disposes TerminalHost, and deletes from dictionary.
    /// </summary>
    /// <param name="tabId">Идентификатор вкладки. / Tab identifier.</param>
    private void RemoveTerminalTab(int tabId)
    {
        if (!_terminalHosts.TryGetValue(tabId, out var host)) return;

        var ti = FindTabItem(host);
        if (ti != null) InnerTerminalTabs.Items.Remove(ti);
        host.Dispose();
        _terminalHosts.Remove(tabId);
    }

    /// <summary>
    /// Асинхронно запускает shell в контейнере терминала и передаёт фокус консоли.
    /// Asynchronously starts a shell in the terminal container and passes focus to the console.
    /// </summary>
    /// <param name="tab">ViewModel вкладки терминала. / Terminal tab ViewModel.</param>
    /// <param name="hostHwnd">Дескриптор HWND-контейнера (HwndHost). / Container window handle (HwndHost).</param>
    private async Task StartShellForTabAsync(TerminalTabViewModel tab, IntPtr hostHwnd)
    {
        if (_vm is null) return;
        LogService.Debug($"StartShellForTabAsync: tab={tab.Id}, hwnd={hostHwnd:X}", "Terminal");
        try
        {
            await _vm.StartTerminalForTabAsync(tab, hostHwnd);
            LogService.Debug($"StartShellForTabAsync: success for tab={tab.Id}", "Terminal");
            // Передаём фокус в консоль после запуска
            if (_vm.TerminalServices.TryGetValue(tab.Id, out var svc))
                svc.Focus();
        }
        catch (Exception ex)
        {
            LogService.Error($"Ошибка запуска терминала", "Terminal", ex);
            _vm.StatusText = $"Терминал не запущен: {ex.Message}";
        }
        finally
        {
            tab.IsShellStarting = false;
        }
    }

    /// <summary>
    /// Выбирает активную вкладку в TabControl на основе ActiveTerminalTab из ViewModel.
    /// Selects the active terminal tab in the TabControl based on ActiveTerminalTab from ViewModel.
    /// </summary>
    private void SelectActiveTerminalTab()
    {
        if (_vm?.ActiveTerminalTab is null) return;
        if (_terminalHosts.TryGetValue(_vm.ActiveTerminalTab.Id, out var host))
        {
            var ti = FindTabItem(host);
            if (ti != null) InnerTerminalTabs.SelectedItem = ti;
        }
    }

    /// <summary>
    /// Ищет TabItem, содержащий указанный TerminalHost, перебирая элементы InnerTerminalTabs.
    /// Finds the TabItem containing the specified TerminalHost by iterating over InnerTerminalTabs items.
    /// </summary>
    /// <param name="host">Экземпляр TerminalHost. / The TerminalHost to find.</param>
    /// <returns>TabItem, содержащий host, или null, если не найден. / TabItem containing host, or null if not found.</returns>
    private TabItem? FindTabItem(TerminalHost host)
    {
        foreach (var item in InnerTerminalTabs.Items)
        {
            if (item is TabItem ti && ti.Content is Border b && b.Child == host)
                return ti;
        }
        return null;
    }

    //========== Toolbar-обработчики ======================

    /// <summary>
    /// Обработчик клика по палитре команд: закрывает палитру.
    /// Handles click on the command palette overlay: closes the palette.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные события мыши. / Mouse event data.</param>
    private void CommandPaletteOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _vm?.CloseCommandPaletteCommand.Execute(null);
    }

    /// <summary>
    /// Обработчик кнопки «Закладки»: показывает контекстное меню со списком закладок.
    /// Handles the "Bookmarks" button: shows a context menu with the list of bookmarks.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные маршрутизованного события. / Routed event data.</param>
    private void Bookmarks_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_vm == null) return;
        var menu = new ContextMenu { PlacementTarget = sender as UIElement };
        foreach (var bm in _vm.Bookmarks)
        {
            var item = new System.Windows.Controls.MenuItem { Header = bm.Name };
            var path = bm.Path;
            item.Click += (_, _) => _vm.GoToBookmarkCommand.Execute(path);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>
    /// Закрывает выпадающую панель терминала.
    /// Closes the terminal drop-down panel.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные маршрутизованного события. / Routed event data.</param>
    private void CloseTerminalPanel(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.IsTerminalPanelVisible = false;
    }

    /// <summary>
    /// Кнопка мыши средней кнопкой на вкладке терминала: закрывает эту вкладку (ph6.4).
    /// Middle mouse button on terminal tab: closes that tab (ph6.4).
    /// </summary>
    private void TerminalTabControl_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton != MouseButtonState.Pressed || _vm is null) return;

        // Определяем TabItem для нажатой кнопкой мыши
        var hit = e.OriginalSource as DependencyObject;
        var tabItem = FindVisualParent<System.Windows.Controls.TabItem>(hit);
        if (tabItem is null) return;

        // Получаем TerminalTabViewModel из TerminalHost внутри TabItem
        var host = FindVisualChild<TerminalHost>(tabItem);
        if (host is null) return;

        var tabId = _terminalHosts.FirstOrDefault(x => x.Value == host).Key;
        if (tabId == 0) return;

        var tab = _vm.TerminalTabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is not null)
            _vm.CloseTerminalTabCommand.Execute(tab);

        e.Handled = true;
    }

    /// <summary>Хелпер поиска предка определённого типа в визуальном дереве (рекурсия).</summary>
    private static T? FindVisualParent<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T found) return found;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    /// <summary>Хелпер поиска дочернего элемента определённого типа в визуальном дереве (рекурсия).</summary>
    private static T? FindVisualChild<T>(DependencyObject? current) where T : DependencyObject
    {
        if (current is null) return null;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(current); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(current, i);
            if (child is T found) return found;
            var result = FindVisualChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    /// <summary>
    /// Создаёт новую вкладку терминала CMD.
    /// Creates a new CMD terminal tab.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные маршрутизованного события. / Routed event data.</param>
    private void NewCmdTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.NewTerminalTabWithShell("cmd");
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, EnsureTerminalShellsStarted);
    }

    /// <summary>
    /// Создаёт новую вкладку терминала PowerShell.
    /// Creates a new PowerShell terminal tab.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные маршрутизованного события. / Routed event data.</param>
    private void NewPowerShellTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.NewTerminalTabWithShell("powershell");
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, EnsureTerminalShellsStarted);
    }

    /// <summary>
    /// Создаёт новую вкладку терминала PowerShell Core (pwsh).
    /// Creates a new PowerShell Core (pwsh) terminal tab.
    /// </summary>
    private void NewPwshTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.NewTerminalTabWithShell("pwsh");
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, EnsureTerminalShellsStarted);
    }

    /// <summary>
    /// Обработчик двойного клика по команде в палитре: выполняет команду.
    /// Handles double-click on a quick command in the list: runs the command.
    /// </summary>
    /// <param name="sender">Источник события (ListBox). / Event source (ListBox).</param>
    /// <param name="e">Данные события мыши. / Mouse event data.</param>
    private void Cmd_Dbl(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is QuickCommand cmd && _vm is not null)
            _ = _vm.RunQuickCommandAsync(cmd);
    }

    /// <summary>
    /// Обработчик пункта «Выход»: закрывает главное окно (горячая клавиша F10 или Alt+F4).
    /// Handles the "Exit" menu item: closes the main window (hotkey F10 or Alt+F4).
    /// </summary>
    /// <param name="s">Источник события. / Event source.</param>
    /// <param name="e">Данные маршрутизованного события. / Routed event data.</param>
    private void Exit_Click(object s, RoutedEventArgs e) => Close();

    /// <summary>
    /// Обработчик пункта «Настройки»: открывает окно настроек (горячая клавиша Ctrl+,).
    /// Handles the "Settings" menu item: opens the settings window (hotkey Ctrl+,).
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные маршрутизованного события. / Routed event data.</param>
        /// <summary>
        /// Обработчик открытия меню «Закладки»: динамически заполняет закладки из BookmarkService.
        /// Handles "Bookmarks" menu open: dynamically populates bookmarks from BookmarkService.
        /// </summary>
    private void BookmarksMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;

        // Ищем MenuItem «Закладки» по заголовку.
        // Find "Bookmarks" MenuItem by header.
        var bookmarksMenuItem = FindBookmarksMenuItem();
        if (bookmarksMenuItem is null) return;

        // Удаляем все элементы после первых двух (Добавить, Управление + разделитель).
        // Remove all items after the first two (Add, Manage + separator).
        while (bookmarksMenuItem.Items.Count > 3)
            bookmarksMenuItem.Items.RemoveAt(bookmarksMenuItem.Items.Count - 1);

        // Добавляем закладки (не более 20).
        var bms = CoderCommander.Services.BookmarkService.Current.Bookmarks;
        for (int i = 0; i < Math.Min(bms.Count, 20); i++)
        {
            var bm = bms[i];
            var item = new System.Windows.Controls.MenuItem
            {
                Header = bm.Name,
                ToolTip = bm.Path,
                InputGestureText = bm.Path
            };
            var path = bm.Path;
            item.Click += (_, _) => _vm.NavigateToBookmarkCommand.Execute(path);
            bookmarksMenuItem.Items.Add(item);
        }
    }

    /// <summary>
    /// Ищет MenuItem «Закладки» в главном меню.
    /// Finds "Bookmarks" MenuItem in the main menu.
    /// </summary>
    private System.Windows.Controls.MenuItem? FindBookmarksMenuItem()
    {
        foreach (var item in MainMenu.Items)
        {
            if (item is System.Windows.Controls.MenuItem mi &&
                mi.Header is string s &&
                s == CoderCommander.Services.LocalizationService.Current.GetString("Menu.Bookmarks"))
                return mi;
        }
        return null;
    }
private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _vm?.OpenSettingsCommand.Execute(null);
    }

//========== Обработка окна редактора/просмотрщика (F3/F4) ==========

/// <summary>
/// Обработчик пункта «Настроить колонки»: открывает диалог настроек колонок.
/// Handles "Configure columns?" menu item: opens column settings dialog.
/// </summary>
private void Columns_Click(object sender, RoutedEventArgs e)
{
    try
    {
        var w = new ColumnSettingsWindow { Owner = this };
        ShowDialogWithFocus(w);
    }
    catch (Exception ex)
    {
        LogService.Error($"Ошибка открытия настроек колонок: {ex.Message}", "Columns", ex);
    }
}

//========== Обработка окна редактора/просмотрщика (F3/F4) ==========

    /// <summary>
    /// Открывает модальное окно редактора (F4) для редактирования файла.
    /// Opens a modal editor window (F4) for editing a file.
    /// </summary>
    /// <param name="path">Путь к файлу. / Path to the file.</param>
    /// <param name="content">Содержимое файла. / File content.</param>
    private void OpenEditor(string path, string content)
    {
        try
        {
            var editorWindow = new EditorWindow(path, content, isReadOnly: false)
            {
                Owner = this
            };
            ShowDialogWithFocus(editorWindow);
        }
        catch (Exception ex)
        {
            LogService.Error(string.Format(LocalizationService.Current.GetString("Error.OpenEditor"), ex.Message), "Editor", ex);
            StyledMessageBoxWindow.Show(string.Format(LocalizationService.Current.GetString("Error.OpenEditor"), ex.Message), LocalizationService.Current.GetString("Error.Title"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Открывает модальное окно просмотрщика (F3) для чтения файла (только чтение).
    /// Opens a modal viewer window (F3) for reading a file (read-only).
    /// </summary>
    /// <param name="path">Путь к файлу. / Path to the file.</param>
    /// <param name="content">Содержимое файла. / File content.</param>
    private void OpenViewer(string path, string content)
    {
        try
        {
            var viewerWindow = new EditorWindow(path, content, isReadOnly: true)
            {
                Owner = this
            };
            ShowDialogWithFocus(viewerWindow);
        }
        catch (Exception ex)
        {
        LogService.Error(string.Format(LocalizationService.Current.GetString("Error.OpenViewer"), ex.Message), "Viewer", ex);
        StyledMessageBoxWindow.Show(string.Format(LocalizationService.Current.GetString("Error.OpenViewer"), ex.Message), LocalizationService.Current.GetString("Error.Title"),
                        MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

    /// <summary>
    /// Открывает модальное окно поиска дубликатов (ph2.4) для указанной папки.
    /// Opens a modal duplicate search window (ph2.4) for the given folder.
    /// </summary>
    /// <param name="path">Стартовая директория поиска. / Start folder for the search.</param>
    private void OpenDuplicates(string path)
    {
        try
        {
            var w = new DuplicatesWindow(path) { Owner = this };
            ShowDialogWithFocus(w);
        }
        catch (Exception ex)
        {
            LogService.Error(string.Format(LocalizationService.Current.GetString("Error.OpenDuplicates"), ex.Message), "Duplicates", ex);
            StyledMessageBoxWindow.Show(string.Format(LocalizationService.Current.GetString("Error.OpenDuplicates"), ex.Message), LocalizationService.Current.GetString("Error.Title"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

/// Открывает модальное окно синхронизации папок (ph3.3) для двух папок из выделенных путей.
/// Opens modal folder synchronization window (ph3.3) for two folders from selected paths.
/// </summary>
/// <param name="left">Левая папка (из левой панели). / Left folder (from the left panel).</param>
/// <param name="right">Правая папка (из правой панели). / Right folder (from the right panel).</param>
/// <param name="selected">Выделенные пути для фильтра «только выделенные» (или null). / Selected paths for "only selected" filter (or null).</param>
private void OpenSyncDirs(string left, string right, System.Collections.Generic.IReadOnlyList<string>? selected)
{
    try
    {
        var w = new SyncDirsWindow(left, right, selected, () => _vm?.RefreshAllCommand?.Execute(null))
        {
            Owner = this
        };
        ShowDialogWithFocus(w);
    }
    catch (System.Exception ex)
    {
        LogService.Error(string.Format(LocalizationService.Current.GetString("Error.OpenSync"), ex.Message), "SyncDirs", ex);
        StyledMessageBoxWindow.Show(string.Format(LocalizationService.Current.GetString("Error.OpenEditor"), ex.Message), LocalizationService.Current.GetString("Error.Title"),
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

/// <summary>
/// Открывает модальное окно очереди операций (ph5.2).
/// Opens modal operation queue window (ph5.2).
/// </summary>
private void OpenOperationQueue()
{
    try
    {
        var w = new OperationQueueWindow
        {
            Owner = this
        };
        ShowDialogWithFocus(w);
    }
    catch (Exception ex)
    {
        LogService.Error(string.Format(LocalizationService.Current.GetString("Error.OpenQueue"), ex.Message), "OperationQueue", ex);
        StyledMessageBoxWindow.Show(string.Format(LocalizationService.Current.GetString("Error.OpenEditor"), ex.Message), LocalizationService.Current.GetString("Error.Title"),
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

/// <summary>
/// Открывает модальное окно управления закладками (ph5.3).
/// Opens modal bookmarks management window (ph5.3).
/// </summary>
private void OpenBookmarks()
{
    try
    {
        var w = new BookmarksWindow
        {
            Owner = this
        };
        ShowDialogWithFocus(w);
    }
    catch (Exception ex)
    {
        LogService.Error(string.Format(LocalizationService.Current.GetString("Error.OpenBookmarks"), ex.Message), "Bookmarks", ex);
        StyledMessageBoxWindow.Show(string.Format(LocalizationService.Current.GetString("Error.OpenEditor"), ex.Message), LocalizationService.Current.GetString("Error.Title"),
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

/// <summary>
/// Открывает модальное окно управления архивами (ph5.1).
/// Opens modal archive management window (ph5.1).
/// </summary>
/// <param name="mode">Режим: "create" или "extract".</param>
/// <param name="files">Список файлов для архивации (режим create).</param>
/// <param name="archivePath">Путь к архиву (режим extract).</param>
private void OpenArchive(string mode, System.Collections.Generic.IReadOnlyList<string>? files, string? archivePath)
{
    try
    {
        var w = new ArchiveWindow(mode, files, archivePath)
        {
            Owner = this
        };
        ShowDialogWithFocus(w);
    }
    catch (System.Exception ex)
    {
        LogService.Error(string.Format(LocalizationService.Current.GetString("Error.OpenArchive"), ex.Message), "Archive", ex);
        StyledMessageBoxWindow.Show(string.Format(LocalizationService.Current.GetString("Error.OpenEditor"), ex.Message), LocalizationService.Current.GetString("Error.Title"),
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

    //========== Вспомогательные методы открытия окон ==========

/// <summary>
/// Открывает модальное окно дерева каталогов (ph5.6) для указанной панели.
/// Opens the modal directory tree window (ph5.6) for the specified panel.
/// </summary>
/// <param name="initialPath">Начальный путь (текущая папка панели). / Initial path (panel's current folder).</param>
/// <param name="targetPanel">Целевая панель для навигации. / Target panel for navigation.</param>
private void OpenDirectoryTree(string initialPath, PanelViewModel targetPanel)
{
    try
    {
        var w = new DirectoryTreeWindow(initialPath, targetPanel)
        {
            Owner = this
        };
        ShowDialogWithFocus(w);
    }
    catch (Exception ex)
    {
        LogService.Error(string.Format(LocalizationService.Current.GetString("Error.OpenTree"), ex.Message), "DirectoryTree", ex);
        StyledMessageBoxWindow.Show(string.Format(LocalizationService.Current.GetString("Error.OpenEditor"), ex.Message), LocalizationService.Current.GetString("Error.Title"),
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

    /// <summary>
    /// Обработчик средней кнопки мыши на TabControl панели: закрывает нажатую вкладку.
    /// Handles middle mouse button on panel TabControl: closes the clicked tab.
    /// </summary>
    /// <param name="sender">TabControl (LeftPanelTabControl или RightPanelTabControl).</param>
    /// <param name="e">Данные события мыши. / Mouse event data.</param>
    private void TabControl_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || e.MiddleButton != MouseButtonState.Pressed)
            return;

        // Ищем TabItem под курсором
        // Find the TabItem under the cursor
        var tabControl = sender as TabControl;
        if (tabControl is null || _vm is null) return;

        var pos = e.GetPosition(tabControl);
        var tabItem = FindTabItemAt(tabControl, pos);
        if (tabItem is null) return;

        var tabVM = tabItem.DataContext as TabViewModel;
        if (tabVM is null) return;

        // Определяем, какой это TabControl (левый или правый) и закрываем вкладку
        // Determine which TabControl (left or right) and close the tab
        e.Handled = true;
        if (tabControl == LeftPanelTabControl)
            _vm.CloseLeftTabCommand.Execute(tabVM);
        else if (tabControl == RightPanelTabControl)
            _vm.CloseRightTabCommand.Execute(tabVM);
    }

    /// <summary>
    /// Ищет TabItem по указанной позиции в TabControl.
    /// Finds the TabItem at the given position within the TabControl.
    /// </summary>
    private static TabItem? FindTabItemAt(TabControl tabControl, System.Windows.Point position)
    {
        // Используем VisualTreeHelper для поиска TabItem по позиции
        // Use VisualTreeHelper to find the TabItem at the position
        var hit = System.Windows.Media.VisualTreeHelper.HitTest(tabControl, position);
        if (hit?.VisualHit is null) return null;

        // Поднимаемся по визуальному дереву в поисках первого TabItem
        // Walk up the visual tree to find the first TabItem
        var depObj = hit.VisualHit;
        while (depObj is not null && depObj is not TabItem)
            depObj = System.Windows.Media.VisualTreeHelper.GetParent(depObj);

        return depObj as TabItem;
    }

    /// <summary>
    /// Обработчик нажатия левой кнопки мыши на заголовке окна: перетаскивание или двойной клик для максимизации/восстановления.
    /// Handles left mouse button press on the window title bar: drag-move or double-click to maximize/restore.
    /// </summary>
/// <param name="sender">Источник события. / Event source.</param>
/// <param name="e">Данные события мыши. / Mouse event data.</param>
private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
{
    if (e.ClickCount == 2)
    {
        MaximizeRestoreWindow(sender, null!);
    }
    else
    {
        DragMove();
    }
}

/// <summary>
/// Сворачивает главное окно в панель задач.
/// Minimizes the main window to the taskbar.
/// </summary>
/// <param name="sender">Источник события. / Event source.</param>
/// <param name="e">Данные маршрутизованного события. / Routed event data.</param>
private void MinimizeWindow(object sender, RoutedEventArgs e)
{
    WindowState = WindowState.Minimized;
}

/// <summary>
/// Переключает главное окно между развёрнутым и нормальным состоянием. Обновляет иконку кнопки максимизации.
/// Toggles the main window between maximized and normal state. Updates the maximize button icon.
/// </summary>
/// <param name="sender">Источник события. / Event source.</param>
/// <param name="e">Данные маршрутизованного события. / Routed event data.</param>
private void MaximizeRestoreWindow(object sender, RoutedEventArgs e)
{
    if (WindowState == WindowState.Maximized)
    {
        WindowState = WindowState.Normal;
        if (MaximizeIcon != null)
            MaximizeIcon.Text = "\uE922"; // ChromeMaximize
    }
    else
    {
        WindowState = WindowState.Maximized;
        if (MaximizeIcon != null)
            MaximizeIcon.Text = "\uE923"; // ChromeRestore
    }
}

/// <summary>
/// Закрывает главное окно приложения.
/// Closes the main application window.
/// </summary>
/// <param name="sender">Источник события. / Event source.</param>
/// <param name="e">Данные маршрутизованного события. / Routed event data.</param>
private void CloseWindow(object sender, RoutedEventArgs e)
{
    Close();
}

// ===========================================
// Динамические горячие клавиши (ph6.1) / DYNAMIC HOTKEYS (ph6.1)
// ===========================================

/// <summary>
/// Множество action ID панельных горячих клавиш (для удаления старых InputBindings).
/// Set of panel hotkey action IDs (for removing old InputBindings).
/// </summary>
private static readonly HashSet<string> _panelActions = new()
{
    "File.Rename", "File.View", "File.Edit", "File.Copy", "File.Move",
    "File.CreateFolder", "File.Delete", "File.Search",
    "Panel.DirectoryTreeLeft", "Panel.DirectoryTreeRight"
};

/// <summary>
/// Активные InputBindings для горячих клавиш макросов (для очистки при пересоздании).
/// Active InputBindings for macro hotkeys (for cleanup on re-creation).
/// </summary>
private readonly List<InputBinding> _macroBindings = new();

/// <summary>
/// Пересоздаёт InputBindings из текущих настроек горячих клавиш.
/// Removes old panel InputBindings and creates new ones from settings.
/// Также привязывает горячие клавиши макросов (ph8.2).
/// Also binds macro hotkeys (ph8.2).
/// </summary>
public void ApplyHotkeys()
{
    if (_vm is null) return;

    // Удаляем старые панельные InputBindings (оставляем app-level: F10, Ctrl+S, Shift+F10, Ctrl+, и т.д.)
    // Remove ONLY panel InputBindings (keep app-level: F10, Ctrl+S, Shift+F10, Ctrl+, etc.)
    var toRemove = InputBindings.Cast<InputBinding>()
        .Where(b => b is KeyBinding kb && kb.Command is not null && IsPanelCommand(kb.Command))
        .ToList();
    foreach (var kb in toRemove) InputBindings.Remove(kb);

    var hotkeys = SettingsService.GetEffectiveHotkeys();
    foreach (var hk in hotkeys)
    {
        if (string.IsNullOrEmpty(hk.Key)) continue;
        if (!Enum.TryParse<Key>(hk.Key, out var key)) continue;
        var mods = ParseModifiers(hk.Modifiers);
        var cmd = ResolveCommand(hk.Action);
        if (cmd is not null)
            InputBindings.Add(new KeyBinding(cmd, key, mods));
    }

    // Удаляем старые привязки макросов / Remove old macro bindings
    foreach (var mb in _macroBindings) InputBindings.Remove(mb);
    _macroBindings.Clear();

    // Привязываем горячие клавиши макросов (ph8.2) / Bind macro hotkeys (ph8.2)
    var macroService = MacroService.Current;
    var engine = new CommandEngine();
    // Регистрируем основные команды для исполнения макросов
    // Register main commands for macro execution
    engine.Register(new QuickCommand("app.copy", "Copy", ct => System.Threading.Tasks.Task.FromResult("copy")));
    engine.Register(new QuickCommand("app.move", "Move", ct => System.Threading.Tasks.Task.FromResult("move")));
    engine.Register(new QuickCommand("app.delete", "Delete", ct => System.Threading.Tasks.Task.FromResult("delete")));
    engine.Register(new QuickCommand("app.rename", "Rename", ct => System.Threading.Tasks.Task.FromResult("rename")));
    engine.Register(new QuickCommand("app.refresh", "Refresh", ct => System.Threading.Tasks.Task.FromResult("refresh")));

    var macroExecutor = new MacroExecutor(engine);
    foreach (var macro in macroService.GetAll())
    {
        if (!macro.IsEnabled || string.IsNullOrWhiteSpace(macro.Hotkey)) continue;
        var parts = macro.Hotkey.Split('+');
        if (parts.Length == 0) continue;
        var keyStr = parts[^1].Trim();
        if (!Enum.TryParse<Key>(keyStr, out var mkey)) continue;
        var mmods = ParseModifiers(string.Join("+", parts[..^1]));
        var macroCmd = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(
            async () =>
            {
                var result = await macroExecutor.ExecuteAsync(macro);
                if (_vm is not null) _vm.StatusText = result;
            });
        var binding = new KeyBinding(macroCmd, mkey, mmods);
        InputBindings.Add(binding);
        _macroBindings.Add(binding);
    }
}

/// <summary>
/// Проверяет, является ли команд панельной (привязан к action ID из _panelActions).
/// Checks if the command is a panel command (bound to an action ID from _panelActions).
/// </summary>
private static bool IsPanelCommand(ICommand command)
{
    foreach (var action in _panelActions)
    {
        var cmd = ResolveCommand(action);
        if (cmd is not null && cmd == command) return true;
    }
    return false;
}

/// <summary>
/// Парсит строку модификаторов ("Ctrl+Alt") в ModifierKeys.
/// Parses a modifier string ("Ctrl+Alt") to ModifierKeys.
/// </summary>
private static ModifierKeys ParseModifiers(string mods)
{
    var result = ModifierKeys.None;
    if (string.IsNullOrEmpty(mods)) return result;
    foreach (var part in mods.Split('+'))
    {
        result |= part.Trim() switch
        {
            "Ctrl" => ModifierKeys.Control,
            "Alt" => ModifierKeys.Alt,
            "Shift" => ModifierKeys.Shift,
            _ => ModifierKeys.None
        };
    }
    return result;
}

/// <summary>
/// Резолвит action ID в ICommand из MainViewModel.
/// Resolves an action ID to an ICommand from MainViewModel.
/// </summary>
private static System.Windows.Input.ICommand? ResolveCommand(string action)
{
    if (Application.Current.MainWindow?.DataContext is not MainViewModel vm) return null;
    return action switch
    {
        "File.Rename" => vm.RenameCommand,
        "File.View" => vm.ViewFileCommand,
        "File.Edit" => vm.EditFileCommand,
        "File.Copy" => vm.CopyCommand,
        "File.Move" => vm.MoveCommand,
        "File.CreateFolder" => vm.CreateFolderCommand,
        "File.Delete" => vm.DeleteCommand,
        "File.Search" => vm.SearchFilesCommand,
        "Panel.DirectoryTreeLeft" => vm.OpenDirectoryTreeLeftCommand,
        "Panel.DirectoryTreeRight" => vm.OpenDirectoryTreeRightCommand,
        _ => null
    };
}

/// <summary>
/// Пере-применяет горячие клавиши (вызывается из SettingsWindow после сохранения).
/// Reapplies hotkeys (called from SettingsWindow after saving).
/// </summary>
internal void ReapplyHotkeys()
{
    ApplyHotkeys();
}
}

