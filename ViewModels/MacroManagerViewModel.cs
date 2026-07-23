using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel для окна управления макросами.
/// ViewModel for the macro manager window.
/// </summary>
public partial class MacroManagerViewModel : ObservableObject
{
    private readonly MacroService _service = MacroService.Current;

    /// <summary>Хелпер для получения строки локализации. / Helper for getting localized string.</summary>
    private static string L10n(string key) => LocalizationService.Current.GetString(key);

    /// <summary>
    /// Все макросы (копия для редактирования). / All macros (editing copy).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MacroItem> _macros = new();

    /// <summary>
    /// Выбранный макрос. / Selected macro.
    /// </summary>
    [ObservableProperty]
    private MacroItem? _selectedMacro;

    /// <summary>
    /// Поисковый запрос по имени. / Name search query.
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = "";

    /// <summary>
    /// Создаёт ViewModel и загружает макросы. / Creates ViewModel and loads macros.
    /// </summary>
    public MacroManagerViewModel()
    {
        LoadMacros();
    }

    /// <summary>
    /// Загружает макросы из сервиса. / Loads macros from the service.
    /// </summary>
    private void LoadMacros()
    {
        Macros = new ObservableCollection<MacroItem>(
            _service.GetAll().Select(m => m));
        ApplyFilter();
    }

