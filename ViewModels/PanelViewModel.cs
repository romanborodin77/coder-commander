#pragma warning disable CS0618 // Backward-compatible: FileService facade kept until full IFileSystem migration
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly Stack<string> _back = new(); private readonly Stack<string> _fwd = new();
    /// <summary>Можно вернуться назад по истории.</summary>
    public bool CanGoBack => _back.Count > 0;
    /// <summary>Можно перейти вперёд по истории.</summary>
    public bool CanGoForward => _fwd.Count > 0;
    private bool _disposed;

    /// <summary>
    /// Создаёт экземпляр PanelViewModel с опциональным Git-сервисом для отображения статуса файлов.
    /// Creates a PanelViewModel instance with an optional Git service for file status display.
    /// </summary>
    /// <param name="git">Git-сервис для показа статуса файлов (опционально).</param>
    public PanelViewModel(GitService? git = null)
    {
        _git = git;
        // Скрытые файлы: берём значение из настроек, чтобы окно настроек применялось.
        // Hidden files: take the value from settings so the settings dialog is honoured.
        ShowHidden = SettingsService.Load().ShowHidden;
        CurrentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // НЕ вызываем RefreshAsync() здесь — навигация (NavigateToAsync) инициирует загрузку.
        // DO NOT call RefreshAsync() here — navigation (NavigateToAsync) triggers the load.
        // Ранее вызов из конструктора гонялся с NavigateToAsync в RestoreTabs: RefreshAsync
        // захватывала лок для UserProfile, NavigateToAsync меняла CurrentPath, и цикл
        // foreach в RefreshAsync ловил path != CurrentPath и выходил без элементов.
        // Previously the constructor's RefreshAsync raced with NavigateToAsync in RestoreTabs:
        // RefreshAsync acquired the lock for UserProfile, NavigateToAsync changed CurrentPath,
        // and the foreach in RefreshAsync hit path != CurrentPath and exited with no items.
        InitWatcher(); // ph3.4: умный watcher авто-обновления панели
    }

    /// <summary>Список доступных дисков для быстрого перехода.</summary>
    public IEnumerable<DriveItem> Drives => DriveInfo.GetDrives()
        .Select(d => new DriveItem(d.Name.TrimEnd('\\'), d.DriveType));

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

        // Проверяем, это корень диска?
        if (IsRootDrive(_savedFocusPath))
        {
            SelectedItem = null;
            return;
        }

        // Ищем элемент в текущем списке
        var item = Items.FirstOrDefault(i => i.FullPath == _savedFocusPath);
        if (item != null)
        {
            SelectedItem = item;
            return;
        }

        // Элемент удалён — выбираем предыдущий или ".."
        if (Items.Count > 0)
        {
            // Находим индекс, где должен был быть элемент
            var itemsList = Items.ToList();
            var index = itemsList.FindIndex(i => string.Compare(i.FullPath, _savedFocusPath, StringComparison.OrdinalIgnoreCase) > 0);

            if (index > 0)
            {
                // Выбираем предыдущий элемент
                SelectedItem = Items[index - 1];
            }
            else if (Items.Count > 0)
            {
                // Выбираем первый элемент (обычно "..")
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
    private static bool IsRootDrive(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var normalized = path.TrimEnd('\\', '/');
        return normalized.Length <= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':';
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

    /// <summary>Обновить содержимое панели (перечитать директорию). Потокобезопасно: semaphore.</summary>
    /// <summary>Refresh the panel contents (re-read directory). Thread-safe via semaphore.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_disposed) return;
        // Пропускаем, если обновление произошло менее 1 с назад (защита от мерцания).
        // Skip if refresh happened less than 1 s ago (anti-flicker guard).
        var now = DateTime.UtcNow;
        if ((now - _lastRefreshTime).TotalMilliseconds < 1000) return;
        // Защита от повторного входа: Watcher debounce / навигация / фильтр могут гоняться.
        // Reentrancy guard: Watcher debounce / navigation / filter can race.
        // Ждём освобождения lock до 5 секунд вместо немедленного возврата.
        // Wait for lock release up to 5 seconds instead of immediate return.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _refreshLock.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return; // Таймаут ожидания lock / Lock wait timeout
        }
        _isRefreshing = true;
        try
        {
            // Виртуальный режим «результаты поиска» (ph2.2): перечисляем через ISearchResultSource.
            // Virtual "search results" mode (ph2.2): enumerate via ISearchResultSource.
            if (VirtualFileSystem is not null) { await RefreshVirtualAsync(); return; }

            var path = CurrentPath;

            // Сохраняем выделение перед пересозданием коллекции Items.
            // Save selection before recreating the Items collection.
            var selPath = SelectedItem?.FullPath;
            var selPaths = Items.Where(i => i.IsSelected && !i.IsParent)
                                .Select(i => i.FullPath).ToHashSet();

            Items.Clear();
            // Сбрасываем кэш ICollectionView, чтобы GetDefaultView вернул актуальное представление.
            // Reset cached ICollectionView so GetDefaultView returns a fresh view.
            _itemsView = null;

            if (!Directory.Exists(path)) { SelectedItem = null; return; }

            // ═══ Flat View mode: recursive enumeration (ph7.1) ═══
            if (IsFlatView)
            {
                await RefreshFlatViewAsync(path, selPath, selPaths);
                return;
            }

            var gitStates = _git is not null && Directory.Exists(Path.Combine(path, ".git"))
                ? (await _git.GetStatusAsync(path, _cts.Token))?.Files.ToDictionary(f => f.Path, f => f.State)
                : null;

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

                Items.Add(i);
            // Восстанавливаем выделение по сохранённым путям
            if (selPaths.Contains(i.FullPath)) i.IsSelected = true;
        }

        // Восстанавливаем SelectedItem с улучшенной логикой
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
/// Восстанавливает SelectedItem с учётом возможных изменений (удаление элемента и т.д.).
/// Restores SelectedItem considering possible changes (item deletion, etc.).
/// </summary>
/// <param name="selPath">Сохранённый путь элемента. / Saved item path.</param>
private void RestoreFocus(string? selPath)
{
    if (string.IsNullOrEmpty(selPath))
    {
        // Нет сохранённого пути — выбираем первый элемент или null для корня
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

    // Проверяем, это корень диска?
    if (IsRootDrive(selPath))
    {
        SelectedItem = null;
        return;
    }

    // Ищем элемент в текущем списке
    var item = Items.FirstOrDefault(i => i.FullPath == selPath);
    if (item != null)
    {
        SelectedItem = item;
        return;
    }

        // Элемент удалён — выбираем предыдущий или ".."
        if (Items.Count > 0)
        {
            // Находим индекс, где должен был быть элемент
            var itemsList = Items.ToList();
            var index = itemsList.FindIndex(i => string.Compare(i.FullPath, selPath, StringComparison.OrdinalIgnoreCase) >= 0);

            if (index > 0)
            {
                // Выбираем предыдущий элемент
                SelectedItem = Items[index - 1];
            }
            else if (Items.Count > 1)
            {
                // Выбираем второй элемент (первый обычно "..")
                SelectedItem = Items[1];
            }
            else
            {
                // Только ".." — выбираем его
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
        // В виртуальном режиме навигация по папкам заблокирована (ph2.2).
        // Navigation is blocked while in virtual mode (ph2.2).
        if (VirtualFileSystem is not null) return;
        if (string.IsNullOrEmpty(p) || !Directory.Exists(p)) return;
        if (hist && p != CurrentPath) { _back.Push(CurrentPath); _fwd.Clear(); }
        CurrentPath = p;
        // НЕ сбрасываем SelectedItem — RefreshAsync восстановит выделение
        // по сохранённым путям, если элемент остался в текущей папке.
        await RefreshAsync();
    }

    /// <summary>Перейти назад по истории.</summary>
    [RelayCommand] public async Task GoBackAsync() { if (!CanGoBack) return; _fwd.Push(CurrentPath); await NavigateToAsync(_back.Pop(), false); }
    /// <summary>Перейти вперёд по истории.</summary>
    [RelayCommand] public async Task GoForwardAsync() { if (!CanGoForward) return; _back.Push(CurrentPath); await NavigateToAsync(_fwd.Pop(), false); }
    /// <summary>Перейти в родительскую директорию.</summary>
    [RelayCommand] public async Task GoUpAsync() { var p = Directory.GetParent(CurrentPath)?.FullName; if (p != null) await NavigateToAsync(p); }
    /// <summary>Перейти на указанный диск.</summary>
    [RelayCommand] public async Task GoToDriveAsync(string drive) { if (!string.IsNullOrEmpty(drive)) await NavigateToAsync(drive.TrimEnd('\\') + "\\"); }

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

    /// <summary>
    /// Освобождает ресурсы: отменяет фоновые операции, очищает коллекции.
    /// Releases resources: cancels background operations, clears collections.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatcher(); // ph3.4: корректно освобождаем FileSystemWatcher
        PropertyChanged -= OnPanelPropertyChanged;
        _cts.Cancel();
        _cts.Dispose();
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        Items.Clear();
        _itemsView = null;
        _refreshLock.Dispose();
        _back.Clear();
        _fwd.Clear();
        GC.SuppressFinalize(this);
    }
}
