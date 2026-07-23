using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using CoderCommander.Services;
using CoderCommander.ViewModels;
using ColumnDef = CoderCommander.Models.ColumnDefinition;

namespace CoderCommander.Views;

/// <summary>
/// Панель файловой системы: навигация по каталогам, фильтр, выделение, контекстные клавиши, динамические колонки.
/// File system panel: directory navigation, filter, selection, context hotkeys, dynamic columns.
/// </summary>
public partial class FilePanel : UserControl
{
    /// <summary>
    /// Направления сортировки колонок (избыточно, хранится в ColumnDefinition; оставлен для обратной совместимости).
    /// Column sort directions (redundant — stored in ColumnDefinition; kept for backward compat).
    /// </summary>
    private readonly Dictionary<string, ListSortDirection> _sortDirections = new();

    /// <summary>
    /// Символы-стрелки для индикации направления сортировки.
    /// Arrow characters for sort direction indicator.
    /// </summary>
    private const string SortAscArrow = " ▲";
    private const string SortDescArrow = " ▼";

    // FIXED: Store handler reference to prevent memory leak from static event subscription.
    // Each FilePanel instance subscribed to static ColumnConfigService.ColumnsChanged via lambda,
    // which captured the instance and prevented GC.
    private readonly EventHandler? _columnsChangedHandler;

    /// <summary>
    /// Конструктор: инициализация XAML, подписка на события фокуса, DataContext, быстрый фильтр, колонки.
    /// Constructor: XAML init, subscribe focus/DataContext/quick filter/column change events.
    /// </summary>
    public FilePanel()
    {
        InitializeComponent();
        GotFocus += FilePanel_GotFocus;
        DataContextChanged += FilePanel_DataContextChanged;

        // Подписка на события быстрого фильтра/поиска (ph1.1)
        FileList.PreviewKeyDown += FileList_PreviewKeyDown;
        FileList.KeyDown += QuickSearch_KeyDown;
        FileList.TextInput += QuickSearch_TextInput;
        FilterInput.KeyDown += FilterInput_KeyDown;

        // Динамические колонки: загрузка конфигурации и подписка на изменения
        _columnsChangedHandler = (_, _) => RebuildColumns();
        ColumnConfigService.ColumnsChanged += _columnsChangedHandler;
        ColumnConfigService.Load();
        Loaded += (_, _) => RebuildColumns();
        // FIXED: Unsubscribe from static event on Unloaded to prevent memory leak.
        Unloaded += (_, _) =>
        {
            if (_columnsChangedHandler is not null)
                ColumnConfigService.ColumnsChanged -= _columnsChangedHandler;
        };
    }

    // ═══════════════════════════════════════════════
    // ОБРАБОТЧИКИ СОБЫТИЙ / EVENT HANDLERS
    // ═══════════════════════════════════════════════