    /// <summary>
    /// Применяет фильтр по поисковому запросу. / Applies the search query filter.
    /// </summary>
    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// Фильтрует список макросов. / Filters the macro list.
    /// </summary>
    private void ApplyFilter()
    {
        var all = _service.GetAll();
        var filtered = string.IsNullOrWhiteSpace(SearchQuery)
            ? all
            : all.Where(m => m.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        Macros = new ObservableCollection<MacroItem>(filtered);
        if (SelectedMacro is not null && !Macros.Contains(SelectedMacro))
            SelectedMacro = Macros.FirstOrDefault();
    }

    /// <summary>
    /// Команда: добавить новый макрос. / Command: add a new macro.
    /// </summary>
    [RelayCommand]
    private void AddMacro()
    {
        var name = Prompt(L10n("Macro.Add"), L10n("Macro.Name"));
        if (string.IsNullOrWhiteSpace(name)) return;

        var macro = new MacroItem(name);
        _service.Add(macro);
        Macros.Add(macro);
        SelectedMacro = macro;
    }

    /// <summary>
    /// Команда: удалить выбранный макрос. / Command: delete the selected macro.
    /// </summary>
    [RelayCommand]
    private void DeleteMacro()
    {
        if (SelectedMacro is null) return;

        var confirm = string.Format(L10n("Macro.DeleteConfirm"), SelectedMacro.Name);
        var result = StyledMessageBoxWindow.Show(confirm, L10n("Macro.Delete"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _service.Delete(SelectedMacro.Id);
        Macros.Remove(SelectedMacro);
        SelectedMacro = Macros.FirstOrDefault();
    }

    /// <summary>
    /// Команда: выполнить выбранный макрос. / Command: execute the selected macro.
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ExecuteMacroAsync()
    {
        if (SelectedMacro is null) return;
        if (Application.Current.MainWindow?.DataContext is not MainViewModel vm) return;

        // Создаём временный CommandEngine с командами MainViewModel / Create temporary CommandEngine with MainViewModel commands
        var engine = new CommandEngine();
        RegisterMainCommands(engine, vm);

        var executor = new MacroExecutor(engine);
        var result = await executor.ExecuteAsync(SelectedMacro);
        vm.StatusText = result;
    }

    /// <summary>
    /// Команда: сохранить все изменения. / Command: save all changes.
    /// </summary>
    [RelayCommand]
    private void SaveChanges()
    {
        // Убедимся, что все макросы в списке сохранены в настройки
        // Ensure all macros in the list are persisted to settings
        var settings = SettingsService.Load();
        settings.Macros.Clear();
        foreach (var m in _service.GetAll())
            settings.Macros.Add(m);
        _service.Save();

        if (Application.Current.MainWindow?.DataContext is MainViewModel vm)
            vm.StatusText = L10n("Macro.Saved");
    }

    /// <summary>
    /// Добавляет шаг к выбранному макросу. / Adds a step to the selected macro.
    /// </summary>
    [RelayCommand]
    private void AddStep()
    {
        if (SelectedMacro is null) return;

        var cmdName = Prompt(L10n("Macro.AddStep"), L10n("Macro.StepCommandPrompt"));
        if (string.IsNullOrWhiteSpace(cmdName)) return;

        var nextOrder = SelectedMacro.Steps.Count > 0
            ? SelectedMacro.Steps.Max(s => s.Order) + 1
            : 0;

        var step = new MacroStep(cmdName, nextOrder);

        var paramsStr = Prompt(L10n("Macro.AddStep"), L10n("Macro.StepParamsPrompt"));
        if (!string.IsNullOrWhiteSpace(paramsStr))
        {
            foreach (var pair in paramsStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var eqIdx = pair.IndexOf('=');
                if (eqIdx > 0)
                    step.Params[pair[..eqIdx].Trim()] = pair[(eqIdx + 1)..].Trim();
            }
        }

        SelectedMacro.Steps.Add(step);
    }

    /// <summary>
    /// Удаляет выбранный шаг из макроса. / Removes the selected step from the macro.
    /// </summary>
    /// <param name="step">Шаг для удаления. / Step to remove.</param>
    [RelayCommand]
    private void RemoveStep(MacroStep? step)
    {
        if (step is null || SelectedMacro is null) return;
        SelectedMacro.Steps.Remove(step);
    }

    /// <summary>
    /// Перемещает шаг вверх. / Moves a step up.
    /// </summary>
    /// <param name="step">Шаг для перемещения. / Step to move.</param>
    [RelayCommand]
    private void MoveStepUp(MacroStep? step)
    {
        if (step is null || SelectedMacro is null) return;
        var idx = SelectedMacro.Steps.IndexOf(step);
        if (idx <= 0) return;
        SelectedMacro.Steps.Move(idx, idx - 1);
        RenumberSteps();
    }

    /// <summary>
    /// Перемещает шаг вниз. / Moves a step down.
    /// </summary>
    /// <param name="step">Шаг для перемещения. / Step to move.</param>
    [RelayCommand]
    private void MoveStepDown(MacroStep? step)
    {
        if (step is null || SelectedMacro is null) return;
        var idx = SelectedMacro.Steps.IndexOf(step);
        if (idx < 0 || idx >= SelectedMacro.Steps.Count - 1) return;
        SelectedMacro.Steps.Move(idx, idx + 1);
        RenumberSteps();
    }

    /// <summary>
    /// Перенумеровывает шаги по порядку. / Renumbers steps in order.
    /// </summary>
    private void RenumberSteps()
    {
        if (SelectedMacro is null) return;
        for (int i = 0; i < SelectedMacro.Steps.Count; i++)
            SelectedMacro.Steps[i].Order = i;
    }

    /// <summary>
    /// Регистрирует команды из MainViewModel в CommandEngine.
    /// Registers commands from MainViewModel into CommandEngine.
    /// </summary>
    private static void RegisterMainCommands(CommandEngine engine, MainViewModel vm)
    {
        engine.Register(new QuickCommand("app.copy", LocalizationService.Current.GetString("Quick.Copy"),
            _ => { vm.CopyCommand.Execute(null); return System.Threading.Tasks.Task.FromResult(""); }));
        engine.Register(new QuickCommand("app.move", LocalizationService.Current.GetString("Quick.Move"),
            _ => { vm.MoveCommand.Execute(null); return System.Threading.Tasks.Task.FromResult(""); }));
        engine.Register(new QuickCommand("app.delete", LocalizationService.Current.GetString("Quick.Delete"),
            _ => { vm.DeleteCommand.Execute(null); return System.Threading.Tasks.Task.FromResult(""); }));
        engine.Register(new QuickCommand("app.rename", LocalizationService.Current.GetString("Quick.Rename"),
            _ => { vm.RenameCommand.Execute(null); return System.Threading.Tasks.Task.FromResult(""); }));
        engine.Register(new QuickCommand("app.search", LocalizationService.Current.GetString("Quick.Search"),
            _ => { vm.SearchCommand.Execute(null); return System.Threading.Tasks.Task.FromResult(""); }));
        engine.Register(new QuickCommand("app.terminal", LocalizationService.Current.GetString("Quick.Terminal"),
            _ => { vm.ToggleTerminalPanelCommand.Execute(null); return System.Threading.Tasks.Task.FromResult(""); }));
        engine.Register(new QuickCommand("app.refresh", LocalizationService.Current.GetString("Quick.Refresh"),
            _ => { vm.RefreshAllCommand.Execute(null); return System.Threading.Tasks.Task.FromResult(""); }));
        engine.Register(new QuickCommand("app.git", LocalizationService.Current.GetString("Quick.Git"),
            _ => { vm.ShowGitTabCommand.Execute(null); return System.Threading.Tasks.Task.FromResult(""); }));
        engine.Register(new QuickCommand("app.docker", LocalizationService.Current.GetString("Quick.Docker"),
            _ => { vm.ShowDockerTabCommand.Execute(null); return System.Threading.Tasks.Task.FromResult(""); }));
    }

    /// <summary>
    /// Диалог ввода строки. / String input dialog.
    /// </summary>
    private static string? Prompt(string title, string prompt, string def = "")
    {
        var w = new Window
        {
            Title = title, Width = 400, Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            Owner = Application.Current.MainWindow,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["BgPanelBrush"]
        };

        var root = new System.Windows.Controls.DockPanel();

        var titleBar = new System.Windows.Controls.Border
        {
            Height = 36, Background = (System.Windows.Media.Brush)Application.Current.Resources["TitleBarBgBrush"]
        };
        System.Windows.Controls.DockPanel.SetDock(titleBar, System.Windows.Controls.Dock.Top);
        var titleSp = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleSp.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = title,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["FgLightBrush"],
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        titleBar.Child = titleSp;
        titleBar.MouseLeftButtonDown += (_, _) => w.DragMove();
        root.Children.Add(titleBar);

        var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(15, 10, 15, 10) };
        sp.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["FgLightBrush"],
            Margin = new Thickness(0, 0, 0, 8)
        });
        var tb = new System.Windows.Controls.TextBox
        {
            Text = def,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["BgInputBrush"],
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["FgLightBrush"],
            BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["BorderBrush"]
        };
        sp.Children.Add(tb);

        var btns = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new System.Windows.Controls.Button
        {
            Content = LocalizationService.Current.GetString("MsgBox.OK"),
            Width = 80, IsDefault = true,
            Style = (System.Windows.Style)Application.Current.Resources["AccentButtonStyle"]
        };
        var cn = new System.Windows.Controls.Button
        {
            Content = LocalizationService.Current.GetString("Dialog.Cancel"),
            Width = 80, IsCancel = true,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (System.Windows.Style)Application.Current.Resources["DefaultButtonStyle"]
        };
        ok.Click += (_, _) => w.DialogResult = true;
        cn.Click += (_, _) => w.DialogResult = false;
        btns.Children.Add(ok);
        btns.Children.Add(cn);
        sp.Children.Add(btns);

        root.Children.Add(sp);
        w.Content = root;
        tb.SelectAll();
        tb.Focus();
        return w.ShowDialog() == true ? tb.Text : null;
    }
}
