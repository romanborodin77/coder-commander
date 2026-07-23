#pragma warning disable CS0618 // Backward-compatible: FileService facade kept until full IFileSystem migration
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// Модель представления для одной панели файлового менеджера: навигация, выделение, фильтрация, история.
/// ViewModel for a single file manager panel: navigation, selection, filtering, history.
/// </summary>
public partial class PanelViewModel : ObservableObject, IDisposable
{
    private readonly GitService? _git;
    private readonly CancellationTokenSource _cts = new();
    /// <summary>Семафор для защиты RefreshAsync от повторного входа (гонка Watcher/навигация/фильтр).</summary>
    /// <summary>Semaphore guarding RefreshAsync against reentrant calls (Watcher/navigation/filter race).</summary>
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    /// <summary>Текущий путь панели.</summary>
    [ObservableProperty] private string _currentPath = "C:\\";
    /// <summary>Выбранный элемент в списке.</summary>
    [ObservableProperty] private FileSystemItem? _selectedItem;
    /// <summary>Флаг активности панели (подсветка границы).</summary>
    [ObservableProperty] private bool _isActive;
    /// <summary>Показывать скрытые файлы.</summary>
    [ObservableProperty] private bool _showHidden;
    /// <summary>Визуальная индикация при перетаскивании файлов на эту панель (ph6.3).</summary>
    [ObservableProperty] private bool _isDragHighlight;
    /// <summary>Фильтр имён файлов (показывать только содержащие подстроку).</summary>
    [ObservableProperty] private string _filter = "";
    /// <summary>Режим плоского списка: показать все файлы из подпапок (ph7.1).</summary>
    [ObservableProperty] private bool _isFlatView;
    /// <summary>Коллекция элементов текущей директории.</summary>
    public ObservableCollection<FileSystemItem> Items { get; } = [];
    /// <summary>История навигации назад с запоминанием позиции курсора (path, focusFile).</summary>
    /// <summary>Back navigation history with cursor position (path, focusFile).</summary>
    private readonly Stack<(string path, string? focusFile)> _back = new();
    /// <summary>История навигации вперёд.</summary>
    private readonly Stack<(string path, string? focusFile)> _fwd = new();
    /// <summary>Можно вернуться назад по истории.</summary>
    public bool CanGoBack => _back.Count > 0;
    /// <summary>Можно перейти вперёд по истории.</summary>
    public bool CanGoForward => _fwd.Count > 0;
    private bool _disposed;

    // ── Status bar properties ──
    /// <summary>Количество выделенных элементов (меток).</summary>
    public int SelectedCount => Items.Count(i => i.IsSelected && !i.IsParent);
    /// <summary>Размер выделенных файлов в человекочитаемом формате.</summary>
    public string SelectedSizeDisplay => FormatSize(Items.Where(i => i.IsSelected && !i.IsParent).Sum(i => i.Size));
    /// <summary>Свободное место на текущем диске.</summary>
    public string FreeSpaceDisplay => GetFreeSpace();
    /// <summary>Информация о файле под курсором для status bar.</summary>
    public string CursorInfo => SelectedItem is null || SelectedItem.IsParent
        ? $"{Items.Count(i => !i.IsParent)} элементов"
        : SelectedItem.IsDirectory
            ? $"[{SelectedItem.Name}] <DIR>"
            : $"{SelectedItem.Name}  {SelectedItem.SizeDisplay}  {SelectedItem.ModifiedDisplay}";

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{s:0.##} {u[i]}";
    }

    private string GetFreeSpace()
    {
        try
        {
            if (CurrentPath.Length >= 2 && CurrentPath[1] == ':')
            {
                var drive = new DriveInfo(CurrentPath[..2]);
                return FormatSize(drive.AvailableFreeSpace) + " / " + FormatSize(drive.TotalSize);
            }
        }
        catch { }
        return "";
    }

