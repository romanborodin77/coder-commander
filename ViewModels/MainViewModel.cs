using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.FileSystem;
using CoderCommander.Operations;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.ViewModels;
using CoderCommander.Views;
using OverwritePolicy = CoderCommander.Operations.OverwritePolicy;

#pragma warning disable CS0618 // Backward-compatible: FileService UI helpers kept for compatibility

namespace CoderCommander.ViewModels;

/// <summary>
/// Оркестратор операций: открытие/редактирование, копирование, перемещение, терминал, закладки, палитра команд.
/// Orchestrates operations: open/edit, copy, move, terminal, bookmarks, command palette.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IProcessService _proc = new ProcessService();
    private readonly GitService _git;
    private readonly DockerService _docker;
    private readonly SshService _ssh;
    private readonly CopyService _copy;

    /// <summary>Servis ocheredi operatsiy (ph5.2).</summary>
    private readonly OperationQueueService _queue = OperationQueueService.Current;
    private readonly CommandEngine _commands = new();

    ///<summary>Dictionary of running console sessions: tabId -> ChildConsoleService.</summary>
    public Dictionary<int, ChildConsoleService> TerminalServices { get; } = new();

    // ═══════════════════════════════════════════
    // ВКЛАДКИ ПАНЕЛЕЙ (ph5.9) / PANEL TABS (ph5.9)
    // ═══════════════════════════════════════════

    /// <summary>Коллекция вкладок левой панели. / Left panel tab collection.</summary>
    [ObservableProperty] private ObservableCollection<TabViewModel> _leftTabs = new();
    /// <summary>Коллекция вкладок правой панели. / Right panel tab collection.</summary>
    [ObservableProperty] private ObservableCollection<TabViewModel> _rightTabs = new();
    /// <summary>Активная вкладка левой панели. / Active left panel tab.</summary>
    [ObservableProperty] private TabViewModel? _activeLeftTab;
    /// <summary>Активная вкладка правой панели. / Active right panel tab.</summary>
    [ObservableProperty] private TabViewModel? _activeRightTab;

    /// <summary>
    /// Модель левой панели (обратная совместимость: возвращает Panel из ActiveLeftTab).
    /// Left panel model (backward compat: returns Panel from ActiveLeftTab).
    /// </summary>
    public PanelViewModel LeftPanel => ActiveLeftTab?.Panel!;
    /// <summary>
    /// Модель правой панели (обратная совместимость: возвращает Panel из ActiveRightTab).
    /// Right panel model (backward compat: returns Panel from ActiveRightTab).
    /// </summary>
    public PanelViewModel RightPanel => ActiveRightTab?.Panel!;

    /// <summary>Активная панель (левая или правая).</summary>
    [ObservableProperty] private PanelViewModel _activePanel = null!;
    /// <summary>Текст строки состояния.</summary>
    [ObservableProperty] private string _statusText = LocalizationService.Current.GetString("Status.Ready");
    /// <summary>Флаг открытого всплывающего терминала.</summary>
    [ObservableProperty] private bool _isTerminalOpen;
    /// <summary>Флаг открытой палитры команд (Ctrl+P).</summary>
    [ObservableProperty] private bool _isCommandPaletteOpen;
    /// <summary>Текст поискового запроса в палитре команд.</summary>
    [ObservableProperty] private string _commandQuery = "";
    /// <summary>Результаты поиска команд.</summary>
    [ObservableProperty] private ObservableCollection<QuickCommand> _commandResults = new();
    /// <summary>Флаг выполнения длительной операции (блокировка UI).</summary>
    [ObservableProperty] private bool _isBusy;
    /// <summary>Прогресс операции (0-100).</summary>
    [ObservableProperty] private double _progressValue;
    /// <summary>Текст текущей операции с прогрессом.</summary>
    [ObservableProperty] private string _progressText = "";
    /// <summary>Индекс выбранной вкладки нижней панели (0=Git, 1=Docker, 2=SSH, 3=SFTP).</summary>
    [ObservableProperty] private int _selectedTabIndex;

    /// <summary>Видимость нижней панели вкладок (Git/Docker/SSH/SFTP). Свёрнута по умолчанию, разворачивается по вызову вкладки.</summary>
    [ObservableProperty] private bool _isBottomPanelVisible;

    /// <summary>Тип shell для новых вкладок терминала ("cmd" или "powershell"). Берётся из настроек.</summary>
    [ObservableProperty] private string _terminalShell = SettingsService.Load().TerminalShell;

    ///<summary>Текущее имя темы для биндинга IsChecked в меню.</summary>
    [ObservableProperty] private string _theme = SettingsService.Load().Theme.ToString();

    /// <summary>Видимость панели терминала (нижняя).</summary>
    [ObservableProperty] private bool _isTerminalPanelVisible;

    /// <summary>Высота панели терминала из настроек.</summary>
    public double TerminalPanelHeight => SettingsService.Load().TerminalPanelHeight;

    /// <summary>Коллекция вкладок терминала.</summary>
    [ObservableProperty] private ObservableCollection<TerminalTabViewModel> _terminalTabs = new();
    /// <summary>Активная вкладка терминала.</summary>
    [ObservableProperty] private TerminalTabViewModel? _activeTerminalTab;

    /// <summary>ViewModel для Git-панели.</summary>
    public GitViewModel Git { get; }
    /// <summary>ViewModel для Docker-панели.</summary>
    public DockerViewModel Docker { get; }
    /// <summary>ViewModel для SSH-панели.</summary>
    public SshViewModel Ssh { get; }
    /// <summary>ViewModel для SFTP-панели.</summary>
    public SftpViewModel Sftp { get; }
    /// <summary>ViewModel для панели облачных хранилищ (ph8.4). / ViewModel for cloud storage panel.</summary>
    public CloudStorageViewModel CloudStorage { get; }
    ///<summary>Called by View to open popup editor (F4).</summary>
    public Action<string, string>? OpenEditorRequest;
    ///<summary>Called by View to open popup viewer (F3).</summary>
    public Action<string, string>? OpenViewerRequest;
    ///<summary>Called by View to close popup editor.</summary>
    public Action? CloseEditorRequest;

    /// <summary>Called View open operation queue window (ph5.2).</summary>
    public Action? OpenOperationQueueRequest;

    /// <summary>Active ops count queue (status bar).</summary>
    [ObservableProperty] private int _operationQueueActiveCount;

    /// <summary>Total ops count queue (status bar).</summary>
    [ObservableProperty] private int _operationQueueTotalCount;

    /// <summary>Список закладок для быстрого перехода (Desktop, Documents, Downloads, C:\).</summary>
    public List<(string Name, string Path)> Bookmarks { get; } = new()
    {
        (LocalizationService.Current.GetString("Bookmark.Desktop"), Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
        (LocalizationService.Current.GetString("Bookmark.Documents"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        (LocalizationService.Current.GetString("Bookmark.Downloads"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
        ("C:\\", "C:\\")
    };

    /// <summary>
    /// Создаёт экземпляр MainViewModel, инициализирует сервисы, панели, Git/Docker/SSH/SFTP ViewModel, палитру команд и первую вкладку терминала.
    /// Creates the MainViewModel instance, initializes services, panels, Git/Docker/SSH/SFTP ViewModels, command palette, and the first terminal tab.
    /// </summary>
    /// <summary>Хелпер для получения строки локализации. / Helper for getting localized string.</summary>
    private static string L10n(string key) => LocalizationService.Current.GetString(key);

    public MainViewModel()
    {
        BookmarkService.Current.Load(); //ph5.3: загрузка закладок

        _git = new GitService(_proc);
        _docker = new DockerService(_proc);
        _ssh = new SshService(_proc);
        _copy = new CopyService(_proc);

        // Восстанавливаем вкладки из настроек или создаём по одной по умолчанию (ph5.9)
        // Restore tabs from settings or create one default tab per side (ph5.9)
        RestoreTabs();

        ActivePanel = LeftPanel; LeftPanel.IsActive = true;
        foreach (var tab in LeftTabs) tab.Panel.PropertyChanged += Panel_PropertyChanged;
        foreach (var tab in RightTabs) tab.Panel.PropertyChanged += Panel_PropertyChanged;
        Git = new GitViewModel(_git, () => ActivePanel.CurrentPath);
        Git.OpenFileAsync = async (p) =>
        {
            try { OpenEditorRequest?.Invoke(p, await File.ReadAllTextAsync(p)); }
            catch (Exception ex) { StatusText = string.Format(L10n("Status.Error"), ex.Message); }
        };
        Git.OpenDiffAsync = async (name, content) => { OpenViewerRequest?.Invoke(name, content); };
        Git.PromptFunc = (title, prompt, def) => Prompt(title, prompt, def ?? "");
        Docker = new DockerViewModel(_docker);
        Ssh = new SshViewModel(_ssh);
        Sftp = new SftpViewModel(_ssh, new SftpService(), () => ActivePanel.CurrentPath, (t, m) => Prompt(t, m));
        CloudStorage = new CloudStorageViewModel(new CloudStorageService(), () => ActivePanel.CurrentPath, (t, m) => Prompt(t, m));
        RegisterQuickCommands();

        // Инициализируем менеджер плагинов (ph8.3) / Initialize plugin manager (ph8.3)
        PluginManager.Instance.Initialize(_commands, this);

        _queue.QueueChanged += (_, _) => {
            OperationQueueActiveCount = _queue.ActiveCount;
            OperationQueueTotalCount = _queue.ActiveCount + _queue.PendingCount;
        };
        CommandResults = new ObservableCollection<QuickCommand>(_commands.Commands);
        NewTerminalTab();

        // При смене темы перечитываем все панели вкладок, чтобы цвета имён файлов
        // (конвертеры GitState/DirColor, использующие FgLightBrush) обновились.
        ((App)Application.Current).ThemeChanged += (_, _) =>
        {
            foreach (var t in LeftTabs) _ = t.Panel.RefreshAsync();
            foreach (var t in RightTabs) _ = t.Panel.RefreshAsync();
        };
    }

    // ═══════════════════════════════════════════
    // УПРАВЛЕНИЕ ВКЛАДКАМИ (ph5.9) / TAB MANAGEMENT (ph5.9)
    // ═══════════════════════════════════════════

    /// <summary>
    /// Восстанавливает вкладки из настроек или создаёт по одной по умолчанию.
    /// Restores tabs from settings or creates one default tab per side.
    /// </summary>
    private void RestoreTabs()
    {
        var s = SettingsService.Load();
        // Левая панель / Left panel
        var leftPaths = s.LeftTabPaths;
        if (leftPaths.Count > 0)
        {
            foreach (var p in leftPaths)
            {
                var pv = new PanelViewModel(_git);
                LeftTabs.Add(new TabViewModel(pv));
                if (System.IO.Directory.Exists(p)) _ = pv.NavigateToAsync(p, false);
            }
        }
        else
        {
            var pv = new PanelViewModel(_git);
            LeftTabs.Add(new TabViewModel(pv));
            _ = pv.NavigateToAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), false);
        }
        ActiveLeftTab = LeftTabs[0];

        // Правая панель / Right panel
        var rightPaths = s.RightTabPaths;
        if (rightPaths.Count > 0)
        {
            foreach (var p in rightPaths)
            {
                var pv = new PanelViewModel(_git);
                RightTabs.Add(new TabViewModel(pv));
                if (System.IO.Directory.Exists(p)) _ = pv.NavigateToAsync(p, false);
            }
        }
        else
        {
            var pv = new PanelViewModel(_git);
            RightTabs.Add(new TabViewModel(pv));
            _ = pv.NavigateToAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), false);
        }
        ActiveRightTab = RightTabs[0];
    }

    /// <summary>
    /// Сохраняет пути вкладок в настройки.
    /// Saves tab paths to settings.
    /// </summary>
    private void SaveTabs()
    {
        var s = SettingsService.Load();
        s.LeftTabPaths = LeftTabs.Select(t => t.Panel.CurrentPath).ToList();
        s.RightTabPaths = RightTabs.Select(t => t.Panel.CurrentPath).ToList();
        SettingsService.Save(s);
    }

    /// <summary>При смене активной левой вкладки обновляем LeftPanel и ActivePanel при необходимости.</summary>
    partial void OnActiveLeftTabChanged(TabViewModel? value)
    {
        OnPropertyChanged(nameof(LeftPanel));
        if (value?.Panel is not null && ActivePanel is not null)
        {
            // Если активна левая сторона — переключаем ActivePanel на новую панель
            // If the left side is active, switch ActivePanel to the new tab's panel
            if (ActivePanel != RightPanel)
            {
                ActivePanel.IsActive = false;
                ActivePanel = value.Panel;
                ActivePanel.IsActive = true;
                Upd();
            }
        }
    }

    /// <summary>При смене активной правой вкладки обновляем RightPanel и ActivePanel при необходимости.</summary>
    partial void OnActiveRightTabChanged(TabViewModel? value)
    {
        OnPropertyChanged(nameof(RightPanel));
        if (value?.Panel is not null && ActivePanel is not null)
        {
            // Если активна правая сторона — переключаем ActivePanel на новую панель
            // If the right side is active, switch ActivePanel to the new tab's panel
            if (ActivePanel != LeftPanel)
            {
                ActivePanel.IsActive = false;
                ActivePanel = value.Panel;
                ActivePanel.IsActive = true;
                Upd();
            }
        }
    }

    /// <summary>Добавляет новую вкладку в левую панель. / Adds a new tab to the left panel.</summary>
    [RelayCommand]
    private void AddLeftTab()
    {
        var panel = new PanelViewModel(_git);
        panel.PropertyChanged += Panel_PropertyChanged;
        var tab = new TabViewModel(panel);
        LeftTabs.Add(tab);
        ActiveLeftTab = tab;
        _ = panel.NavigateToAsync(ActivePanel?.CurrentPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), false);
    }

    /// <summary>Добавляет новую вкладку в правую панель. / Adds a new tab to the right panel.</summary>
    [RelayCommand]
    private void AddRightTab()
    {
        var panel = new PanelViewModel(_git);
        panel.PropertyChanged += Panel_PropertyChanged;
        var tab = new TabViewModel(panel);
        RightTabs.Add(tab);
        ActiveRightTab = tab;
        _ = panel.NavigateToAsync(ActivePanel?.CurrentPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), false);
    }

    /// <summary>Закрывает вкладку левой панели (минимум 1 вкладка сохраняется).</summary>
    [RelayCommand]
    private void CloseLeftTab(TabViewModel? tab)
    {
        if (tab is null) return;
        if (LeftTabs.Count <= 1) return; // Не закрываем последнюю вкладку
        var wasActive = ActiveLeftTab == tab;
        tab.Panel.PropertyChanged -= Panel_PropertyChanged;
        tab.Panel.Dispose();
        LeftTabs.Remove(tab);
        if (wasActive)
            ActiveLeftTab = LeftTabs.LastOrDefault();
    }

    /// <summary>Закрывает вкладку правой панели (минимум 1 вкладка сохраняется).</summary>
    [RelayCommand]
    private void CloseRightTab(TabViewModel? tab)
    {
        if (tab is null) return;
        if (RightTabs.Count <= 1) return;
        var wasActive = ActiveRightTab == tab;
        tab.Panel.PropertyChanged -= Panel_PropertyChanged;
        tab.Panel.Dispose();
        RightTabs.Remove(tab);
        if (wasActive)
            ActiveRightTab = RightTabs.LastOrDefault();
    }

    /// <summary>Закрывает все вкладки левой панели, кроме указанной. / Closes all left tabs except the specified one.</summary>
    [RelayCommand]
    private void CloseOtherLeftTabs(TabViewModel? tab)
    {
        if (tab is null) return;
        foreach (var t in LeftTabs.Where(t => t != tab).ToList())
        {
            t.Panel.PropertyChanged -= Panel_PropertyChanged;
            t.Panel.Dispose();
            LeftTabs.Remove(t);
        }
        ActiveLeftTab = tab;
    }

    /// <summary>Закрывает все вкладки правой панели, кроме указанной. / Closes all right tabs except the specified one.</summary>
    [RelayCommand]
    private void CloseOtherRightTabs(TabViewModel? tab)
    {
        if (tab is null) return;
        foreach (var t in RightTabs.Where(t => t != tab).ToList())
        {
            t.Panel.PropertyChanged -= Panel_PropertyChanged;
            t.Panel.Dispose();
            RightTabs.Remove(t);
        }
        ActiveRightTab = tab;
    }

    /// <summary>Переключает активную вкладку левой панели на следующую (Ctrl+Tab из левой панели).</summary>
    [RelayCommand]
    private void NextLeftTab()
    {
        if (LeftTabs.Count <= 1) return;
        var idx = ActiveLeftTab is not null ? LeftTabs.IndexOf(ActiveLeftTab) : -1;
        idx = (idx + 1) % LeftTabs.Count;
        ActiveLeftTab = LeftTabs[idx];
    }

    /// <summary>Переключает активную вкладку правой панели на следующую (Ctrl+Tab из правой панели).</summary>
    [RelayCommand]
    private void NextRightTab()
    {
        if (RightTabs.Count <= 1) return;
        var idx = ActiveRightTab is not null ? RightTabs.IndexOf(ActiveRightTab) : -1;
        idx = (idx + 1) % RightTabs.Count;
        ActiveRightTab = RightTabs[idx];
    }

    /// <summary>Переключает активную вкладку в активной панели (Ctrl+Tab). / Switches the active tab in the active panel.</summary>
    [RelayCommand]
    private void NextActiveTab()
    {
        if (ActivePanel == LeftPanel)
            NextLeftTab();
        else
            NextRightTab();
    }

    private void Panel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelViewModel.SelectedItem))
            Upd();
        else if (e.PropertyName == nameof(PanelViewModel.CurrentPath) && sender == ActivePanel)
        {
            SyncTerminalDir();
            _ = Git.RefreshAsync();
        }
    }

    private void Upd()
    {
        var L = LocalizationService.Current;
        var s = ActivePanel.SelectedItem;
        StatusText = s is null
            ? string.Format(L.GetString("Status.ItemsIn"), ActivePanel.Items.Count, ActivePanel.CurrentPath)
            : s.IsParent
                ? string.Format(L.GetString("Status.ItemsIn"), ActivePanel.Items.Count, ActivePanel.CurrentPath)
                : s.IsDirectory
                    ? string.Format(L.GetString("Status.Dir"), s.Name)
                    : string.Format(L.GetString("Status.File"), s.Name, s.SizeDisplay, s.ModifiedDisplay);
    }

    private void RegisterQuickCommands()
    {
        _commands.Register(new QuickCommand(L10n("Quick.GitStatus"), L10n("Quick.GitStatusDesc"), async _ => { Git.SetVisible(true); await Git.RefreshAsync(); return L10n("Quick.GitStatusResult"); }));
        _commands.Register(new QuickCommand(L10n("Quick.GitCommitAll"), L10n("Quick.GitCommitDesc"), async _ =>
        {
            var p = ActivePanel.CurrentPath; if (!await _git.IsRepositoryAsync(p)) return L10n("Quick.GitNotRepo");
            var msg = Prompt(L10n("Quick.GitCommitAll"), L10n("Quick.GitCommitMsg")); if (string.IsNullOrWhiteSpace(msg)) return L10n("Quick.GitCancelled");
            await _git.AddAsync(p, new[] { "." });
            var r = await _git.CommitAsync(p, msg);
            return r.Success ? L10n("Quick.GitCommitResult") : r.StdErr;
        }));
        _commands.Register(new QuickCommand(L10n("Quick.GitPush"), L10n("Quick.GitPushDesc"), async _ =>
        {
            var p = ActivePanel.CurrentPath; if (!await _git.IsRepositoryAsync(p)) return L10n("Quick.GitNotRepo");
            var r = await _git.PushAsync(p); return r.Success ? L10n("Quick.GitPushResult") : r.StdErr;
        }));
        _commands.Register(new QuickCommand(L10n("Quick.GitPull"), L10n("Quick.GitPullDesc"), async _ =>
        {
            var p = ActivePanel.CurrentPath; if (!await _git.IsRepositoryAsync(p)) return L10n("Quick.GitNotRepo");
            var r = await _git.PullAsync(p); return r.Success ? L10n("Quick.GitPullResult") : r.StdErr;
        }));
        _commands.Register(new QuickCommand(L10n("Quick.DockerPanel"), L10n("Quick.DockerPanelDesc"), async _ => { Docker.SetVisible(true); await Docker.RefreshAsync(); return L10n("Quick.DockerResult"); }));
        _commands.Register(new QuickCommand(L10n("Quick.ComposeUp"), L10n("Quick.ComposeUpDesc"), async _ =>
        {
            var r = await _docker.ComposeUpAsync(ActivePanel.CurrentPath); return r.Success ? L10n("Quick.ComposeUpResult") : r.StdErr;
        }));
        _commands.Register(new QuickCommand(L10n("Quick.ComposeDown"), L10n("Quick.ComposeDownDesc"), async _ =>
        {
            var r = await _docker.ComposeDownAsync(ActivePanel.CurrentPath); return r.Success ? L10n("Quick.ComposeDownResult") : r.StdErr;
        }));
        _commands.Register(new QuickCommand(L10n("Quick.SshPanel"), L10n("Quick.SshPanelDesc"), async _ => { Ssh.SetVisible(true); return L10n("Quick.SshResult"); }));
        _commands.Register(new QuickCommand(L10n("Quick.SftpPanel"), L10n("Quick.SftpPanelDesc"), async _ => { Sftp.SetVisible(true); return L10n("Quick.SftpResult"); }));
        _commands.Register(new QuickCommand(L10n("Quick.CloudStorage"), L10n("Quick.CloudStorageDesc"), async _ => { CloudStorage.SetVisible(true); return L10n("Quick.CloudStorageResult"); }));
        _commands.Register(new QuickCommand(L10n("Quick.TerminalOpen"), L10n("Quick.TerminalOpenDesc"), async _ => { OpenTerminal(); return L10n("Quick.TerminalResult"); }));
    }

    /// <summary>Открыть палитру команд (Ctrl+P).</summary>
    [RelayCommand] public void OpenCommandPalette() { IsCommandPaletteOpen = true; CommandQuery = ""; CommandResults = new ObservableCollection<QuickCommand>(_commands.Commands); }
    /// <summary>Закрыть палитру команд.</summary>
    [RelayCommand] public void CloseCommandPalette() { IsCommandPaletteOpen = false; }

    /// <summary>Обработать нажатие Escape: закрыть палитру, нижнюю панель, терминал или редактор.</summary>
    [RelayCommand]
    public void EscapeKey()
    {
        if (IsCommandPaletteOpen) { IsCommandPaletteOpen = false; return; }
        if (IsBottomPanelVisible) { IsBottomPanelVisible = false; return; }
        if (IsTerminalPanelVisible) { IsTerminalPanelVisible = false; return; }
        CloseEditorRequest?.Invoke();
    }

    /// <summary>Переключить видимость панели терминала.</summary>
    [RelayCommand] public void ToggleTerminalPanel() => IsTerminalPanelVisible = !IsTerminalPanelVisible;

    /// <summary>Показать вкладку терминала.</summary>
    [RelayCommand] public void ShowTerminalTab() { IsTerminalPanelVisible = true; }
    /// <summary>Показать вкладку Git.</summary>
    [RelayCommand]
    public void ShowGitTab()
    {
        SelectedTabIndex = 0;
        Git.SetVisible(true);
        IsBottomPanelVisible = true;
        _ = Git.RefreshAsync();
    }
    /// <summary>Показать вкладку Docker.</summary>
    [RelayCommand]
    public void ShowDockerTab()
    {
        SelectedTabIndex = 1;
        Docker.SetVisible(true);
        IsBottomPanelVisible = true;
        _ = Docker.RefreshAsync();
    }
    /// <summary>Показать вкладку SSH.</summary>
    [RelayCommand] public void ShowSshTab() { SelectedTabIndex = 2; Ssh.SetVisible(true); IsBottomPanelVisible = true; }
    /// <summary>Показать вкладку SFTP.</summary>
    [RelayCommand] public void ShowSftpTab() { SelectedTabIndex = 3; Sftp.SetVisible(true); IsBottomPanelVisible = true; }
    /// <summary>Показать вкладку облачных хранилищ (ph8.4). / Show cloud storage tab.</summary>
    [RelayCommand] public void ShowCloudStorageTab() { SelectedTabIndex = 4; CloudStorage.SetVisible(true); IsBottomPanelVisible = true; }

    /// <summary>Закрыть нижнюю панель (Git/Docker/SSH/SFTP).</summary>
    [RelayCommand] public void CloseBottomPanel() => IsBottomPanelVisible = false;

    /// <summary>Выполнить выбранную быструю команду из палитры.</summary>
    [RelayCommand]
    public async Task RunQuickCommandAsync(QuickCommand? cmd)
    {
        if (cmd is null) return;
        IsCommandPaletteOpen = false;
        StatusText = await _commands.RunAsync(cmd);
    }

    /// <summary>При изменении текста запроса обновить результаты фильтрации команд.</summary>
    partial void OnCommandQueryChanged(string value) => CommandResults = new ObservableCollection<QuickCommand>(_commands.Search(value));

    /// <summary>Переключить активную панель (левая/правая).</summary>
    [RelayCommand] public void SwitchPanel() { ActivePanel.IsActive = false; ActivePanel = ActivePanel == LeftPanel ? RightPanel : LeftPanel; ActivePanel.IsActive = true; Upd(); SyncTerminalDir(); }

    /// <summary>Установить указанную панель как активную.</summary>
    [RelayCommand]
    public void ActivatePanel(PanelViewModel? panel)
    {
        if (panel is null || panel == ActivePanel) return;
        ActivePanel.IsActive = false;
        ActivePanel = panel;
        ActivePanel.IsActive = true;
        Upd();
        SyncTerminalDir();
    }

    private void SyncTerminalDir()
    {
        if (ActiveTerminalTab is null) return;
        if (TerminalServices.TryGetValue(ActiveTerminalTab.Id, out var svc) && svc.IsRunning)
            svc.ChangeDirectory(ActivePanel.CurrentPath);
    }

    /// <summary>Открыть выбранный элемент: папку, текстовый файл (в редакторе) или внешней программой.</summary>
    [RelayCommand]
    public async Task OpenItemAsync()
    {
        var i = ActivePanel.SelectedItem; if (i is null) return;
        if (i.IsParent) { await ActivePanel.GoUpAsync(); return; }
        if (i.IsDirectory) { await ActivePanel.NavigateToAsync(i.FullPath); }
        else if (FileService.IsTextFile(i.FullPath)) { await EditFileAsync(); }
        else FileService.OpenWithDefault(i.FullPath);
    }

    /// <summary>Открыть выбранный файл в режиме просмотра (только чтение, F3).
    /// Для изображений — content=null (EditorWindow определит тип сам).
    /// </summary>
    [RelayCommand]
    public async Task ViewFileAsync()
    {
        var i = ActivePanel.SelectedItem; if (i is null || i.IsDirectory) return;
        try
        {
            string? content = null;
            if (IsImageFile(i.FullPath))
            {
                // Изображения передаём без текстового содержимого — EditorWindow покажет просмотрщик
                // Images are passed without text content — EditorWindow will show the image viewer
                content = null;
            }
            else
            {
                content = await File.ReadAllTextAsync(i.FullPath);
            }
            OpenViewerRequest?.Invoke(i.FullPath, content ?? "");
        }
        catch (Exception ex) { StatusText = string.Format(L10n("Status.Error"), ex.Message); }
    }

    /// <summary>Открыть выбранный файл в редакторе (F4).
    /// Для изображений — content=null (EditorWindow покажет просмотрщик изображений).
    /// </summary>
    [RelayCommand]
    public async Task EditFileAsync()
    {
        var i = ActivePanel.SelectedItem; if (i is null || i.IsDirectory) return;
        try
        {
            string? content = null;
            if (IsImageFile(i.FullPath))
            {
                // Изображения открываем в просмотрщике (F4 не редактирует изображения)
                // Images open in viewer (F4 does not edit images)
                OpenViewerRequest?.Invoke(i.FullPath, "");
                return;
            }
            content = await File.ReadAllTextAsync(i.FullPath);
            OpenEditorRequest?.Invoke(i.FullPath, content);
        }
        catch (Exception ex) { StatusText = string.Format(L10n("Status.Error"), ex.Message); }
    }

    /// <summary>Проверяет, является ли файл изображением по расширению.
    /// Checks if a file is an image by extension.</summary>
    private static bool IsImageFile(string path)
    {
        var ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp"
            or ".ico" or ".tiff" or ".tif" or ".webp" or ".svg";
    }

    /// <summary>Сохранить изменения в редакторе (обрабатывается через popup-окно).</summary>
    [RelayCommand] public async Task SaveEditorAsync() { /* Сохранение через popup-окно */ }
    /// <summary>Закрыть редактор (обрабатывается через popup-окно).</summary>
    [RelayCommand] public void CloseEditor() { /* Закрытие через popup-окно */ }

    //========== Меню: команды ==========
    ///<summary>Корректный выход приложения.</summary>
    [RelayCommand] public void ExitApp() => Application.Current.Shutdown();

    /// <summary>Open operation queue window (ph5.2).</summary>
    [RelayCommand] private void OpenOperationQueue() => OpenOperationQueueRequest?.Invoke();

    ///<summary>Установить тему.</summary>
    [RelayCommand]
    public void SetTheme(string themeName)
    {
        var mode = themeName?.ToLowerInvariant() switch
        {
            "dark" => ThemeMode.Dark,
            "light" => ThemeMode.Light,
            "system" => ThemeMode.System,
            _ => ThemeMode.Dark
        };
        
        var settings = SettingsService.Load();
        settings.Theme = mode;
        SettingsService.Save(settings);
        
        // Применяем тему немедленно
        ((App)Application.Current).ApplyTheme(mode);
        
        // Обновляем свойство для меню
        Theme = themeName ?? "Dark";
    }

    ///<summary>Показать окно «О программе».</summary>
    [RelayCommand]
    public void About()
    {
        ActivePanel.SaveFocus();
        var win = new Views.AboutWindow { Owner = Application.Current.MainWindow };
        win.ShowDialog();
        ActivePanel.RestoreFocus();
    }

    ///<summary>Открыть диалог настроек.</summary>
    [RelayCommand]
    public void OpenSettings()
    {
        ActivePanel.SaveFocus();
        var settingsWindow = new CoderCommander.Views.SettingsWindow
        {
            Owner = Application.Current.MainWindow
        };
        settingsWindow.ShowDialog();
        ActivePanel.RestoreFocus();

        // Применяем сохранённые настройки к работающему приложению
        // (тему/язык уже применило само окно настроек в Save_Click).
        // Apply saved settings to the running app
        // (theme/language were already applied by the settings window in Save_Click).
        ApplySettings();
    }

    /// <summary>
    /// Применяет сохранённые настройки к работающему приложению: оболочку терминала по умолчанию,
    /// отображение скрытых файлов в панелях и т.п. Вызывается после закрытия окна настроек.
    /// Applies saved settings to the running app: default terminal shell, hidden-files visibility
    /// in panels, etc. Called after the settings dialog is closed.
    /// </summary>
    private void ApplySettings()
    {
        var s = SettingsService.Load();
        TerminalShell = s.TerminalShell;
        ApplyShowHidden(s.ShowHidden);
        Theme = s.Theme.ToString();
    }

    /// <summary>
    /// Синхронизирует флаг «показывать скрытые файлы» обеих панелей с заданным значением
    /// и обновляет их содержимое при изменении.
    /// Syncs the "show hidden files" flag of both panels with the given value and refreshes
    /// their contents when it changes.
    /// </summary>
    private void ApplyShowHidden(bool showHidden)
    {
        foreach (var tab in LeftTabs)
        {
            if (tab.Panel.ShowHidden != showHidden)
            {
                tab.Panel.ShowHidden = showHidden;
                _ = tab.Panel.RefreshAsync();
            }
        }
        foreach (var tab in RightTabs)
        {
            if (tab.Panel.ShowHidden != showHidden)
            {
                tab.Panel.ShowHidden = showHidden;
                _ = tab.Panel.RefreshAsync();
            }
        }
    }

    /// <summary>Копировать выделенные элементы в другую панель (F5).</summary>
    [RelayCommand]
    public void Copy()
    {
        if (IsBusy) return;
        var it = ActivePanel.GetSelectionOrCurrent().ToList(); if (it.Count == 0) return;
        var td = (ActivePanel == LeftPanel ? RightPanel : LeftPanel).CurrentPath;
        ShowCopyMoveDialog(it, td, false);
    }

    /// <summary>Переместить выделенные элементы в другую панель (F6).</summary>
    [RelayCommand]
    public void Move()
    {
        if (IsBusy) return;
        var it = ActivePanel.GetSelectionOrCurrent().ToList(); if (it.Count == 0) return;
        var td = (ActivePanel == LeftPanel ? RightPanel : LeftPanel).CurrentPath;
        ShowCopyMoveDialog(it, td, true);
    }

    private void ShowCopyMoveDialog(List<FileSystemItem> items, string defaultDest, bool isMove, string? srcDir = null)
    {
        srcDir ??= ActivePanel.CurrentPath;
        var dlg = new Views.CopyMoveDialog();
        var filePaths = items.Select(i => i.FullPath).ToList();
        dlg.Initialize(!isMove, srcDir, defaultDest, filePaths);
        dlg.Owner = Application.Current.MainWindow;

        var vm = (ViewModels.CopyMoveDialogViewModel)dlg.DataContext!;
        vm.ExecuteRequested = () =>
        {
            var destDir = vm.DestinationPath;
            if (vm.AddToQueue)
                EnqueueCopyMove(items, destDir, isMove, vm.SelectedOverwritePolicy, vm.CopyAttributes, vm.CopyTimestamps, vm.CopyNtfsPermissions);
            else
                _ = Cm(items, destDir, isMove, vm.SelectedOverwritePolicy, vm.CopyAttributes, vm.CopyTimestamps, vm.CopyNtfsPermissions);
        };

        // Сохраняем фокус перед открытием диалога
        ActivePanel.SaveFocus();
        dlg.Closed += (_, _) => ActivePanel.RestoreFocus();

        dlg.Show();
    }

    private async Task Cm(List<FileSystemItem> it, string td, bool mv, OverwritePolicy policy,
        bool copyAttributes, bool copyTimestamps, bool copyNtfsPermissions)
    {
        var settings = SettingsService.Load();
        var bufferSize = settings.CopyBufferSizeKB * 1024;

        var lfs = new LocalFileSystem();
        var sources = it.Select(i => i.FullPath).ToList();
        var options = new TransferOptions
        {
            BufferSize = bufferSize,
            CopyAttributes = copyAttributes,
            CopyTimestamps = copyTimestamps,
            ReserveDiskSpace = settings.ReserveDiskSpace,
            CopyNtfsPermissions = copyNtfsPermissions
        };

        TransferOperation op = null!;
        Func<string, string, OverwritePolicy>? conflictCb = null;
        if (policy == OverwritePolicy.Ask)
        {
            conflictCb = (src, dst) =>
            {
                try
                {
                    OverwritePolicy result = OverwritePolicy.Skip;
                    bool applyToAll = false;
                    var ev = new System.Threading.ManualResetEventSlim(false);
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var dlg = new Views.OverwriteDialog(Path.GetFileName(src), src, dst)
                            { Owner = Application.Current.MainWindow };
                            if (dlg.ShowDialog() == true)
                            {
                                result = dlg.Result;
                                applyToAll = dlg.ApplyToAll;
                            }
                        }
                        finally { ev.Set(); }
                    }));
                    ev.Wait();
                    if (applyToAll) op.SetCachedAskPolicy(result);
                    return result;
                }
                catch { return OverwritePolicy.Skip; }
            };
        }

        if (mv)
            op = new MoveOperation(lfs, sources, td, policy, conflictCb, null, options);
        else
            op = new CopyOperation(lfs, sources, td, policy, conflictCb, null, options);

        var title = mv ? L10n("OpDlg.Title.Move") : L10n("OpDlg.Title.Copy");
        var progressVm = new ViewModels.OperationDialogViewModel(op, title, ActivePanel.CurrentPath, td);
        options.PauseEvent = progressVm.PauseEvent;
        options.SkipCurrentFileFunc = () => progressVm.TrySkipCurrentFile();

        var progressDlg = new Views.OperationDialogWindow { DataContext = progressVm, Owner = Application.Current.MainWindow };

        IsBusy = true;
        try
        {
            // Операция в фоне, окно прогресса модально блокирует UI.
            // Operation runs in background, progress dialog blocks UI modally.
            var executeTask = Task.Run(() => op.ExecuteAsync(progressVm.CancellationToken));

            progressDlg.ShowDialog();

            // Дожидаемся завершения операции после закрытия окна.
            // Await operation completion after dialog closes.
            try { await executeTask; } catch (OperationCanceledException) { }

            if (op.State == OperationState.Completed)
            {
                var done = (int)op.Copied;
                if (op.Skipped > 0 || op.Failed > 0)
                    StatusText = $"{(mv ? L10n("Status.Moved") : L10n("Status.Copied"))}{done}, skip: {op.Skipped}, err: {op.Failed}";
                else
                    StatusText = (mv ? L10n("Status.Moved") : L10n("Status.Copied")) + done;
            }
            else if (op.State == OperationState.Canceled)
                StatusText = L10n("CrossVfs.Cancelled");
            else if (op.State == OperationState.Failed)
                StatusText = string.Format(L10n("Status.Error"), op.LastError?.Message ?? "Unknown");
        }
        finally
        {
            progressVm.Dispose();
            IsBusy = false; ProgressValue = 0; ProgressText = "";
        }

        await ActivePanel.RefreshAsync();
        await (ActivePanel == LeftPanel ? RightPanel : LeftPanel).RefreshAsync();
        await SyncActiveVirtualPanelAsync();
    }

    /// <summary>Создать новую папку в текущей директории (F7).</summary>
    [RelayCommand]
    public async Task CreateFolderAsync()
    {
        var n = Prompt(L10n("Dialog.NewFolderTitle"), L10n("Dialog.NewFolderName"));
        if (string.IsNullOrWhiteSpace(n)) return;
        try { Directory.CreateDirectory(Path.Combine(ActivePanel.CurrentPath, n)); await ActivePanel.RefreshAsync(); }
        catch (Exception e) { StatusText = e.Message; }
    }

    /// <summary>Удалить выделенные элементы (F8, Del).</summary>
    [RelayCommand]
    public async Task DeleteAsync()
    {
        var it = ActivePanel.GetSelectionOrCurrent().ToList(); if (it.Count == 0) return;
        // Подтверждение удаления только если разрешено в настройках.
        // Confirm deletion only when allowed by settings.
        if (SettingsService.Load().ConfirmDelete &&
            StyledMessageBoxWindow.Show(string.Format(LocalizationService.Current.GetString("Dialog.ConfirmDelete"), it.Count), LocalizationService.Current.GetString("Dialog.ConfirmDeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        IsBusy = true; int n = 0;
        try
        {
            foreach (var i in it)
            {
                ProgressText = L10n("Operation.Deleting") + " " + i.Name;
                if (i.IsDirectory) Directory.Delete(i.FullPath, true); else File.Delete(i.FullPath);
                n++; ProgressValue = n * 100.0 / it.Count;
            }
            await ActivePanel.RefreshAsync(); StatusText = string.Format(LocalizationService.Current.GetString("Status.Deleted"), it.Count);
            await SyncActiveVirtualPanelAsync(); // ph2.2: удалённые выпадают из результатов
        }
        catch (Exception e) { StatusText = string.Format(LocalizationService.Current.GetString("Status.DeletedOf"), n, it.Count, e.Message); }
        finally { IsBusy = false; ProgressValue = 0; ProgressText = ""; }
    }

    /// <summary>Переименовать выбранный элемент.</summary>
    [RelayCommand]
    public async Task RenameAsync()
    {
        var i = ActivePanel.SelectedItem; if (i is null || i.IsParent) return;
        var oldPath = i.FullPath; // ph2.2: нужен для обновления виртуальной панели результатов
        var n = Prompt(L10n("Dialog.RenameTitle"), L10n("Dialog.RenameName"), i.Name);
        if (string.IsNullOrWhiteSpace(n)) return;
        try
        {
            var np = Path.Combine(Path.GetDirectoryName(i.FullPath)!, n);
            if (i.IsDirectory) Directory.Move(i.FullPath, np); else File.Move(i.FullPath, np);
            await SyncActiveVirtualPanelAsync(oldPath, np); // ph2.2: обновить путь в результатах
            await ActivePanel.RefreshAsync();
        }
        catch (Exception e) { StatusText = e.Message; }
    }

    /// <summary>Обновить все панели всех вкладок.</summary>
    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        foreach (var t in LeftTabs) await t.Panel.RefreshAsync();
        foreach (var t in RightTabs) await t.Panel.RefreshAsync();
    }
    /// <summary>Переключить отображение скрытых файлов.</summary>
    [RelayCommand] public async Task ToggleHiddenAsync() { ActivePanel.ShowHidden = !ActivePanel.ShowHidden; await ActivePanel.RefreshAsync(); }

    /// <summary>Открыть панель терминала.</summary>
    [RelayCommand]
    public void OpenTerminal()
    {
        IsTerminalPanelVisible = true;
        if (TerminalTabs.Count == 0) NewTerminalTab();
    }

    /// <summary>Создать новую вкладку терминала с типом shell по умолчанию.</summary>
    [RelayCommand]
    public void NewTerminalTab()
    {
        NewTerminalTabWithShell(TerminalShell);
    }
    
    /// <summary>Создаёт новую вкладку терминала с указанным shell.</summary>
    public void NewTerminalTabWithShell(string shell)
    {
        var tab = new TerminalTabViewModel(shell);
        TerminalTabs.Add(tab);
        RenumberTerminalTabs();
        ActiveTerminalTab = tab;
    }

    /// <summary>Закрыть указанную вкладку терминала и освободить ресурсы.</summary>
    [RelayCommand]
    public async Task CloseTerminalTab(TerminalTabViewModel? tab)
    {
        if (tab is null) return;
        if (TerminalServices.TryGetValue(tab.Id, out var svc))
        {
            await svc.StopAsync(); svc.Dispose(); TerminalServices.Remove(tab.Id);
        }
        TerminalTabs.Remove(tab);
        RenumberTerminalTabs();
        ActiveTerminalTab = TerminalTabs.LastOrDefault();
        if (TerminalTabs.Count == 0) IsTerminalOpen = false;
    }
    
    /// <summary>Пересортировывает вкладки по Id и обновляет отображаемые номера.</summary>
    private void RenumberTerminalTabs()
    {
        var sorted = TerminalTabs.OrderBy(t => t.Id).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].DisplayNumber = i + 1;
            sorted[i].UpdateTitle();
        }
    }

    ///<summary>Запускает нативный терминал для вкладки с указанным HWND контейнера.</summary>
    public async Task StartTerminalForTabAsync(TerminalTabViewModel tab, IntPtr hostHwnd)
    {
        if (tab is null || hostHwnd == IntPtr.Zero) return;
        if (TerminalServices.TryGetValue(tab.Id, out var old)) { await old.StopAsync(); old.Dispose(); }

        var svc = new ChildConsoleService();
        await svc.StartAsync(tab.Shell ?? TerminalShell, ActivePanel.CurrentPath, hostHwnd);
        TerminalServices[tab.Id] = svc;
    }

    ///<summary>Передаёт фокус в консоль активной вкладки.</summary>
    public void FocusTerminal()
    {
        if (ActiveTerminalTab is null) return;
        if (TerminalServices.TryGetValue(ActiveTerminalTab.Id, out var svc)) svc.Focus();
    }

    ///<summary>Меняет размер консоли для указанной вкладки.</summary>
    public void ResizeTerminal(int tabId, int width, int height)
    {
        if (TerminalServices.TryGetValue(tabId, out var svc)) svc.Resize(width, height);
    }

    /// <summary>Закрыть все вкладки терминала.</summary>
    [RelayCommand]
    public async Task CloseTerminal()
    {
        foreach (var tab in TerminalTabs.ToList()) await CloseTerminalTab(tab);
    }

    /// <summary>Перейти к указанному пути из закладок.</summary>
    [RelayCommand] public async Task GoToBookmarkAsync(string p) { if (Directory.Exists(p)) await ActivePanel.NavigateToAsync(p); }

    /// <summary>Опубликовать выбранный файл/папку через SSH.</summary>
    [RelayCommand]
    public async Task PublishSelectedAsync()
    {
        var i = ActivePanel.SelectedItem; if (i is null || i.IsParent) return;
        if (Ssh.Profiles.Count == 0) { Ssh.SetVisible(true); StatusText = L10n("Status.NoSshProfiles"); return; }
        var prof = Ssh.SelectedProfile ?? Ssh.Profiles[0];
        await Ssh.PublishAsync(prof, i.FullPath);
        StatusText = Ssh.Status;
    }

    /// <summary>Выделить все файлы.</summary>
    [RelayCommand] public void SelectAll() { foreach (var item in ActivePanel.Items.Where(i => !i.IsParent)) item.IsSelected = true; Upd(); }
    
    /// <summary>Снять выделение.</summary>
    [RelayCommand] public void DeselectAll() { foreach (var item in ActivePanel.Items) item.IsSelected = false; Upd(); }
    
    /// <summary>Инвертировать выделение.</summary>
    [RelayCommand] public void InvertSelection() { foreach (var item in ActivePanel.Items.Where(i => !i.IsParent)) item.IsSelected = !item.IsSelected; Upd(); }
    
    /// <summary>Выделить по маске.</summary>
    [RelayCommand] public void SelectByPattern() { var pattern = Prompt(L10n("Dialog.SelectFilesTitle"), L10n("Dialog.SelectFilesMask"), "*.*"); if (string.IsNullOrWhiteSpace(pattern)) return; var regex = PatternToRegex(pattern); foreach (var item in ActivePanel.Items.Where(i => !i.IsParent)) { if (regex.IsMatch(item.Name)) item.IsSelected = true; } Upd(); }
    
    /// <summary>Снять выделение по маске.</summary>
    [RelayCommand] public void DeselectByPattern() { var pattern = Prompt(L10n("Dialog.DeselectFilesTitle"), L10n("Dialog.SelectFilesMask"), "*.*"); if (string.IsNullOrWhiteSpace(pattern)) return; var regex = PatternToRegex(pattern); foreach (var item in ActivePanel.Items.Where(i => !i.IsParent)) { if (regex.IsMatch(item.Name)) item.IsSelected = false; } Upd(); }
    
    /// <summary>Сравнить каталоги.</summary>
    [RelayCommand] public void CompareDirectories() { var leftFiles = LeftPanel.Items.Where(i => !i.IsParent && !i.IsDirectory).GroupBy(i => i.Name).ToDictionary(g => g.Key, g => g.First()); var rightFiles = RightPanel.Items.Where(i => !i.IsParent && !i.IsDirectory).GroupBy(i => i.Name).ToDictionary(g => g.Key, g => g.First()); foreach (var item in LeftPanel.Items.Where(i => !i.IsParent)) { if (!rightFiles.ContainsKey(item.Name)) item.IsSelected = true; else if (!item.IsDirectory) { var other = rightFiles[item.Name]; if (item.Size != other.Size || item.Modified != other.Modified) item.IsSelected = true; } } foreach (var item in RightPanel.Items.Where(i => !i.IsParent)) { if (!leftFiles.ContainsKey(item.Name)) item.IsSelected = true; else if (!item.IsDirectory) { var other = leftFiles[item.Name]; if (item.Size != other.Size || item.Modified != other.Modified) item.IsSelected = true; } } StatusText = L10n("Status.Compared"); }
    
    /// <summary>Поиск файлов.</summary>
    [RelayCommand] public async Task SearchFilesAsync() { var pattern = Prompt(L10n("Dialog.SearchTitle"), L10n("Dialog.SearchName"), "*.*"); if (string.IsNullOrWhiteSpace(pattern)) return; var searchText = Prompt(L10n("Dialog.SearchTitle"), L10n("Dialog.SearchText"), ""); IsBusy = true; StatusText = L10n("Status.Searching"); try { var results = new List<string>(); var regex = PatternToRegex(pattern); await Task.Run(() => SearchDirectory(ActivePanel.CurrentPath, regex, searchText, results)); if (results.Count == 0) { StatusText = L10n("Status.NotFound"); StyledMessageBoxWindow.Show(LocalizationService.Current.GetString("Dialog.SearchEmpty"), LocalizationService.Current.GetString("Dialog.SearchTitle"), MessageBoxButton.OK, MessageBoxImage.Information); } else { StatusText = string.Format(L10n("Status.Found"), results.Count); var result = string.Join("\n", results.Take(50)); if (results.Count > 50) result += string.Format(L10n("Dialog.SearchTruncated"), results.Count - 50); StyledMessageBoxWindow.Show(result, string.Format(L10n("Dialog.SearchResults"), results.Count), MessageBoxButton.OK, MessageBoxImage.Information); } } catch (Exception ex) { StatusText = string.Format(L10n("Status.Error"), ex.Message); } finally { IsBusy = false; } }

    /// <summary>Синхронизировать панели.</summary>
    [RelayCommand] public async Task SyncPanelsAsync() { var target = ActivePanel == LeftPanel ? RightPanel : LeftPanel; await target.NavigateToAsync(ActivePanel.CurrentPath); StatusText = L10n("Status.PanelsSynced"); }
    
    /// <summary>Поменять панели местами.</summary>
    [RelayCommand] public void SwapPanels() { (LeftPanel.CurrentPath, RightPanel.CurrentPath) = (RightPanel.CurrentPath, LeftPanel.CurrentPath); _ = LeftPanel.RefreshAsync(); _ = RightPanel.RefreshAsync(); StatusText = L10n("Status.PanelsSwapped"); }
    
    /// <summary>Показать информацию о дисках.</summary>
    [RelayCommand] public void ShowDiskInfo() { try { var drives = DriveInfo.GetDrives(); var msg = string.Join("\n\n", drives.Select(d => { var name = d.Name.TrimEnd('\\'); var type = d.DriveType switch { DriveType.Fixed => L10n("Disk.Type.Fixed"), DriveType.Removable => L10n("Disk.Type.Removable"), DriveType.CDRom => L10n("Disk.Type.CDRom"), DriveType.Network => L10n("Disk.Type.Network"), DriveType.Ram => L10n("Disk.Type.Ram"), _ => L10n("Disk.Type.Unknown") }; if (!d.IsReady) return $"■ {name}  [{type}]  {L10n("Disk.NotReady")}"; var label = string.IsNullOrEmpty(d.VolumeLabel) ? "—" : d.VolumeLabel; var free = d.AvailableFreeSpace; var total = d.TotalSize; var used = total - free; var pct = total > 0 ? (int)(used * 100.0 / total) : 0; return $"■ {name}  [{type}]  {d.DriveFormat}\n  {L10n("Disk.Label")}: {label}\n  {L10n("Disk.Total")}: {FormatBytes(total)}\n  {L10n("Disk.Used")}: {FormatBytes(used)} ({pct}%)\n  {L10n("Disk.Free")}: {FormatBytes(free)} ({100 - pct}%)"; })); StyledMessageBoxWindow.Show(msg, L10n("Disk.Title"), MessageBoxButton.OK, MessageBoxImage.Information); } catch (Exception ex) { StatusText = string.Format(L10n("Status.Error"), ex.Message); } }
    
    private static string FormatBytes(long bytes) { string[] u = ["B", "KB", "MB", "GB", "TB"]; double s = bytes; int i = 0; while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; } return $"{s:0.##} {u[i]}"; }
    
    private static System.Text.RegularExpressions.Regex PatternToRegex(string pattern) { var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$"; return new System.Text.RegularExpressions.Regex(regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
    
    private void SearchDirectory(string path, System.Text.RegularExpressions.Regex regex, string? searchText, List<string> results) { if (results.Count >= 1000) return; try { foreach (var file in Directory.GetFiles(path)) { var name = Path.GetFileName(file); if (regex.IsMatch(name)) { if (string.IsNullOrWhiteSpace(searchText)) { results.Add(file); } else if (FileService.IsTextFile(file)) { try { var content = File.ReadAllText(file); if (content.Contains(searchText, StringComparison.OrdinalIgnoreCase)) results.Add(file); } catch { } } } } foreach (var dir in Directory.GetDirectories(path)) { SearchDirectory(dir, regex, searchText, results); } } catch { } }

    private static string? Prompt(string title, string prompt, string def = "")
    {
        var w = new Window
        {
            Title = title, Width = 400, Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current.MainWindow
        };
        var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
        sp.Children.Add(new System.Windows.Controls.TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
        var tb = new System.Windows.Controls.TextBox { Text = def };
        sp.Children.Add(tb);
        var btns = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new System.Windows.Controls.Button { Content = LocalizationService.Current.GetString("MsgBox.OK"), Width = 80, IsDefault = true };
        var cn = new System.Windows.Controls.Button { Content = LocalizationService.Current.GetString("Dialog.Cancel"), Width = 80, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, _) => w.DialogResult = true;
        cn.Click += (_, _) => w.DialogResult = false;
        btns.Children.Add(ok); btns.Children.Add(cn);
        sp.Children.Add(btns);
        w.Content = sp;
        tb.SelectAll(); tb.Focus();
        return w.ShowDialog() == true ? tb.Text : null;
    }

    /// <summary>
    /// Освобождает ресурсы: останавливает терминальные сессии, сохраняет вкладки, освобождает панели.
    /// Releases resources: stops terminal sessions, saves tabs, disposes panels.
    /// </summary>
    public void Dispose()
    {
        foreach (var tab in LeftTabs) tab.Panel.PropertyChanged -= Panel_PropertyChanged;
        foreach (var tab in RightTabs) tab.Panel.PropertyChanged -= Panel_PropertyChanged;

        foreach (var svc in TerminalServices.Values)
        {
            try { svc.Dispose(); } catch { }
        }
        TerminalServices.Clear();

        // Сохраняем вкладки перед выходом (ph5.9)
        // Save tabs before exit (ph5.9)
        SaveTabs();

        foreach (var tab in LeftTabs) tab.Panel.Dispose();
        foreach (var tab in RightTabs) tab.Panel.Dispose();
    }
}
