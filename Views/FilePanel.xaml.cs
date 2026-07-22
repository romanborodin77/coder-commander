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
    /// Текущее направление сортировки для колонки (по ключу колонки).
    /// Current sort direction per column key.
    /// </summary>
    private readonly Dictionary<string, ListSortDirection> _sortDirections = new();

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
        FileList.KeyDown += QuickSearch_KeyDown;
        FileList.TextInput += QuickSearch_TextInput;
        FilterInput.KeyDown += FilterInput_KeyDown;

        // Динамические колонки: загрузка конфигурации и подписка на изменения
        ColumnConfigService.ColumnsChanged += (_, _) => RebuildColumns();
        ColumnConfigService.Load();
        Loaded += (_, _) => RebuildColumns();
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
                    ? Directory.GetParent(vm.CurrentPath)?.FullName ?? vm.CurrentPath
                    : i.FullPath);
            else if (Application.Current.MainWindow?.DataContext is MainViewModel mvm)
                _ = mvm.OpenItemAsync();
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
                            ? Directory.GetParent(vm.CurrentPath)?.FullName ?? vm.CurrentPath
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
                    mvmDel.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.A when Keyboard.Modifiers == ModifierKeys.Control:
                foreach (var it in lb.Items)
                    if (it is Models.FileSystemItem fi) fi.IsSelected = true;
                e.Handled = true;
                break;
        }
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

        int colIdx = 1;
        foreach (var col in cols)
        {
            var width = col.Key == "Name"
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(col.Width);
            var minW = col.Key == "Name" ? 120.0 : (col.Width > 0 ? col.Width * 0.5 : 60);

            ColumnHeadersGrid.ColumnDefinitions.Add(
                new System.Windows.Controls.ColumnDefinition { Width = width, MinWidth = minW });

            var header = new TextBlock
            {
                Text = col.Header,
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
        catch
        {
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
                case "Attributes":
                    sb.AppendLine($"<TextBlock Grid.Column=\"{ci}\" Text=\"—\"");
                    sb.AppendLine("  Foreground=\"{DynamicResource FgDimBrush}\"");
                    sb.AppendLine("  HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\"");
                    sb.AppendLine("  Margin=\"6,0,0,0\" FontSize=\"11\"");
                    sb.AppendLine("  FontFamily=\"Consolas, Segoe UI\" TextTrimming=\"CharacterEllipsis\"/>");
                    break;

                case "Type":
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

        var view = vm.ItemsView;
        if (view is null) return;

        // Переключаем направление сортировки
        var dir = _sortDirections.TryGetValue(key, out var existing) && existing == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        _sortDirections[key] = dir;

        view.SortDescriptions.Clear();

        // Папки всегда сверху
        view.SortDescriptions.Add(new SortDescription(nameof(Models.FileSystemItem.IsDirectory), ListSortDirection.Descending));

        var propDesc = key switch
        {
            "Name" => nameof(Models.FileSystemItem.Name),
            "Extension" => nameof(Models.FileSystemItem.Extension),
            "Size" => nameof(Models.FileSystemItem.Size),
            "ModifiedDate" => nameof(Models.FileSystemItem.Modified),
            _ => nameof(Models.FileSystemItem.Name)
        };

        view.SortDescriptions.Add(new SortDescription(propDesc, dir));
        view.Refresh();
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
        var hit = e.OriginalSource as DependencyObject;
        var item = FindAncestor<ListBoxItem>(hit);
        if (item is null) return;

        // Если кликнули по невыделенному элементу — выделяем его (без запуска drag).
        // Если кликнули по выделенному — запоминаем позицию для potential drag.
        if (!item.IsSelected)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                item.IsSelected = !item.IsSelected;
            }
            else
            {
                foreach (var fi in vm.Items) fi.IsSelected = false;
                item.IsSelected = true;
            }
        }

        // Запоминаем позицию — drag стартует в MouseMove если движение ≥ threshold.
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