    /// <summary>Уведомляет UI об изменении свойств status bar.</summary>
    public void RefreshStatusBar()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSizeDisplay));
        OnPropertyChanged(nameof(FreeSpaceDisplay));
        OnPropertyChanged(nameof(CursorInfo));
    }

    /// <summary>Список облачных профилей для отображения в панели дисков.</summary>
    private List<CloudDriveItem> _cloudDrives = [];

    /// <summary>
    /// Создаёт экземпляр PanelViewModel с опциональным Git-сервисом для отображения статуса файлов.
    /// Creates a PanelViewModel instance with an optional Git service for file status display.
    /// </summary>
    /// <param name="git">Git-сервис для показа статуса файлов (опционально).</param>
    public PanelViewModel(GitService? git = null)
    {
        _git = git;
        ShowHidden = SettingsService.Load().ShowHidden;
        CurrentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        InitWatcher();
    }

    /// <summary>Список доступных дисков для быстрого перехода (локальные + облачные).</summary>
    public IEnumerable<object> Drives
    {
        get
        {
            foreach (var d in DriveInfo.GetDrives())
                yield return new DriveItem(d.Name.TrimEnd('\\'), d.DriveType);
            foreach (var cd in _cloudDrives)
                yield return cd;
        }
    }

    /// <summary>Обновить список облачных дисков и уведомить UI.</summary>
    public void SetCloudDrives(List<CloudDriveItem> drives)
    {
        _cloudDrives = drives;
        OnPropertyChanged(nameof(Drives));
    }

    /// <summary>
    /// Сохраняет текущий путь фокуса для последующего восстановления.
    /// Saves the current focus path for later restoration.
    /// </summary>
    private string? _savedFocusPath;

    /// <summary>
    /// Сохраняет путь текущего выделенного элемента для восстановления после закрытия окна.
    /// Saves the path of the currently selected item for restoration after window close.
    /// </summary>
    public void SaveFocus()
    {
        _savedFocusPath = SelectedItem?.FullPath;
    }

    /// <summary>
    /// Восстанавливает фокус на ранее сохранённом элементе.
    /// Если элемент удалён — выбирает предыдущий или "..".
    /// Если корень диска — фокус не требуется.
    /// Restores focus to the previously saved item.
    /// If the item is deleted — selects the previous one or "..".
    /// If root drive — focus is not required.
    /// </summary>
    public void RestoreFocus()
    {
        if (string.IsNullOrEmpty(_savedFocusPath)) return;

        if (IsRootDrive(_savedFocusPath))
        {
            SelectedItem = null;
            return;
        }

        var item = Items.FirstOrDefault(i => i.FullPath == _savedFocusPath);
        if (item != null)
        {
            SelectedItem = item;
            return;
        }

        if (Items.Count > 0)
        {
            var itemsList = Items.ToList();
            var index = itemsList.FindIndex(i => string.Compare(i.FullPath, _savedFocusPath, StringComparison.OrdinalIgnoreCase) > 0);

            if (index > 0)
            {
                SelectedItem = Items[index - 1];
            }
            else if (Items.Count > 0)
            {
                SelectedItem = Items[0];
            }
        }
        else
        {
            SelectedItem = null;
        }
    }

    /// <summary>
    /// Проверяет, является ли путь корнем диска (C:\, D:\ и т.д.).
    /// Checks if the path is a drive root (C:\, D:\, etc.).
    /// </summary>
    // FIXED: Was `Length <= 2` which allowed length 1 → IndexOutOfRangeException on `normalized[1]`
    private static bool IsRootDrive(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var normalized = path.TrimEnd('\\', '/');
        return normalized.Length == 2 && char.IsLetter(normalized[0]) && normalized[1] == ':';
    }

    /// <summary>Перейти к сегменту пути (части адресной строки).</summary>
    [RelayCommand]
    public async Task NavigateToSegmentAsync(string seg)
    {
        if (string.IsNullOrEmpty(seg)) return;
        var parts = CurrentPath.Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();
        var idx = parts.FindIndex(p => string.Equals(p, seg, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return;
        var np = string.Join("\\", parts.Take(idx + 1));
        if (np.Length <= 2) np += "\\";
        await NavigateToAsync(np);
    }

    /// <summary>
    /// Возвращает родительский путь для локальных и облачных путей.
    /// Returns parent path for both local and cloud paths.
    /// </summary>
    public string GetParentPath()
    {
        if (CurrentPath.StartsWith("cloud://", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = NormalizeCloudPath(CurrentPath);
            var idx = normalized.LastIndexOf('/');
            return idx > 8 ? normalized[..idx] : normalized;
        }
        return Directory.GetParent(CurrentPath)?.FullName ?? CurrentPath;
    }

    /// <summary>Обновить содержимое панели (перечитать директорию). Потокобезопасно: semaphore.</summary>
    /// <summary>Refresh the panel contents (re-read directory). Thread-safe via semaphore.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_disposed) return;
        var now = DateTime.UtcNow;
        if ((now - _lastRefreshTime).TotalMilliseconds < 1000) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _refreshLock.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        _isRefreshing = true;
        try
        {
            if (VirtualFileSystem is not null) { await RefreshVirtualAsync(); return; }

            var path = CurrentPath;

            var selPath = SelectedItem?.FullPath;
            HashSet<string>? selPaths = null;
            if (Items.Count > 0)
            {
                selPaths = new HashSet<string>(
                    Items.Where(i => i.IsSelected && !i.IsParent).Select(i => i.FullPath),
                    StringComparer.OrdinalIgnoreCase);
            }

            if (!Directory.Exists(path)) { Items.Clear(); _itemsView = null; SelectedItem = null; return; }

            if (IsFlatView)
            {
                await RefreshFlatViewAsync(path, selPath, selPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                return;
            }

            var gitStates = _git is not null && Directory.Exists(Path.Combine(path, ".git"))
                ? (await _git.GetStatusAsync(path, _cts.Token))?.Files.ToDictionary(f => f.Path, f => f.State)
                : null;

            var newItems = new List<FileSystemItem>();
            foreach (var i in await FileService.EnumerateDirectoryAsync(path, ShowHidden, _cts.Token))
            {
                if (path != CurrentPath) return;
                if (!string.IsNullOrEmpty(Filter) && !i.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)) continue;
                if (gitStates is not null && !i.IsParent)
                {
                    var rel = i.FullPath.Length > path.Length ? i.FullPath[(path.Length + 1)..] : i.Name;
                    rel = rel.Replace('\\', '/');
                    if (gitStates.TryGetValue(rel, out var st)) i.GitState = st;
                }
                if (selPaths is not null && selPaths.Contains(i.FullPath)) i.IsSelected = true;
                newItems.Add(i);
            }

            ReplaceItems(newItems);

            RestoreFocus(selPath);

            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            _lastRefreshTime = DateTime.UtcNow;
        }
        finally
        {
            _isRefreshing = false;
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Заменяет содержимое Items одним вызовом (Reset-событие вместо N Add).
    /// Replaces Items content in one call (single Reset event instead of N Add).
    /// </summary>
    private void ReplaceItems(List<FileSystemItem> newItems)
    {
        // Отписываемся от старых элементов
        foreach (var old in Items)
            old.PropertyChanged -= Item_PropertyChanged;

        Items.Clear();
        _itemsView = null;
        foreach (var item in newItems)
        {
            item.PropertyChanged += Item_PropertyChanged;
            Items.Add(item);
        }
        ApplySortFromConfig();
        RefreshStatusBar();
    }

    /// <summary>Обновляет status bar при изменении IsSelected или GitState элемента.</summary>
    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileSystemItem.IsSelected))
            RefreshStatusBar();
    }

    /// <summary>
    /// Применяет сохранённую сортировку из ColumnConfigService к ItemsView.
    /// Applies persisted sort from ColumnConfigService to ItemsView.
    /// </summary>
    public void ApplySortFromConfig()
    {
        var view = ItemsView;
        if (view is null) return;
        view.SortDescriptions.Clear();

        // Находим колонку, по которой задана сортировка.
        // Все колонки по умолчанию имеют SortDirection=Ascending, но это не значит что они сортируют.
        // Сортирует только та колонка, которая была выбрана кликом — её SortDirection сохранён.
        // Для определения используем: берём первую видимую колонку, если она Name — проверяем,
        // не задана ли сортировка по другой колонке (у которой SortDirection != Ascending после сброса).
        // На самом деле: все колонки сброшены в Ascending, только активная может быть Ascending или Descending.
        // Проблема: как отличить Name Ascending (по умолчанию) от Name Ascending (выбрана пользователем)?
        // Решение: храним колонку сортировки отдельно в ColumnConfigService (SortedColumnKey).

        var sortedKey = ColumnConfigService.SortedColumnKey;
        ColumnDefinition? sortCol = null;
        if (!string.IsNullOrEmpty(sortedKey))
        {
            sortCol = ColumnConfigService.ActiveColumns.FirstOrDefault(c => c.Key == sortedKey);
        }

        // Папки сверху (всегда)
        view.SortDescriptions.Add(
            new SortDescription(nameof(FileSystemItem.IsDirectory), ListSortDirection.Descending));

        if (sortCol is not null)
        {
            view.SortDescriptions.Add(
                new SortDescription(sortCol.GetSortPropertyName(), sortCol.SortDirection));
        }

        view.Refresh();
    }

    /// <summary>
    /// Устанавливает сортировку по указанной колонке (циклически: Ascending → Descending).
    /// Sets sort by the specified column (cyclic: Ascending → Descending).
    /// </summary>
    public void SetSortByColumn(string columnKey)
    {
        var view = ItemsView;
        if (view is null) return;

        var col = ColumnConfigService.ActiveColumns.FirstOrDefault(c => c.Key == columnKey);
        if (col is null) return;

        // Циклическое переключение: Asc -> Desc -> Asc
        var previousDir = col.SortDirection;
        col.SortDirection = previousDir == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        // Запоминаем, какая колонка сортирует
        ColumnConfigService.SortedColumnKey = columnKey;

        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(
            new SortDescription(nameof(FileSystemItem.IsDirectory), ListSortDirection.Descending));
        view.SortDescriptions.Add(
            new SortDescription(col.GetSortPropertyName(), col.SortDirection));
        view.Refresh();

        ColumnConfigService.Save();
    }

    /// <summary>
    /// Восстанавливает SelectedItem с учётом возможных изменений (удаление элемента и т.д.).
    /// Restores SelectedItem considering possible changes (item deletion, etc.).
    /// </summary>
    /// <param name="selPath">Сохранённый путь элемента. / Saved item path.</param>
    private void RestoreFocus(string? selPath)
    {
        if (string.IsNullOrEmpty(selPath))
        {
            if (IsRootDrive(CurrentPath))
            {
                SelectedItem = null;
            }
            else if (Items.Count > 0)
            {
                SelectedItem = Items[0];
            }
            else
            {
                SelectedItem = null;
            }
            return;
        }

        if (IsRootDrive(selPath))
        {
            SelectedItem = null;
            return;
        }

        var item = Items.FirstOrDefault(i => i.FullPath == selPath);
        if (item != null)
        {
            SelectedItem = item;
            return;
        }

        if (Items.Count > 0)
        {
            var itemsList = Items.ToList();
            var index = itemsList.FindIndex(i => string.Compare(i.FullPath, selPath, StringComparison.OrdinalIgnoreCase) >= 0);

            if (index > 0)
            {
                SelectedItem = Items[index - 1];
            }
            else if (Items.Count > 1)
            {
                SelectedItem = Items[1];
            }
            else
            {
                SelectedItem = Items[0];
            }
        }
        else
        {
            SelectedItem = null;
        }
    }

    /// <summary>
    /// Рекурсивное перечисление всех файлов в подпапках (Flat View). Пропускает каталоги, показывает только файлы.
    /// Recursive enumeration of all files in subdirectories (Flat View). Skips directories, shows files only.
    /// </summary>
    private async Task RefreshFlatViewAsync(string rootPath, string? selPath, HashSet<string> selPaths)
    {
        const int maxDepth = 10;
        var ct = _cts.Token;

        Items.Add(new FileSystemItem(rootPath + @"\..", isDirectory: true, isParent: true));

        async Task ScanDirectoryAsync(string dir, int depth)
        {
            if (depth > maxDepth || ct.IsCancellationRequested) return;
            try
            {
                var entries = await FileService.EnumerateDirectoryAsync(dir, ShowHidden, ct);
                foreach (var entry in entries)
                {
                    if (ct.IsCancellationRequested) return;
                    if (rootPath != CurrentPath) return;
                    if (entry.IsDirectory || entry.IsParent) continue;

                    var relativePath = Path.GetRelativePath(rootPath, entry.FullPath);
                    if (!string.IsNullOrEmpty(Filter) && !relativePath.Contains(Filter, StringComparison.OrdinalIgnoreCase)) continue;

                    var item = new FileSystemItem(
                        entry.FullPath,
                        isDirectory: false,
                        size: entry.Size,
                        modified: entry.Modified,
                        displayName: relativePath);

                    Items.Add(item);
                    if (selPaths.Contains(entry.FullPath)) item.IsSelected = true;
                }

                foreach (var entry in entries)
                {
                    if (ct.IsCancellationRequested) return;
                    if (entry.IsDirectory && !entry.IsParent)
                    {
                        await ScanDirectoryAsync(entry.FullPath, depth + 1);
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        await ScanDirectoryAsync(rootPath, 0);

        if (rootPath == CurrentPath)
        {
            if (selPath != null) SelectedItem = Items.FirstOrDefault(i => i.FullPath == selPath);
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        }
    }

    /// <summary>
    /// Перейти к указанному пути с опциональным добавлением в историю навигации.
    /// Navigate to the specified path with optional history tracking.
    /// </summary>
    /// <param name="p">Целевой путь.</param>
    /// <param name="hist">Добавлять текущий путь в историю назад (true по умолчанию).</param>
    public async Task NavigateToAsync(string p, bool hist = true)
    {
        if (string.IsNullOrEmpty(p)) return;

        // Облачные пути (начинаются с cloud://) — переключаемся на виртуальный режим.
        if (p.StartsWith("cloud://", StringComparison.OrdinalIgnoreCase))
        {
            // Нормализуем путь: разрешаем ".." и "//" в cloud:// путях.
            p = NormalizeCloudPath(p);

            var profileId = ExtractCloudProfileId(p);
            var cloudPath = ExtractCloudPath(p);
            var drive = _cloudDrives.FirstOrDefault(d => d.ProfileId == profileId);

            // Если FileSystem не установлен — пытаемся подключиться автоматически.
            if (drive?.FileSystem is null)
            {
                try
                {
                    var profiles = new CloudStorageService().GetProfiles();
                    var profile = profiles.FirstOrDefault(pr => pr.Id == profileId);
                    if (profile is not null)
                    {
                        var fs = CloudStorageService.CreateFileSystem(profile);
                        await fs.ConnectAsync(_cts.Token);
                        if (drive is not null) drive.FileSystem = fs;
                    }
                }
                catch (Exception)
                {
                    return;
                }
            }

            if (drive?.FileSystem is not null)
            {
                if (hist && p != CurrentPath) { _back.Push((CurrentPath, SelectedItem?.FullPath)); _fwd.Clear(); }
                VirtualReturnPath = CurrentPath;
                VirtualFileSystem = drive.FileSystem;
                CurrentPath = p;
                await RefreshAsync();
            }
            return;
        }

        // Переход с виртуального режима (cloud://) на локальный путь —
        // выходим из виртуального режима и продолжаем навигацию.
        // Switching from virtual mode (cloud://) to a local path —
        // exit virtual mode and continue navigation.
        if (VirtualFileSystem is not null)
        {
            VirtualFileSystem = null;
            VirtualReturnPath = "";
        }

        if (!Directory.Exists(p)) return;
        if (hist && p != CurrentPath) { _back.Push((CurrentPath, SelectedItem?.FullPath)); _fwd.Clear(); }
        CurrentPath = p;
        await RefreshAsync();
    }

    /// <summary>Перейти назад по истории (с восстановлением позиции курсора).</summary>
    [RelayCommand]
    public async Task GoBackAsync()
    {
        if (!CanGoBack) return;
        var (path, focus) = _back.Pop();
        _fwd.Push((CurrentPath, SelectedItem?.FullPath));
        await NavigateToAsync(path, false);
        RestoreFocusByPath(focus);
    }

    /// <summary>Перейти вперёд по истории (с восстановлением позиции курсора).</summary>
    [RelayCommand]
    public async Task GoForwardAsync()
    {
        if (!CanGoForward) return;
        var (path, focus) = _fwd.Pop();
        _back.Push((CurrentPath, SelectedItem?.FullPath));
        await NavigateToAsync(path, false);
        RestoreFocusByPath(focus);
    }

    /// <summary>Восстанавливает курсор на элемент по пути.</summary>
    private void RestoreFocusByPath(string? focusPath)
    {
        if (string.IsNullOrEmpty(focusPath)) return;
        var item = Items.FirstOrDefault(i => i.FullPath == focusPath);
        if (item is not null) SelectedItem = item;
    }
    /// <summary>Перейти в родительскую директорию.</summary>
    [RelayCommand]
    public async Task GoUpAsync()
    {
        if (VirtualFileSystem is not null)
        {
            // В облачном режиме — поднимаемся по cloud:// пути.
            if (CurrentPath.StartsWith("cloud://", StringComparison.OrdinalIgnoreCase))
            {
                var normalized = NormalizeCloudPath(CurrentPath);
                var idx = normalized.LastIndexOf('/');
                if (idx > 8) // после "cloud://id"
                {
                    var parent = normalized[..idx];
                    await NavigateToAsync(parent, false);
                }
                else
                {
                    // Корень облака — выходим из виртуального режима.
                    ExitVirtualMode();
                }
            }
            return;
        }
        var p = Directory.GetParent(CurrentPath)?.FullName;
        if (p != null) await NavigateToAsync(p);
    }

    /// <summary>Перейти на указанный диск (локальный или облачный).</summary>
    [RelayCommand]
    public async Task GoToDriveAsync(string drive)
    {
        if (string.IsNullOrEmpty(drive)) return;

        // Облачный диск — начинается с "cloud:"
        if (drive.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase))
        {
            var profileId = drive["cloud:".Length..];
            await NavigateToAsync($"cloud://{profileId}/");
            return;
        }

        await NavigateToAsync(drive.TrimEnd('\\') + "\\");
    }

    /// <summary>Перейти на облачный диск (принимает CloudDriveItem).</summary>
    [RelayCommand]
    public async Task GoToCloudDriveAsync(CloudDriveItem drive)
    {
        if (drive is null) return;
        await NavigateToAsync($"cloud://{drive.ProfileId}/");
    }

    /// <summary>Переключить режим плоского списка (Flat View).</summary>
    [RelayCommand]
    private void ToggleFlatView()
    {
        IsFlatView = !IsFlatView;
        _ = RefreshAsync();
    }

    private CancellationTokenSource? _filterCts;
    /// <summary>
    /// Обработчик изменения фильтра: запускает отложенное обновление с задержкой 250 мс.
    /// Handles filter changes: triggers a debounced refresh with a 250 ms delay.
    /// </summary>
    partial void OnFilterChanged(string value)
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        var cts = _filterCts;
        _ = FilterDebouncedAsync(cts.Token);
    }

    private async Task FilterDebouncedAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(250, ct);
            if (!ct.IsCancellationRequested) await RefreshAsync();
        }
        catch (TaskCanceledException) { }
    }

    /// <summary>
    /// Возвращает выбранные элементы или текущий элемент, если ничего не выделено. Исключает элемент "..".
    /// Returns selected items or the current item if nothing is selected. Excludes the ".." entry.
    /// </summary>
    public IEnumerable<FileSystemItem> GetSelectionOrCurrent()
    {
        var s = Items.Where(i => i.IsSelected && !i.IsParent).ToList();
        return s.Count == 0 ? (SelectedItem is { IsParent: false } ? [SelectedItem] : []) : s;
    }

    // ═══════════════════════════════════════════════
    // МЕТОДЫ ВЫДЕЛЕНИЯ (по референсу Double Commander)
    // SELECTION METHODS (per Double Commander reference)
    // ═══════════════════════════════════════════════

    /// <summary>Выделить все файлы с расширением текущего (DC cm_MarkCurrentExtension).</summary>
    public void MarkCurrentExtension(bool select)
    {
        var ext = SelectedItem?.Extension;
        if (string.IsNullOrEmpty(ext)) return;
        foreach (var fi in Items.Where(i => !i.IsParent && i.Extension == ext))
            fi.IsSelected = select;
        RefreshStatusBar();
    }

    /// <summary>Выделить все файлы с именем текущего (DC cm_MarkCurrentName).</summary>
    public void MarkCurrentName(bool select)
    {
        var name = Path.GetFileNameWithoutExtension(SelectedItem?.Name ?? "");
        if (string.IsNullOrEmpty(name)) return;
        foreach (var fi in Items.Where(i => !i.IsParent && Path.GetFileNameWithoutExtension(i.Name) == name))
            fi.IsSelected = select;
        RefreshStatusBar();
    }

    /// <summary>Сохранённое выделение для RestoreSelection (DC cm_SaveSelection).</summary>
    private HashSet<string>? _savedSelection;

    /// <summary>Сохранить текущее выделение (DC cm_SaveSelection).</summary>
    public void SaveSelection()
    {
        _savedSelection = Items.Where(i => i.IsSelected && !i.IsParent)
            .Select(i => i.FullPath).ToHashSet();
    }

    /// <summary>Восстановить сохранённое выделение (DC cm_RestoreSelection).</summary>
    public void RestoreSelection()
    {
        if (_savedSelection is null) return;
        foreach (var fi in Items.Where(i => !i.IsParent))
            fi.IsSelected = _savedSelection.Contains(fi.FullPath);
        RefreshStatusBar();
    }

    // ═══════════════════════════════════════════════
    // НАВИГАЦИЯ (по референсу Double Commander)
    // NAVIGATION (per Double Commander reference)
    // ═══════════════════════════════════════════════

    /// <summary>Перейти к корню диска (DC cm_ChangeDirToRoot, Ctrl+\).</summary>
    [RelayCommand]
    public async Task GoToRootAsync()
    {
        if (VirtualFileSystem is not null) return;
        var root = Path.GetPathRoot(CurrentPath);
        if (root is not null) await NavigateToAsync(root);
    }

    /// <summary>Перейти к домашней папке (DC cm_ChangeDirToHome).</summary>
    [RelayCommand]
    public async Task GoToHomeAsync()
    {
        if (VirtualFileSystem is not null) return;
        await NavigateToAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    /// <summary>
    /// Извлекает profileId из cloud:// пути.
    /// Extracts profileId from a cloud:// path.
    /// </summary>
    private static string ExtractCloudProfileId(string cloudPath)
    {
        // cloud://profileId/rest/of/path
        var withoutScheme = "cloud://".Length;
        var slash = cloudPath.IndexOf('/', withoutScheme);
        return slash > withoutScheme ? cloudPath[withoutScheme..slash] : cloudPath[withoutScheme..];
    }

    /// <summary>
    /// Извлекает путь внутри облака из cloud:// пути.
    /// Extracts the cloud-internal path from a cloud:// path.
    /// </summary>
    private static string ExtractCloudPath(string cloudPath)
    {
        var withoutScheme = "cloud://".Length;
        var slash = cloudPath.IndexOf('/', withoutScheme);
        return slash > withoutScheme ? "/" + cloudPath[(slash + 1)..] : "/";
    }

    /// <summary>
    /// Нормализует cloud:// путь: разрешает "..", убирает "//", ".".
    /// Normalizes cloud:// path: resolves "..", removes "//", ".".
    /// </summary>
    private static string NormalizeCloudPath(string cloudPath)
    {
        var withoutScheme = "cloud://".Length;
        var slash = cloudPath.IndexOf('/', withoutScheme);
        if (slash <= withoutScheme) return cloudPath;

        var profileId = cloudPath[withoutScheme..slash];
        var internalPath = cloudPath[(slash + 1)..];

        // Разрешаем сегменты ".." и "." в internalPath.
        var segments = internalPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var resolved = new List<string>();
        foreach (var seg in segments)
        {
            if (seg == "..")
            {
                if (resolved.Count > 0) resolved.RemoveAt(resolved.Count - 1);
            }
            else if (seg != ".")
            {
                resolved.Add(seg);
            }
        }

        var normalized = resolved.Count > 0 ? "/" + string.Join("/", resolved) : "/";
        return $"cloud://{profileId}{normalized}";
    }

    /// <summary>
    /// Освобождает ресурсы: отменяет фоновые операции, очищает коллекции.
    /// Releases resources: cancels background operations, clears collections.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatcher();
        PropertyChanged -= OnPanelPropertyChanged;
        _cts.Cancel();
        _cts.Dispose();
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        // FIXED: Cancel and dispose _qfCts and _quickViewCts to prevent kernel timer leaks.
        _qfCts?.Cancel();
        _qfCts?.Dispose();
        _quickViewCts?.Cancel();
        _quickViewCts?.Dispose();
        Items.Clear();
        _itemsView = null;
        _refreshLock.Dispose();
        _back.Clear();
        _fwd.Clear();
        GC.SuppressFinalize(this);
    }
}