    /// <summary>
    /// Обработчик смены DataContext: отписывается от старой ViewModel, подписывается на новую.
    /// </summary>
    private void FilePanel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PanelViewModel oldVm) oldVm.PropertyChanged -= Vm_PropertyChanged;
        if (e.NewValue is PanelViewModel newVm) newVm.PropertyChanged += Vm_PropertyChanged;
    }

    /// <summary>
    /// Обработчик изменения свойств ViewModel: при активации панели переводит фокус на FileList.
    /// </summary>
    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelViewModel.IsActive)
            && DataContext is PanelViewModel vm && vm.IsActive)
        {
            if (!IsKeyboardFocusWithin)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!IsKeyboardFocusWithin) FileList.Focus();
                }));
            }
        }
    }

    /// <summary>
    /// Обработчик получения фокуса панелью: активирует эту панель через MainViewModel.
    /// </summary>
    private void FilePanel_GotFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is PanelViewModel vm && !vm.IsActive
            && Application.Current.MainWindow?.DataContext is MainViewModel mvm)
        {
            mvm.ActivatePanelCommand.Execute(vm);
        }
    }

    /// <summary>
    /// Обработчик кнопки очистки фильтра.
    /// </summary>
    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PanelViewModel vm) vm.QuickFilterText = "";
    }

    /// <summary>
    /// Обработчик Enter в адресной строке: навигация.
    /// </summary>
    private void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && DataContext is PanelViewModel vm)
        {
            e.Handled = true;
            _ = vm.NavigateToAsync(tb.Text);
        }
    }

    /// <summary>
    /// Двойной клик: открыть директорию или файл.
    /// </summary>
    private void LB_Dbl(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PanelViewModel vm && vm.SelectedItem is Models.FileSystemItem i)
        {
            if (i.IsDirectory)
                _ = vm.NavigateToAsync(i.IsParent
                    ? vm.GetParentPath()
                    : i.FullPath);
            else if (Application.Current.MainWindow?.DataContext is MainViewModel mvm)
                _ = mvm.OpenItemAsync();
        }
    }

    /// <summary>
    /// Якорь диапазона для Shift+клик и Shift+стрелки (как в Double Commander).
    /// Range anchor for Shift+click and Shift+arrows (like Double Commander).
    /// </summary>
    private int _rangeAnchor = -1;

    /// <summary>
    /// PreviewKeyDown: управление метками и курсором (Ins, Space, *, Shift+стрелки, Ctrl+A/D/I, Gray+/-).
    /// Курсор (SelectedItem) и метки (IsSelected) независимы (архитектура Double Commander).
    /// PreviewKeyDown: mark and cursor management (Ins, Space, *, Shift+arrows, Ctrl+A/D/I, Gray+/-).
    /// Cursor (SelectedItem) and marks (IsSelected) are independent (Double Commander architecture).
    /// </summary>
    private void FileList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PanelViewModel vm || sender is not ListBox lb) return;

        // ── Ins / Space: переключить метку текущего + курсор вниз ──
        // ── Ins / Space: toggle current mark + cursor down ──
        if (e.Key == Key.Insert || (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None))
        {
            e.Handled = true;
            if (vm.SelectedItem is not Models.FileSystemItem cur || cur.IsParent) return;
            cur.IsSelected = !cur.IsSelected;
            MoveCursor(lb, 1);
            return;
        }

        // ── Shift+Up/Down: переключить метку текущего + двигать курсор (DC InvertActiveFile) ──
        // ── Shift+Up/Down: toggle current mark + move cursor (DC InvertActiveFile) ──
        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            if (e.Key == Key.Down)
            {
                e.Handled = true;
                if (vm.SelectedItem is Models.FileSystemItem cur && !cur.IsParent)
                    cur.IsSelected = !cur.IsSelected;
                MoveCursor(lb, 1);
                return;
            }
            if (e.Key == Key.Up)
            {
                e.Handled = true;
                if (vm.SelectedItem is Models.FileSystemItem cur && !cur.IsParent)
                    cur.IsSelected = !cur.IsSelected;
                MoveCursor(lb, -1);
                return;
            }
        }

        // ── * (numpad Multiply или Shift+8): инвертировать все метки ──
        // ── * (numpad Multiply or Shift+8): invert all marks ──
        if (e.Key == Key.Multiply || (e.Key == Key.D8 && Keyboard.Modifiers == ModifierKeys.Shift))
        {
            e.Handled = true;
            foreach (var fi in vm.Items.Where(i => !i.IsParent))
                fi.IsSelected = !fi.IsSelected;
            return;
        }

        // ── Ctrl+A: выделить все ──
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            foreach (var fi in vm.Items.Where(i => !i.IsParent))
                fi.IsSelected = true;
            return;
        }

        // ── Ctrl+D: снять все метки ──
        if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            foreach (var fi in vm.Items)
                fi.IsSelected = false;
            return;
        }

        // ── Ctrl+I: инвертировать все метки ──
        if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            foreach (var fi in vm.Items.Where(i => !i.IsParent))
                fi.IsSelected = !fi.IsSelected;
            return;
        }

        // ── Gray+ / OemPlus: выделить по маске ──
        if ((e.Key == Key.Add || e.Key == Key.OemPlus) && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            if (Application.Current.MainWindow?.DataContext is MainViewModel mvm)
                mvm.SelectByPatternCommand.Execute(null);
            return;
        }

        // ── Gray- / OemMinus: снять по маске ──
        if ((e.Key == Key.Subtract || e.Key == Key.OemMinus) && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            if (Application.Current.MainWindow?.DataContext is MainViewModel mvm)
                mvm.DeselectByPatternCommand.Execute(null);
            return;
        }

        // ── Shift+Gray+ / Shift+OemPlus: выделить по расширению текущего (DC cm_MarkCurrentExtension) ──
        if ((e.Key == Key.Add || e.Key == Key.OemPlus) && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            vm.MarkCurrentExtension(true);
            return;
        }

        // ── Shift+Gray- / Shift+OemMinus: снять по расширению текущего ──
        if ((e.Key == Key.Subtract || e.Key == Key.OemMinus) && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            vm.MarkCurrentExtension(false);
            return;
        }

        // ── Ctrl+\ : перейти к корню диска (DC cm_ChangeDirToRoot) ──
        if (e.Key == Key.OemBackslash && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            _ = vm.GoToRootAsync();
            return;
        }

        // ── Ctrl+PgUp : перейти в родительскую папку (DC cm_ChangeDirToParent) ──
        if (e.Key == Key.Prior && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            _ = vm.GoUpAsync();
            return;
        }

        // ── Ctrl+PgDn : открыть папку/архив под курсором (DC cm_OpenArchive) ──
        if (e.Key == Key.Next && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (vm.SelectedItem is Models.FileSystemItem cur && cur.IsDirectory && !cur.IsParent)
                _ = vm.NavigateToAsync(cur.FullPath);
            return;
        }

        var mods = Keyboard.Modifiers;
        // Обновление status bar после любой операции с метками
        if (e.Handled) vm.RefreshStatusBar();
    }

    /// <summary>
    /// Сдвигает курсор ListBox на offset позиций (±1), скроллит в зону видимости.
    /// Move ListBox cursor by offset positions (±1), scroll into view.
    /// </summary>
    private static void MoveCursor(ListBox lb, int offset)
    {
        var idx = lb.SelectedIndex;
        if (idx < 0) return;
        var newIdx = idx + offset;
        if (newIdx < 0) newIdx = 0;
        if (newIdx >= lb.Items.Count) newIdx = lb.Items.Count - 1;
        if (newIdx != idx)
        {
            lb.SelectedIndex = newIdx;
            lb.ScrollIntoView(lb.Items[newIdx]);
        }
    }

    /// <summary>
    /// Клавиши в списке файлов: Enter, Back, Delete, F2, F5, Ctrl+A + динамические хоткеи.
    /// </summary>
    private void ListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PanelViewModel vm || sender is not ListBox lb) return;

        if (vm.IsQuickSearchActive && (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Enter))
            return;

        // Динамическая проверка панельных хоткеев через настройки (ph6.1)
        // Dynamic panel hotkey check via settings (ph6.1)
        if (TryHandlePanelHotkey(e)) return;

        switch (e.Key)
        {
            case Key.Enter:
                if (Keyboard.Modifiers == ModifierKeys.Shift
                    && vm.SelectedItem is Models.FileSystemItem { IsParent: false, IsDirectory: false }
                    && Application.Current.MainWindow?.DataContext is MainViewModel mvmOw)
                {
                    //Shift+Enter: «Открыть как» (ph5.5)
                    mvmOw.OpenWithCommand.Execute(null);
                    e.Handled = true;
                }
                else if (vm.SelectedItem is Models.FileSystemItem selItem)
                {
                    e.Handled = true;
                    if (selItem.IsDirectory)
                        _ = vm.NavigateToAsync(selItem.IsParent
                            ? vm.GetParentPath()
                            : selItem.FullPath);
                    else if (Application.Current.MainWindow?.DataContext is MainViewModel mvm)
                        _ = mvm.OpenItemAsync();
                }
                break;
            case Key.Back:
                e.Handled = true;
                _ = vm.GoUpAsync();
                break;
            case Key.Delete:
                if (Application.Current.MainWindow?.DataContext is MainViewModel mvmDel)
                {
                    if (Keyboard.Modifiers == ModifierKeys.Shift)
                        mvmDel.WipeCommand.Execute(null);
                    else
                        mvmDel.DeleteCommand.Execute(null);
                }
                e.Handled = true;
                break;
            case Key.Home:
                e.Handled = true;
                if (lb.Items.Count > 0) { lb.SelectedIndex = 0; lb.ScrollIntoView(lb.Items[0]); }
                break;
            case Key.End:
                e.Handled = true;
                if (lb.Items.Count > 0) { lb.SelectedIndex = lb.Items.Count - 1; lb.ScrollIntoView(lb.Items[^1]); }
                break;
        }
        vm.RefreshStatusBar();
    }

    /// <summary>
    /// Пытается обработать нажатие клавиши как панельный хоткей из настроек (ph6.1).
    /// Tries to handle a key press as a panel hotkey from settings (ph6.1).
    /// </summary>
    private static bool TryHandlePanelHotkey(KeyEventArgs e)
    {
        if (Application.Current.MainWindow?.DataContext is not MainViewModel mvm) return false;

        // Для Alt+клавиш WPF присылает e.Key == Key.System, реальная клавиша — в e.SystemKey.
        // For Alt+key combos WPF reports e.Key == Key.System; the real key is in e.SystemKey.
        var pressedKey = e.Key == Key.System ? e.SystemKey : e.Key;

        var hotkeys = SettingsService.GetEffectiveHotkeys();
        foreach (var hk in hotkeys)
        {
            if (string.IsNullOrEmpty(hk.Key)) continue;
            if (!Enum.TryParse<Key>(hk.Key, out var hkKey)) continue;
            if (hkKey != pressedKey) continue;

            var expectedMods = ParseModifiersString(hk.Modifiers);
            if (expectedMods != Keyboard.Modifiers) continue;

            // Нашли совпадение — выполняем команду
            // Found a match — execute the command
            var cmd = ResolvePanelCommand(mvm, hk.Action);
            if (cmd is not null)
            {
                cmd.Execute(null);
                e.Handled = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Парсит строку модификаторов в ModifierKeys.
    /// Parses a modifier string to ModifierKeys.
    /// </summary>
    private static ModifierKeys ParseModifiersString(string mods)
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
    /// Резолвит action ID в ICommand из MainViewModel (только панельные команды).
    /// Resolves an action ID to an ICommand from MainViewModel (panel commands only).
    /// </summary>
    private static System.Windows.Input.ICommand? ResolvePanelCommand(MainViewModel mvm, string action)
    {
        return action switch
        {
            "File.Rename" => mvm.RenameCommand,
            "File.View" => mvm.ViewFileCommand,
            "File.Edit" => mvm.EditFileCommand,
            "File.Copy" => mvm.CopyCommand,
            "File.Move" => mvm.MoveCommand,
            "File.CreateFolder" => mvm.CreateFolderCommand,
            "File.Delete" => mvm.DeleteCommand,
            "File.Search" => mvm.SearchFilesCommand,
            "Panel.DirectoryTreeLeft" => mvm.OpenDirectoryTreeLeftCommand,
            "Panel.DirectoryTreeRight" => mvm.OpenDirectoryTreeRightCommand,
            _ => null
        };
    }

    /// <summary>
    /// Быстрый поиск (ph1.1): Esc, стрелки, Enter.
    /// </summary>
    private void QuickSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PanelViewModel vm) return;
        if (e.Key == Key.Escape)
        {
            vm.ResetQuick();
            e.Handled = true;
            return;
        }
        if (vm.IsQuickSearchActive)
        {
            switch (e.Key)
            {
                case Key.Down: vm.QuickSearchNext(); e.Handled = true; ScrollSelectedIntoView(); break;
                case Key.Up: vm.QuickSearchPrev(); e.Handled = true; ScrollSelectedIntoView(); break;
                case Key.Enter: vm.FinalizeQuickSearch(); e.Handled = true; break;
            }
        }
    }

    /// <summary>
    /// Ввод текста в списке (ph1.1): символы строят строку быстрого поиска.
    /// </summary>
    private void QuickSearch_TextInput(object sender, TextCompositionEventArgs e)
    {
        if (DataContext is not PanelViewModel vm) return;
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        vm.ExtendQuickSearch(e.Text);
        e.Handled = true;
        ScrollSelectedIntoView();
    }

    /// <summary>
    /// Esc в поле фильтра (ph1.1): сброс.
    /// </summary>
    private void FilterInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is PanelViewModel vm)
        {
            vm.ResetQuick();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Циклическое переключение области фильтра (все/файлы/папки).
    /// </summary>
    private void CycleScope_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PanelViewModel vm) vm.CycleScope();
    }

    /// <summary>
    /// Прокрутка к выделенному элементу.
    /// </summary>
    private void ScrollSelectedIntoView()
    {
        if (DataContext is PanelViewModel vm && vm.SelectedItem is not null)
            FileList.ScrollIntoView(vm.SelectedItem);
    }

    // ═══════════════════════════════════════════════
    // КОНТЕКСТНОЕ МЕНЮ ЗАГОЛОВКА / HEADER CONTEXT MENU
    // ═══════════════════════════════════════════════

    /// <summary>
    /// Обработчик «Настроить колонки» из контекстного меню заголовка.
    /// </summary>
    private void ConfigureColumns_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var w = new ColumnSettingsWindow { Owner = Window.GetWindow(this) };
            w.ShowDialog();
        }
        catch (Exception ex)
        {
            LogService.Error(string.Format(LocalizationService.Current.GetString("Error.OpenColumnSettings"), ex.Message), "Columns", ex);
        }
    }

    // ═══════════════════════════════════════════════
    // ДИНАМИЧЕСКИЕ КОЛОНКИ / DYNAMIC COLUMNS
    // ═══════════════════════════════════════════════

    /// <summary>
    /// Пересоздаёт заголовки и шаблон элемента ListBox на основе ActiveColumns.
    /// </summary>
    private void RebuildColumns()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RebuildColumnHeaders();
            RebuildItemTemplate();
            if (DataContext is PanelViewModel vm)
                vm.ApplySortFromConfig();
        }));
    }

    /// <summary>
    /// Генерирует заголовки колонок в ColumnHeadersGrid.
    /// </summary>
    private void RebuildColumnHeaders()
    {
        ColumnHeadersGrid.ColumnDefinitions.Clear();
        ColumnHeadersGrid.Children.Clear();

        var cols = ColumnConfigService.ActiveColumns.Where(c => c.IsVisible).ToList();

        // Колонка иконки (всегда, фикс 24)
        ColumnHeadersGrid.ColumnDefinitions.Add(
            new System.Windows.Controls.ColumnDefinition { Width = new GridLength(24) });
        var iconHeader = new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(iconHeader, 0);
        ColumnHeadersGrid.Children.Add(iconHeader);

        // Определяем, какая колонка сейчас сортируется (по SortedColumnKey)
        var sortedKey = ColumnConfigService.SortedColumnKey;
        var sortedCol = !string.IsNullOrEmpty(sortedKey)
            ? cols.FirstOrDefault(c => c.Key == sortedKey)
            : null;

        int colIdx = 1;
        foreach (var col in cols)
        {
            var width = col.Key == "Name"
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(col.Width);
            var minW = col.Key == "Name" ? 120.0 : (col.Width > 0 ? col.Width * 0.5 : 60);

            ColumnHeadersGrid.ColumnDefinitions.Add(
                new System.Windows.Controls.ColumnDefinition { Width = width, MinWidth = minW });

            // Добавляем индикатор сортировки к заголовку (стрелка)
            var headerText = col.Header;
            if (sortedCol == col)
            {
                headerText += col.SortDirection == ListSortDirection.Ascending ? SortAscArrow : SortDescArrow;
            }

            var header = new TextBlock
            {
                Text = headerText,
                Style = (Style)FindResource("ColumnHeaderStyle"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = col.Key,
                Cursor = Cursors.Hand,
                ToolTip = string.Format(LocalizationService.Current.GetString("FilePanel.SortBy"), col.Header)
            };

            if (col.IsRightAligned)
            {
                header.HorizontalAlignment = HorizontalAlignment.Right;
                header.Margin = new Thickness(0, 0, 6, 0);
            }
            else
            {
                header.HorizontalAlignment = HorizontalAlignment.Left;
                header.Margin = new Thickness(6, 0, 0, 0);
            }

            header.MouseLeftButtonDown += ColumnHeader_Click;
            Grid.SetColumn(header, colIdx);
            ColumnHeadersGrid.Children.Add(header);
            colIdx++;
        }
    }

    /// <summary>
    /// Генерирует DataTemplate для элементов ListBox из ActiveColumns.
    /// </summary>
    private void RebuildItemTemplate()
    {
        var cols = ColumnConfigService.ActiveColumns.Where(c => c.IsVisible).ToList();
        var xaml = BuildItemTemplateXaml(cols);
        try
        {
            var template = (DataTemplate)XamlReader.Parse(xaml);
            FileList.ItemTemplate = template;
        }
        catch (Exception ex)
        {
            LogService.Error($"RebuildItemTemplate failed: {ex.Message}", nameof(FilePanel));
            FileList.ItemTemplate = null;
        }
    }

    /// <summary>
    /// Строит XAML DataTemplate для элементов ListBox.
    /// </summary>
    private static string BuildItemTemplateXaml(List<ColumnDef> cols)
    {
        const string nsP = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        const string nsX = "http://schemas.microsoft.com/winfx/2006/xaml";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<DataTemplate xmlns=\"{nsP}\" xmlns:x=\"{nsX}\">");
        sb.AppendLine("<Grid MinHeight=\"26\" HorizontalAlignment=\"Stretch\" VerticalAlignment=\"Stretch\"");
        sb.AppendLine("      Margin=\"2,0\" ToolTip=\"{Binding FullPath}\">");

        // ColumnDefinitions
        sb.AppendLine("<Grid.ColumnDefinitions>");
        sb.AppendLine("<ColumnDefinition Width=\"24\"/>");
        foreach (var col in cols)
        {
            if (col.Key == "Name")
                sb.AppendLine("<ColumnDefinition Width=\"*\" MinWidth=\"120\"/>");
            else
                sb.AppendLine($"<ColumnDefinition Width=\"{col.Width}\" MinWidth=\"{col.Width * 0.5}\"/>");
        }
        sb.AppendLine("</Grid.ColumnDefinitions>");

        // Col 0: иконка
        sb.AppendLine("<TextBlock Grid.Column=\"0\"");
        sb.AppendLine("  Text=\"{Binding Converter={StaticResource FileIcon}}\"");
        sb.AppendLine("  Foreground=\"{Binding Converter={StaticResource FileIconColor}}\"");
        sb.AppendLine("  FontFamily=\"Segoe MDL2 Assets\" FontSize=\"13\"");
        sb.AppendLine("  HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>");

        int ci = 1;
        foreach (var col in cols)
        {
            switch (col.Key)
            {
                case "Name":
                    sb.AppendLine($"<Grid Grid.Column=\"{ci}\" VerticalAlignment=\"Center\" Margin=\"6,0,6,0\">");
                    sb.AppendLine("  <Grid.ColumnDefinitions>");
                    sb.AppendLine("    <ColumnDefinition Width=\"*\" MinWidth=\"80\"/>");
                    sb.AppendLine("    <ColumnDefinition Width=\"Auto\"/>");
                    sb.AppendLine("  </Grid.ColumnDefinitions>");
                    sb.AppendLine("  <TextBlock Grid.Column=\"0\" Text=\"{Binding Name}\"");
                    sb.AppendLine("    VerticalAlignment=\"Center\" TextWrapping=\"NoWrap\" FontSize=\"13\"");
                    sb.AppendLine("    Foreground=\"{Binding GitState, Converter={StaticResource GitStateBrush}}\"");
                    sb.AppendLine("    TextTrimming=\"CharacterEllipsis\" HorizontalAlignment=\"Stretch\"");
                    sb.AppendLine("    MaxWidth=\"{Binding ActualWidth, RelativeSource={RelativeSource AncestorType=ListBox}}\"");
                    sb.AppendLine("    ToolTip=\"{Binding Name}\"/>");
                    sb.AppendLine("  <Border x:Name=\"GitIndicator\" Grid.Column=\"1\"");
                    sb.AppendLine("    Width=\"6\" Height=\"6\" CornerRadius=\"3\"");
                    sb.AppendLine("    Margin=\"8,0,0,0\" VerticalAlignment=\"Center\"");
                    sb.AppendLine("    HorizontalAlignment=\"Right\" Visibility=\"Collapsed\"");
                    sb.AppendLine("    Background=\"{DynamicResource OkBrush}\"/>");
                    sb.AppendLine("</Grid>");
                    break;

                case "Extension":
                    sb.AppendLine($"<TextBlock Grid.Column=\"{ci}\" Text=\"{{Binding Extension}}\"");
                    sb.AppendLine("  Foreground=\"{DynamicResource FgDimBrush}\"");
                    sb.AppendLine("  HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\"");
                    sb.AppendLine("  Margin=\"6,0,0,0\" FontSize=\"11\" TextTrimming=\"CharacterEllipsis\"/>");
                    break;

                case "Size":
                    sb.AppendLine($"<TextBlock Grid.Column=\"{ci}\" Text=\"{{Binding SizeDisplay}}\"");
                    sb.AppendLine("  Foreground=\"{DynamicResource FgDimBrush}\"");
                    sb.AppendLine("  HorizontalAlignment=\"Right\" VerticalAlignment=\"Center\"");
                    sb.AppendLine("  Margin=\"0,0,6,0\" FontSize=\"11\"");
                    sb.AppendLine("  FontFamily=\"Consolas, Segoe UI\" TextTrimming=\"CharacterEllipsis\"/>");
                    break;

                case "ModifiedDate":
                    sb.AppendLine($"<TextBlock Grid.Column=\"{ci}\" Text=\"{{Binding ModifiedDisplay}}\"");
                    sb.AppendLine("  Foreground=\"{DynamicResource FgDimBrush}\"");
                    sb.AppendLine("  HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\"");
                    sb.AppendLine("  Margin=\"6,0,0,0\" FontSize=\"11\"");
                    sb.AppendLine("  FontFamily=\"Consolas, Segoe UI\" TextTrimming=\"CharacterEllipsis\"/>");
                    break;

                case "CreatedDate":
                    sb.AppendLine($"<TextBlock Grid.Column=\"{ci}\" Text=\"{{Binding CreatedDate, StringFormat='yyyy-MM-dd HH:mm'}}\"");
                    sb.AppendLine("  Foreground=\"{DynamicResource FgDimBrush}\"");
                    sb.AppendLine("  HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\"");
                    sb.AppendLine("  Margin=\"6,0,0,0\" FontSize=\"11\"");
                    sb.AppendLine("  FontFamily=\"Consolas, Segoe UI\" TextTrimming=\"CharacterEllipsis\"/>");
                    break;

                case "Attributes":
                    sb.AppendLine($"<TextBlock Grid.Column=\"{ci}\" Text=\"{{Binding Attributes}}\"");
                    sb.AppendLine("  Foreground=\"{DynamicResource FgDimBrush}\"");
                    sb.AppendLine("  HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\"");
                    sb.AppendLine("  Margin=\"6,0,0,0\" FontSize=\"11\"");
                    sb.AppendLine("  FontFamily=\"Consolas, Segoe UI\" TextTrimming=\"CharacterEllipsis\"/>");
                    break;

                case "Type":
                    // FileSystemItem has no TypeDisplay property; Extension is the closest available
                    sb.AppendLine($"<TextBlock Grid.Column=\"{ci}\" Text=\"{{Binding Extension}}\"");
                    sb.AppendLine("  Foreground=\"{DynamicResource FgDimBrush}\"");
                    sb.AppendLine("  HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\"");
                    sb.AppendLine("  Margin=\"6,0,0,0\" FontSize=\"11\" TextTrimming=\"CharacterEllipsis\"/>");
                    break;
            }
            ci++;
        }

        sb.AppendLine("</Grid>");

        // DataTriggers for GitIndicator
        sb.AppendLine("<DataTemplate.Triggers>");
        foreach (var (gitState, brush) in new[]
        {
            ("Modified", "WarnBrush"),
            ("Added", "OkBrush"),
            ("Deleted", "ErrBrush"),
            ("Untracked", "FgMutedBrush"),
            ("Conflicted", "ErrBrush")
        })
        {
            sb.AppendLine($"<DataTrigger Binding=\"{{Binding GitState}}\" Value=\"{gitState}\">");
            sb.AppendLine($"  <Setter Property=\"Visibility\" Value=\"Visible\" TargetName=\"GitIndicator\"/>");
            sb.AppendLine($"  <Setter Property=\"Background\" Value=\"{{DynamicResource {brush}}}\" TargetName=\"GitIndicator\"/>");
            sb.AppendLine("</DataTrigger>");
        }
        sb.AppendLine("</DataTemplate.Triggers>");
        sb.AppendLine("</DataTemplate>");

        return sb.ToString();
    }

    /// <summary>
    /// Обработчик клика на заголовке колонки: сортировка по колонке.
    /// </summary>
    private void ColumnHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock header || header.Tag is not string key) return;
        if (DataContext is not PanelViewModel vm) return;

        vm.SetSortByColumn(key);
        RebuildColumnHeaders();
    }

    // ═══════════════════════════════════════════════
    // DRAG & DROP МЕЖДУ ПАНЕЛЯМИ (ph6.3)
    // DRAG & DROP BETWEEN PANELS (ph6.3)
    // ═══════════════════════════════════════════════

    /// <summary>
    /// Начало перетаскивания: при нажатии левой кнопки мыши на выделенных элементах
    /// запоминаем позицию; реальный drag стартует только при движении мыши ≥5 px.
    /// Это позволяет клику и двойному клику работать без блокировки DoDragDrop.
    /// Drag start: on left mouse button press on selected items,
    /// remember position; real drag starts only on mouse move ≥5 px.
    /// This allows click and double-click to work without DoDragDrop blocking.
    /// </summary>
    private Point _dragStartPoint;
    private bool _dragPending;

    private void ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PanelViewModel vm) return;
        var lb = FileList;
        var hit = e.OriginalSource as DependencyObject;
        var item = FindAncestor<ListBoxItem>(hit);
        if (item is null) return;
        if (item.DataContext is not Models.FileSystemItem fi) return;

        var isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        // Управляем только метками (FileSystemItem.IsSelected).
        // Курсор (SelectedItem) и фокус — WPF обрабатывает сам (e.Handled не ставим!).
        // Manage marks only (FileSystemItem.IsSelected).
        // Cursor (SelectedItem) and focus — WPF handles natively (don't set e.Handled!).

        if (isCtrl && !fi.IsParent)
        {
            // Ctrl+клик: переключить метку, остальные не трогать
            fi.IsSelected = !fi.IsSelected;
            _rangeAnchor = lb.SelectedIndex;
        }
        else if (isShift && !fi.IsParent)
        {
            // Shift+клик: диапазон меток от якоря до кликнутого
            var anchor = _rangeAnchor >= 0 ? _rangeAnchor : lb.SelectedIndex;
            var clickIdx = lb.Items.IndexOf(fi);
            if (anchor >= 0 && clickIdx >= 0)
            {
                var from = Math.Min(anchor, clickIdx);
                var to = Math.Max(anchor, clickIdx);
                for (int i = from; i <= to; i++)
                    if (lb.Items[i] is Models.FileSystemItem item2 && !item2.IsParent)
                        item2.IsSelected = true;
            }
        }
        else if (!fi.IsParent)
        {
            // Обычный клик: сбросить все метки (курсор поставит WPF)
            foreach (var x in vm.Items) x.IsSelected = false;
            _rangeAnchor = lb.SelectedIndex;
        }
        else
        {
            // Клик по ".." — не сбрасываем метки
            _rangeAnchor = lb.SelectedIndex;
        }

        // Drag detection
        _dragStartPoint = e.GetPosition(FileList);
        _dragPending = true;
    }

    /// <summary>
    /// Если мышь сдвинулась ≥5 px — стартуем drag-and-drop.
    /// </summary>
    private void ListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragPending || e.LeftButton != MouseButtonState.Pressed) return;
        if (DataContext is not PanelViewModel vm) return;

        var pos = e.GetPosition(FileList);
        var dx = Math.Abs(pos.X - _dragStartPoint.X);
        var dy = Math.Abs(pos.Y - _dragStartPoint.Y);
        if (dx < SystemParameters.MinimumHorizontalDragDistance && dy < SystemParameters.MinimumVerticalDragDistance) return;

        _dragPending = false;

        var items = vm.GetSelectionOrCurrent().Where(i => !i.IsParent).ToList();
        if (items.Count == 0) return;

        var paths = items.Select(i => i.FullPath).ToArray();
        var data = new DataObject(DataFormats.FileDrop, paths);

        // Shift/Alt+Shift = Move, Ctrl+Alt = Link (symlink), иначе = Copy
        var isCtrlAlt = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var isMove = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        DragDropEffects effect;
        if (isCtrlAlt)
            effect = DragDropEffects.Link;
        else if (isMove)
            effect = DragDropEffects.Move;
        else
            effect = DragDropEffects.Copy;

        DragDrop.DoDragDrop(FileList, data, effect);
    }

    /// <summary>
    /// Кнопка мыши отпущена без drag — сбрасываем флаг.
    /// </summary>
    private void ListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragPending = false;
    }

    /// <summary>
    /// Наведение на ListBox: показываем overlay-подсветку.
    /// Mouse drag enter: show overlay highlight.
    /// </summary>
    private void ListBox_DragEnter(object sender, DragEventArgs e)
    {
        // Проверяем наличие данных в любом поддерживаемом формате
        bool hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop) ||
                        e.Data.GetDataPresent("CoderCommander.FilePaths");

        if (hasFiles)
        {
            DragHighlightBorder.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    /// <summary>
    /// Уход с ListBox: скрываем overlay-подсветку.
    /// Mouse drag leave: hide overlay highlight.
    /// </summary>
    private void ListBox_DragLeave(object sender, DragEventArgs e)
    {
        DragHighlightBorder.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    /// <summary>
    /// Над ListBox: определяем допустимые эффекты по содержимому DataObject и модификаторам.
    /// Over ListBox: determine allowed effects based on DataObject content and modifiers.
    /// Shift = Move, Alt = Symlink (Link), Ctrl+Alt = Hardlink (Link), иначе = Copy.
    /// </summary>
    private void ListBox_DragOver(object sender, DragEventArgs e)
    {
        // Проверяем наличие данных в любом поддерживаемом формате
        bool hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop) ||
                        e.Data.GetDataPresent("CoderCommander.FilePaths");

        if (!hasFiles)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        // Разрешаем все эффекты, Windows сам выберет по модификаторам
        e.Effects = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
        e.Handled = true;
    }

    /// <summary>
    /// Drop: передаём пути в MainViewModel.DropFilesCommand для копирования/переноса/создания ярлыка.
    /// Drop: pass paths to MainViewModel.DropFilesCommand for copy/move/symlink.
    /// Shift = Move, Alt = Symlink, Ctrl+Alt = Hardlink, иначе = Copy.
    /// </summary>
    private async void ListBox_Drop(object sender, DragEventArgs e)
    {
        DragHighlightBorder.Visibility = Visibility.Collapsed;

        if (DataContext is not PanelViewModel vm) return;
        if (Application.Current.MainWindow?.DataContext is not MainViewModel mvm) return;

        // Получаем пути из любого поддерживаемого формата
        string[]? paths = null;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            paths = e.Data.GetData(DataFormats.FileDrop) as string[];

        if (paths is null || paths.Length == 0) return;

        // Определяем операцию по модификаторам
        var isCtrlAlt = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var isAltOnly = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        var isMove = isShift;
        var isSymlink = isAltOnly;
        var isHardlink = isCtrlAlt;

        var targetDir = vm.CurrentPath;
        var sourceDir = paths.Length > 0 ? System.IO.Path.GetDirectoryName(paths[0]) : null;
        e.Handled = true;

        await mvm.DropFilesAsync(paths, targetDir, isMove, sourceDir, isSymlink, isHardlink);
    }

    /// <summary>
    /// Вспомогательный поиск предка определённого типа в визуальном дереве.
    /// Helper to find an ancestor of a specific type in the visual tree.
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T found) return found;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
