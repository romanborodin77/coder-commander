using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Модель представления для Git-панели: статус, diff, commit, push/pull, ветки, лог.
/// ViewModel for the Git panel: status, diff, commit, push/pull, branches, log.
/// </summary>
public partial class GitViewModel : ObservableObject
{
    private readonly GitService _git;
    private readonly Func<string?> _getPath;
    private bool _reloading;

    /// <summary>Истина, когда идёт перезагрузка данных git (для блокировки обработчиков UI).</summary>
    public bool IsReloading => _reloading;

    /// <summary>
    /// Создаёт экземпляр GitViewModel с указанным сервисом Git и функцией получения пути к репозиторию.
    /// Creates a GitViewModel instance with the specified Git service and repository path provider.
    /// </summary>
    /// <param name="git">Сервис Git для выполнения команд.</param>
    /// <param name="getPath">Функция, возвращающая путь к текущему репозиторию.</param>
    public GitViewModel(GitService git, Func<string?> getPath)
    {
        _git = git;
        _getPath = getPath;
        IsVisible = false;
    }

    /// <summary>Делегат для открытия файла в редакторе (асинхронный).</summary>
    public Func<string, Task>? OpenFileAsync { get; set; }
    /// <summary>Делегат для открытия diff в просмотрщике (асинхронный).</summary>
    public Func<string, string, Task>? OpenDiffAsync { get; set; }
    /// <summary>Делегат для отображения диалога ввода (заголовок, подпись, значение по умолчанию).</summary>
    public Func<string, string, string?, string?>? PromptFunc { get; set; }

    /// <summary>Видимость Git-панели.</summary>
    [ObservableProperty] private bool _isVisible;
    /// <summary>Флаг, является ли текущая директория Git-репозиторием.</summary>
    [ObservableProperty] private bool _isRepo;
    /// <summary>Флаг выполнения операции (блокировка UI).</summary>
    [ObservableProperty] private bool _isBusy;
    /// <summary>Текущая ветка.</summary>
    [ObservableProperty] private string _branch = "";
    /// <summary>Количество коммитов впереди удалённой ветки.</summary>
    [ObservableProperty] private int _ahead;
    /// <summary>Количество коммитов позади удалённой ветки.</summary>
    [ObservableProperty] private int _behind;
    /// <summary>Список изменённых файлов.</summary>
    [ObservableProperty] private ObservableCollection<GitFileStatus> _files = new();
    /// <summary>Выбранный файл для просмотра diff.</summary>
    [ObservableProperty] private GitFileStatus? _selectedFile;
    /// <summary>Сырой текст diff.</summary>
    [ObservableProperty] private string _diff = "";
    /// <summary>Разобранные строки diff для построчного отображения.</summary>
    [ObservableProperty] private ObservableCollection<DiffLine> _diffLines = new();
    /// <summary>Заголовок диффа (имя файла или хеш коммита).</summary>
    [ObservableProperty] private string _diffTitle = LocalizationService.Current.GetString("Git.DiffTitle");
    /// <summary>Список веток репозитория.</summary>
    [ObservableProperty] private ObservableCollection<string> _branches = new();
    /// <summary>Лог коммитов.</summary>
    [ObservableProperty] private ObservableCollection<GitLogEntry> _log = new();
    /// <summary>Выбранный коммит из лога.</summary>
    [ObservableProperty] private GitLogEntry? _selectedCommit;
    /// <summary>Текст сообщения для нового коммита.</summary>
    [ObservableProperty] private string _commitMessage = "";
    /// <summary>Флаг «поправить последний коммит» (--amend).</summary>
    [ObservableProperty] private bool _amend;
    /// <summary>Строка статуса для отображения в UI.</summary>
    [ObservableProperty] private string _status = "";
    /// <summary>Количество проиндексированных (staged) файлов.</summary>
    [ObservableProperty] private int _stagedCount;
    /// <summary>Количество неиндексированных (unstaged) файлов.</summary>
    [ObservableProperty] private int _unstagedCount;

    /// <summary>
    /// Устанавливает видимость панели и обновляет данные Git при показе.
    /// Sets panel visibility and refreshes Git data when shown.
    /// </summary>
    /// <param name="v">Новое состояние видимости.</param>
    public void SetVisible(bool v)
    {
        IsVisible = v;
        if (v) _ = RefreshAsync();
    }

    /// <summary>Закрыть Git-панель (скрыть вкладку).</summary>
    [RelayCommand]
    public void Close() => IsVisible = false;

    /// <summary>Открыть Git-панель.</summary>
    [RelayCommand]
    public void Open() => SetVisible(true);

    /// <summary>Обновить статус Git, список веток и лог коммитов.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        var path = _getPath();
        if (path is null) return;
        _reloading = true;
        IsBusy = true;
        try
        {
            IsRepo = await _git.IsRepositoryAsync(path);
            if (!IsRepo)
            {
                Status = LocalizationService.Current.GetString("Git.NoRepo");
                Files.Clear();
                Log.Clear();
                Branches.Clear();
                StagedCount = UnstagedCount = 0;
                return;
            }
            var st = await _git.GetStatusAsync(path);
            if (st is null)
            {
                IsRepo = false;
                Status = LocalizationService.Current.GetString("Git.GitStatusError");
                Files.Clear();
                Log.Clear();
                Branches.Clear();
                StagedCount = UnstagedCount = 0;
                return;
            }
            Branch = st.Branch;
            Ahead = st.Ahead;
            Behind = st.Behind;
            Files.Clear();
            foreach (var f in st.Files) Files.Add(f);
            StagedCount = st.Files.Count(f => f.IsStaged);
            UnstagedCount = st.Files.Count(f => f.IsUnstaged || f.State == GitState.Untracked);
            Branches.Clear();
            foreach (var b in await _git.BranchesAsync(path)) Branches.Add(b);
            Log.Clear();
            foreach (var l in await _git.LogAsync(path)) Log.Add(l);
            Status = string.Format(LocalizationService.Current.GetString("Git.StatusFormat"),
                Branch, Ahead, Behind, Files.Count);
        }
        finally
        {
            _reloading = false;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Обработчик изменения выбранного файла: показывает diff.
    /// Handles selected file change: shows the diff.
    /// </summary>
    partial void OnSelectedFileChanged(GitFileStatus? value)
    {
        if (value is not null && !_reloading) _ = ShowDiffAsync();
    }

    /// <summary>
    /// Обработчик изменения выбранного коммита: показывает diff коммита.
    /// Handles selected commit change: shows the commit diff.
    /// </summary>
    partial void OnSelectedCommitChanged(GitLogEntry? value)
    {
        if (value is not null && !_reloading) _ = ShowCommitDiffAsync(value.Hash);
    }

    private void SetDiff(string title, string raw)
    {
        DiffTitle = title;
        Diff = raw;
        DiffLines = new ObservableCollection<DiffLine>(DiffParser.Parse(raw));
    }

    /// <summary>Показать diff выбранного файла относительно HEAD.</summary>
    [RelayCommand]
    public async Task ShowDiffAsync()
    {
        var path = _getPath();
        if (path is null || SelectedFile is null) return;
        var raw = await _git.DiffHeadAsync(path, SelectedFile.Path);
        SetDiff(SelectedFile.Path, raw);
    }

    /// <summary>Показать diff указанного коммита.</summary>
    [RelayCommand]
    public async Task ShowCommitDiffAsync(string? hash)
    {
        var path = _getPath();
        if (path is null || string.IsNullOrEmpty(hash)) return;
        var raw = await _git.ShowCommitAsync(path, hash);
        SetCommit(hash!, raw);
    }

    private void SetCommit(string hash, string raw)
    {
        var entry = Log.FirstOrDefault(l => l.Hash == hash);
        SetDiff(entry is null ? hash : $"{entry.ShortHash}  {entry.Subject}", raw);
    }

    /// <summary>Проиндексировать (stage) выбранный файл.</summary>
    [RelayCommand]
    public async Task StageAsync()
    {
        var path = _getPath();
        if (path is null || SelectedFile is null) return;
        await _git.AddAsync(path, new[] { SelectedFile.Path });
        await RefreshAsync();
    }

    /// <summary>Снять индексацию (unstage) выбранного файла.</summary>
    [RelayCommand]
    public async Task UnstageAsync()
    {
        var path = _getPath();
        if (path is null || SelectedFile is null) return;
        await _git.UnstageAsync(path, new[] { SelectedFile.Path });
        await RefreshAsync();
    }

    /// <summary>Проиндексировать все изменения (git add .).</summary>
    [RelayCommand]
    public async Task StageAllAsync()
    {
        var path = _getPath();
        if (path is null) return;
        await _git.AddAsync(path, new[] { "." });
        await RefreshAsync();
    }

    /// <summary>Снять индексацию со всех файлов.</summary>
    [RelayCommand]
    public async Task UnstageAllAsync()
    {
        var path = _getPath();
        if (path is null) return;
        await _git.UnstageAsync(path, Files.Select(f => f.Path));
        await RefreshAsync();
    }

    /// <summary>Отменить изменения в выбранном файле (git checkout/discard).</summary>
    [RelayCommand]
    public async Task DiscardAsync()
    {
        var path = _getPath();
        if (path is null || SelectedFile is null) return;
        if (StyledMessageBoxWindow.Show(string.Format(LocalizationService.Current.GetString("Git.DiscardConfirm"), SelectedFile.Path), "Git",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        await _git.DiscardAsync(path, SelectedFile.Path);
        await RefreshAsync();
    }

    /// <summary>Открыть выбранный файл во встроенном редакторе.</summary>
    [RelayCommand]
    public async Task OpenInEditorAsync()
    {
        var path = _getPath();
        if (path is null || SelectedFile is null || OpenFileAsync is null) return;
        var fullPath = Path.Combine(path, SelectedFile.Path);
        if (File.Exists(fullPath)) await OpenFileAsync(fullPath);
    }

    /// <summary>Открыть diff выбранного файла в просмотрщике.</summary>
    [RelayCommand]
    public async Task OpenDiffInEditorAsync()
    {
        if (SelectedFile is null || string.IsNullOrEmpty(Diff) || OpenDiffAsync is null) return;
        await OpenDiffAsync(SelectedFile.Path + ".diff", Diff);
    }

    /// <summary>Создать коммит (или поправить последний, если Amend=true).</summary>
    [RelayCommand]
    public async Task CommitAsync()
    {
        var path = _getPath();
        if (path is null) return;
        if (string.IsNullOrWhiteSpace(CommitMessage) && !Amend)
        {
            Status = LocalizationService.Current.GetString("Git.EnterCommitMessage");
            return;
        }
        var r = Amend
            ? await _git.CommitAmendAsync(path, CommitMessage)
            : await _git.CommitAsync(path, CommitMessage);
        Status = r.Success
            ? (Amend ? LocalizationService.Current.GetString("Git.CommitAmended") : LocalizationService.Current.GetString("Git.CommitCreated"))
            : string.Format(LocalizationService.Current.GetString("Status.Error"), r.StdErr);
        if (r.Success)
        {
            CommitMessage = "";
            Amend = false;
        }
        await RefreshAsync();
    }

    /// <summary>Отправить коммиты в удалённый репозиторий (git push).</summary>
    [RelayCommand]
    public async Task PushAsync()
    {
        var path = _getPath();
        if (path is null) return;
        var r = await _git.PushAsync(path);
        Status = r.Success ? LocalizationService.Current.GetString("Git.PushDone") : string.Format(LocalizationService.Current.GetString("Git.ErrorPrefix"), r.StdErr);
        await RefreshAsync();
    }

    /// <summary>Забрать изменения из удалённого репозитория (git pull).</summary>
    [RelayCommand]
    public async Task PullAsync()
    {
        var path = _getPath();
        if (path is null) return;
        var r = await _git.PullAsync(path);
        Status = r.Success ? LocalizationService.Current.GetString("Git.PullDone") : string.Format(LocalizationService.Current.GetString("Git.ErrorPrefix"), r.StdErr);
        await RefreshAsync();
    }

    /// <summary>Получить изменения без слияния (git fetch).</summary>
    [RelayCommand]
    public async Task FetchAsync()
    {
        var path = _getPath();
        if (path is null) return;
        var r = await _git.FetchAsync(path);
        Status = r.Success ? LocalizationService.Current.GetString("Git.FetchDone") : string.Format(LocalizationService.Current.GetString("Git.ErrorPrefix"), r.StdErr);
        await RefreshAsync();
    }

    /// <summary>Спрятать незакоммиченные изменения (git stash).</summary>
    [RelayCommand]
    public async Task StashAsync()
    {
        var path = _getPath();
        if (path is null) return;
        var r = await _git.StashAsync(path);
            Status = r.Success
                ? LocalizationService.Current.GetString("Git.StashApplied")
                : r.StdErr.Contains("No local changes", StringComparison.OrdinalIgnoreCase) || r.StdErr.Contains("нет изменений", StringComparison.OrdinalIgnoreCase)
                    ? LocalizationService.Current.GetString("Git.NoChanges")
                    : string.Format(LocalizationService.Current.GetString("Git.ErrorPrefix"), r.StdErr);
        await RefreshAsync();
    }

    /// <summary>Применить последний stash (git stash pop).</summary>
    [RelayCommand]
    public async Task PopStashAsync()
    {
        var path = _getPath();
        if (path is null) return;
        var r = await _git.PopStashAsync(path);
        Status = r.Success ? LocalizationService.Current.GetString("Git.StashApplied") : string.Format(LocalizationService.Current.GetString("Git.ErrorPrefix"), r.StdErr);
        await RefreshAsync();
    }

    /// <summary>Создать новую ветку.</summary>
    [RelayCommand]
    public async Task NewBranchAsync()
    {
        var path = _getPath();
        if (path is null || PromptFunc is null) return;
        var name = PromptFunc(LocalizationService.Current.GetString("Git.NewBranchPrompt"), LocalizationService.Current.GetString("Git.BranchNamePrompt"), "") ?? "";
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var r = await _git.CreateBranchAsync(path, name);
        Status = r.Success ? string.Format(LocalizationService.Current.GetString("Git.BranchCreated"), name) : string.Format(LocalizationService.Current.GetString("Status.Error"), r.StdErr);
        await RefreshAsync();
    }

    /// <summary>Удалить указанную ветку.</summary>
    [RelayCommand]
    public async Task DeleteBranchAsync(string? branch)
    {
        var path = _getPath();
        if (path is null || string.IsNullOrEmpty(branch)) return;
        if (branch == Branch) { Status = LocalizationService.Current.GetString("Git.CannotDeleteCurrentBranch"); return; }
        if (StyledMessageBoxWindow.Show(string.Format(LocalizationService.Current.GetString("Git.ConfirmDeleteBranch"), branch), LocalizationService.Current.GetString("Git.BranchTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var r = await _git.DeleteBranchAsync(path, branch);
        Status = r.Success ? string.Format(LocalizationService.Current.GetString("Git.BranchDeleted"), branch) : string.Format(LocalizationService.Current.GetString("Status.Error"), r.StdErr);
        await RefreshAsync();
    }

    /// <summary>Переключиться на указанную ветку (git checkout).</summary>
    [RelayCommand]
    public async Task CheckoutAsync(string branch)
    {
        var path = _getPath();
        if (path is null || string.IsNullOrEmpty(branch)) return;
        if (_reloading || branch == Branch) return;
        var r = await _git.CheckoutAsync(path, branch);
        Status = r.Success ? string.Format(LocalizationService.Current.GetString("Git.SwitchedTo"), branch) : string.Format(LocalizationService.Current.GetString("Git.ErrorPrefix"), r.StdErr);
        await RefreshAsync();
    }
}
