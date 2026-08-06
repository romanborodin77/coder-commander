using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel for a single file manager panel: navigation, selection, history.
//// </summary>
public sealed partial class PanelViewModel : ObservableObject, IDisposable
{
    private IFileSystem _fs;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private CancellationTokenSource? _navCts;
    private readonly object _navLock = new();

    private readonly Stack<(IFileSystem fs, string path)> _back = new();
    private readonly Stack<(IFileSystem fs, string path)> _fwd = new();

    private FileSystemWatcher? _watcher;
    private System.Windows.Forms.Timer? _refreshDebounce;
    private const int DebounceMs = 300;

    private System.Windows.Forms.Timer? _settingsSaveDebounce;
    private const int SettingsSaveDebounceMs = 400;

    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private FileSystemItem? _selectedItem;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _isFlatView;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _sortColumn = "Name";
    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private bool _directoriesFirst = true;

    /// <summary>Raised when sort settings change so the UI can refresh.</summary>
    public event EventHandler? SortChanged;

    /// <summary>Items visible in the panel (after filtering).</summary>
    public ObservableCollection<FileSystemItem> Items { get; } = [];

    /// <summary>All loaded items (before filtering).</summary>
    private List<FileSystemItem> _allItems = [];

    /// <summary>File system provider used by this panel (may change when entering an archive).</summary>
    public IFileSystem CurrentFileSystem
    {
        get => _fs;
        set => _fs = value;
    }

    /// <summary><c>true</c> when the current path points inside a ZIP archive.</summary>
    public bool IsInsideArchive => _fs is FileSystem.ZipArchiveFileSystem;

    /// <summary><c>true</c> when there is at least one entry on the back-navigation stack.</summary>
    public bool CanGoBack => _back.Count > 0;
    /// <summary><c>true</c> when there is at least one entry on the forward-navigation stack.</summary>
    public bool CanGoForward => _fwd.Count > 0;

    // Cached rather than computed on every access: these three are read repeatedly per user
    // action (status bar, cursor info, toolbar state), and each used to be its own O(n) LINQ pass
    // over Items. Recomputed together in RecomputeSelectionStats(), called from ApplyFilter() (the
    // only place Items itself changes) and NotifySelectionChanged() (the single choke point every
    // selection mutation - bulk or ad-hoc - already funnels through).
    private int _selectedCount;
    private long _selectedBytes;
    private int _totalCount;

    /// <summary>Number of selected items in the panel (excluding the parent "…" entry).</summary>
    public int SelectedCount => _selectedCount;
    /// <summary>Total size in bytes of all selected non-directory items.</summary>
    public long SelectedBytes => _selectedBytes;
    /// <summary>Number of visible items excluding the parent entry.</summary>
    public int TotalCount => _totalCount;

    /// <summary>Formatted string showing free and total disk space for the current drive.</summary>
    public string FreeSpaceDisplay { get; private set; } = "";
    /// <summary>Text describing the item under the cursor or a generic item count.</summary>
    public string CursorInfo { get; private set; } = "";

    /// <summary>Raised after the visible items list changes (after filtering or refresh).</summary>
    public event EventHandler? ItemsChanged;
    /// <summary>Raised when the current directory path changes.</summary>
    public event EventHandler? PathChanged;

    /// <summary>Initialises the panel with a file system provider and loads persisted sort/view settings.</summary>
    /// <param name="fs">File system provider for this panel.</param>
    public PanelViewModel(IFileSystem fs)
    {
        _fs = fs;
        var s = SettingsService.Load();
        ShowHidden = s.ShowHidden;
        IsFlatView = s.FlatView;
        _sortColumn = s.SortColumn;
        _sortDescending = s.SortDescending;
        _directoriesFirst = s.DirectoriesFirst;
        CurrentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    partial void OnCurrentPathChanged(string value)
    {
        PathChanged?.Invoke(this, EventArgs.Empty);
        StartWatcher(value);
    }

    partial void OnSelectedItemChanged(FileSystemItem? value)
    {
        UpdateCursorInfo();
    }

    partial void OnFilterChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnShowHiddenChanged(bool value)
    {
        _ = RefreshAsync();
    }

    partial void OnIsFlatViewChanged(bool value)
    {
        _ = RefreshAsync();
    }

    partial void OnSortColumnChanged(string value)
    {
        SaveSortSettings();
        ResortAndReapply();
        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSortDescendingChanged(bool value)
    {
        SaveSortSettings();
        ResortAndReapply();
        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnDirectoriesFirstChanged(bool value)
    {
        SaveSortSettings();
        ResortAndReapply();
        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-sorts the already-loaded item list and reapplies the current filter, without
    /// touching the file system - sort/directories-first changes used to call RefreshAsync(),
    /// which re-enumerated the whole directory (or archive, or network share) just to reorder
    /// items already in memory.</summary>
    private void ResortAndReapply()
    {
        SortAllItems();
        ApplyFilter();
    }

    /// <summary>
    /// Debounces the actual settings.json write: clicking a column header used to synchronously
    /// run File.WriteAllText+File.Move (under SettingsService's lock) directly in the property-
    /// changed handler, on the UI thread, on every single click. This coalesces rapid clicks into
    /// one write and moves the write itself off the UI thread.
    /// </summary>
    private void SaveSortSettings()
    {
        _settingsSaveDebounce ??= CreateSettingsSaveDebounceTimer();
        _settingsSaveDebounce.Stop();
        _settingsSaveDebounce.Start();
    }

    private System.Windows.Forms.Timer CreateSettingsSaveDebounceTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = SettingsSaveDebounceMs };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SaveSortSettingsNow();
        };
        return timer;
    }

    private void SaveSortSettingsNow()
    {
        var column = SortColumn;
        var descending = SortDescending;
        var dirsFirst = DirectoriesFirst;
        _ = Task.Run(() =>
        {
            var s = SettingsService.Load();
            s.SortColumn = column;
            s.SortDescending = descending;
            s.DirectoriesFirst = dirsFirst;
            SettingsService.Save(s);
        });
    }

    /// <summary>
    /// Navigates to a new path, pushing the current path onto the back stack.
    /// </summary>
    public async Task NavigateAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        bool isArchivePath = FileSystem.ZipArchiveFileSystem.IsArchivePath(path);
        if (!isArchivePath)
            path = path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        // Deliberately no ConfigureAwait(false): the rest of this method (and RefreshAsync below)
        // sets CurrentPath and mutates the ObservableCollection Items, both of which need to run
        // back on the UI thread that called NavigateAsync - StartWatcher (triggered by the
        // CurrentPath setter) creates a System.Windows.Forms.Timer, which only fires its Tick
        // event on the thread that created it, and ObservableCollection isn't thread-safe.
        if (!await _fs.ExistsAsync(path))
        {
            LogService.Warning($"Path does not exist: {path}");
            return;
        }

        lock (_navLock)
        {
            _navCts?.Cancel();
            _navCts?.Dispose();
            _navCts = new CancellationTokenSource();
        }

        if (!string.Equals(CurrentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(CurrentPath))
                _back.Push((_fs, CurrentPath));
            _fwd.Clear();
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        }

        CurrentPath = path;
        LogService.LogNavigation(path);
        await RefreshAsync(_navCts.Token);
    }

    /// <summary>
    /// Navigates to parent directory.
    /// </summary>
    public async Task GoToParentAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        if (FileSystem.ZipArchiveFileSystem.IsArchivePath(CurrentPath))
        {
            var (archivePath, innerPath) = FileSystem.ZipArchiveFileSystem.SplitPath(CurrentPath);
            innerPath = innerPath.Replace('\\', '/').Trim('/');

            if (string.IsNullOrEmpty(innerPath))
            {
                // Exit archive — switch back to local filesystem, navigate to archive's parent directory
                CurrentFileSystem = new LocalFileSystem();
                var parentDir = Path.GetDirectoryName(archivePath);
                if (!string.IsNullOrEmpty(parentDir))
                    await NavigateAsync(parentDir);
                return;
            }

            var lastSlash = innerPath.LastIndexOf('/');
            var parentInner = lastSlash > 0 ? innerPath[..lastSlash] : "";
            var parentPath = FileSystem.ZipArchiveFileSystem.MakePath(archivePath, parentInner);
            await NavigateAsync(parentPath);
        }
        else
        {
            var parent = Path.GetFullPath(Path.Combine(CurrentPath, ".."));
            if (!string.Equals(parent, CurrentPath, StringComparison.OrdinalIgnoreCase))
                await NavigateAsync(parent);
        }
    }

    /// <summary>Navigates back to the previous directory in the history stack.</summary>
    public async Task GoBackAsync()
    {
        if (_back.Count == 0) return;
        var (fs, path) = _back.Pop();
        _fwd.Push((_fs, CurrentPath));
        _fs = fs;
        CurrentPath = path;
        await RefreshAsync();
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    /// <summary>Navigates forward to the next directory in the history stack.</summary>
    public async Task GoForwardAsync()
    {
        if (_fwd.Count == 0) return;
        var (fs, path) = _fwd.Pop();
        _back.Push((_fs, CurrentPath));
        _fs = fs;
        CurrentPath = path;
        await RefreshAsync();
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    /// <summary>
    /// Reloads the current directory contents.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // Snapshot once: CurrentPath can change out from under us (another navigation racing this
        // refresh, or a FileSystemWatcher debounce firing mid-flight) since nothing guards the
        // property itself - only re-entrancy of RefreshAsync is serialized below.
        var path = CurrentPath;
        if (string.IsNullOrEmpty(path))
            return;

        // No ConfigureAwait(false) here either - see the comment in NavigateAsync above. Everything
        // from _allItems.Clear() down through ApplyFilter()'s ObservableCollection mutation must
        // stay on the UI thread this method was called from.
        await _refreshLock.WaitAsync(ct);
        try
        {
            var entries = IsFlatView
                ? await _fs.EnumerateDeepAsync(path, ShowHidden, ct)
                : await _fs.EnumerateAsync(path, ShowHidden, ct);

            _allItems.Clear();

            var root = _fs.GetRootPath(path);
            var isAtRoot = string.Equals(path.TrimEnd(Path.DirectorySeparatorChar, '/'),
                root.TrimEnd(Path.DirectorySeparatorChar, '/'), StringComparison.OrdinalIgnoreCase);
            // Always show ".." when inside archive (even at root — it exits the archive)
            var showParent = !isAtRoot || FileSystem.ArchivePath.IsArchivePath(path);
            if (showParent)
                _allItems.Add(FileSystemItem.CreateParent(path));

            foreach (var e in entries)
            {
                var item = IsFlatView
                    ? new FileSystemItem(e) { DisplayName = Path.GetRelativePath(path, e.FullPath) }
                    : new FileSystemItem(e);
                _allItems.Add(item);
            }

            SortAllItems();
            ApplyFilter();

            UpdateFreeSpace(path);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogService.Error($"Refresh failed for {path}: {ex.Message}", ex);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void SortAllItems()
    {
        _allItems.Sort(new FileComparer(DirectoriesFirst, SortColumn, SortDescending));
    }

    private void RecomputeSelectionStats()
    {
        var selectedCount = 0;
        long selectedBytes = 0;
        var totalCount = 0;
        foreach (var i in Items)
        {
            if (i.IsParent) continue;
            totalCount++;
            if (!i.IsSelected) continue;
            selectedCount++;
            if (!i.IsDirectory) selectedBytes += i.Size;
        }
        _selectedCount = selectedCount;
        _selectedBytes = selectedBytes;
        _totalCount = totalCount;
    }

    private void ApplyFilter()
    {
        Items.Clear();
        foreach (var item in _allItems)
        {
            if (string.IsNullOrEmpty(Filter) || item.IsParent ||
                item.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase))
            {
                Items.Add(item);
            }
        }
        RecomputeSelectionStats();
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
        UpdateCursorInfo();
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateFreeSpace(string path)
    {
        _ = UpdateFreeSpaceAsync(path);
    }

    private async Task UpdateFreeSpaceAsync(string path)
    {
        try
        {
            // No ConfigureAwait(false): OnPropertyChanged below is observed by UI-bound controls.
            var (free, total) = await _fs.GetDriveSpaceAsync(path);
            FreeSpaceDisplay = total > 0 ? $"{FormatUtils.FormatSize(free)} / {FormatUtils.FormatSize(total)}" : "";
        }
        catch (Exception ex)
        {
            LogService.Warning($"Failed to get free space: {ex.Message}");
            FreeSpaceDisplay = "";
        }
        OnPropertyChanged(nameof(FreeSpaceDisplay));
    }

    private void UpdateCursorInfo()
    {
        CursorInfo = SelectedItem switch
        {
            null or { IsParent: true } => $"{TotalCount} items",
            { IsDirectory: true } => $"[DIR] {SelectedItem.Name}",
            _ => $"{SelectedItem.Name}  {SelectedItem.SizeDisplay}  {SelectedItem.ModifiedDisplay}"
        };
        OnPropertyChanged(nameof(CursorInfo));
    }

    // ── Selection operations ──

    /// <summary>
    /// Raises change notifications for SelectedCount/SelectedBytes after the view has
    /// mutated item.IsSelected directly (mouse click, Space toggle) without going through
    /// one of the bulk-selection methods below.
    /// </summary>
    public void NotifySelectionChanged()
    {
        RecomputeSelectionStats();
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
    }

    /// <summary>Selects all visible items except the parent entry.</summary>
    public void SelectAll()
    {
        foreach (var i in Items) if (!i.IsParent) i.IsSelected = true;
        NotifySelectionChanged();
    }

    /// <summary>Deselects all items.</summary>
    public void DeselectAll()
    {
        foreach (var i in Items) i.IsSelected = false;
        NotifySelectionChanged();
    }

    /// <summary>Toggles the selection state of every visible item (except the parent entry).</summary>
    public void InvertSelection()
    {
        foreach (var i in Items) if (!i.IsParent) i.IsSelected = !i.IsSelected;
        NotifySelectionChanged();
    }

    /// <summary>Selects all items whose names match the given wildcard pattern (e.g. <c>*.txt</c>).</summary>
    /// <param name="pattern">Wildcard pattern matched against item names.</param>
    public void SelectByPattern(string pattern)
    {
        foreach (var i in Items)
        {
            if (i.IsParent) continue;
            i.IsSelected = MatchesPattern(i.Name, pattern);
        }
        NotifySelectionChanged();
    }

    /// <summary>Deselects all items whose names match the given wildcard pattern.</summary>
    /// <param name="pattern">Wildcard pattern matched against item names.</param>
    public void DeselectByPattern(string pattern)
    {
        foreach (var i in Items)
        {
            if (i.IsParent) continue;
            if (MatchesPattern(i.Name, pattern))
                i.IsSelected = false;
        }
        NotifySelectionChanged();
    }

    /// <summary>
    /// Returns all selected items (excluding ".."). If none selected, returns the cursor item.
    /// </summary>
    public IReadOnlyList<FileSystemItem> GetSelectedOrActive()
    {
        var selected = Items.Where(i => i.IsSelected && !i.IsParent).ToList();
        if (selected.Count > 0) return selected;
        if (SelectedItem != null && !SelectedItem.IsParent) return [SelectedItem];
        return [];
    }

    private static bool MatchesPattern(string name, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return true;
        // Convert wildcard to regex
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    // ── FileSystemWatcher ──

    private void StartWatcher(string path)
    {
        StopWatcher();
        if (_fs is not LocalFileSystem) return;
        try
        {
            if (!Directory.Exists(path)) return;

            _watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFsChanged;
            _watcher.Deleted += OnFsChanged;
            _watcher.Changed += OnFsChanged;
            _watcher.Renamed += OnFsRenamed;

            _refreshDebounce = new System.Windows.Forms.Timer { Interval = DebounceMs };
            _refreshDebounce.Tick += OnDebounceTick;
        }
        catch (Exception ex)
        {
            LogService.Warning($"FileSystemWatcher failed for {path}: {ex.Message}");
        }
    }

    private void StopWatcher()
    {
        if (_watcher != null)
        {
            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFsChanged;
                _watcher.Deleted -= OnFsChanged;
                _watcher.Changed -= OnFsChanged;
                _watcher.Renamed -= OnFsRenamed;
                _watcher.Dispose();
            }
            catch { }
            _watcher = null;
        }

        if (_refreshDebounce != null)
        {
            _refreshDebounce.Stop();
            _refreshDebounce.Dispose();
            _refreshDebounce = null;
        }
    }

    private void OnFsChanged(object? sender, FileSystemEventArgs e)
    {
        ScheduleRefresh();
    }

    private void OnFsRenamed(object? sender, RenamedEventArgs e)
    {
        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        if (_refreshDebounce == null) return;
        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _refreshDebounce?.Stop();
        if (!string.IsNullOrEmpty(CurrentPath))
            _ = RefreshAsync();
    }

    /// <summary>Stops the file-system watcher, cancels pending navigation and disposes resources.</summary>
    public void Dispose()
    {
        StopWatcher();

        if (_settingsSaveDebounce is { Enabled: true })
        {
            _settingsSaveDebounce.Stop();
            SaveSortSettingsNow(); // flush a pending debounced save rather than lose it on close
        }
        _settingsSaveDebounce?.Dispose();
        _settingsSaveDebounce = null;

        var cts = _navCts;
        _navCts = null;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        cts?.Dispose();
        _refreshLock.Dispose();
    }
}

/// <summary>
/// Compares file items by the configured sort column and direction, with directories-first support.
/// </summary>
sealed class FileComparer(bool dirsFirst, string column, bool descending) : IComparer<FileSystemItem>
{
    /// <summary>Compares two <see cref="FileSystemItem"/> instances according to the sort settings.</summary>
    public int Compare(FileSystemItem? x, FileSystemItem? y)
    {
        if (x == null || y == null) return 0;

        // ".." always first
        if (x.IsParent && y.IsParent) return 0;
        if (x.IsParent) return -1;
        if (y.IsParent) return 1;

        if (dirsFirst)
        {
            var dirCmp = y.IsDirectory.CompareTo(x.IsDirectory);
            if (dirCmp != 0) return dirCmp;
        }

        int result = column switch
        {
            "Size" => x.Size.CompareTo(y.Size),
            "Modified" => x.Modified.CompareTo(y.Modified),
            "Extension" => string.Compare(x.Extension, y.Extension, StringComparison.OrdinalIgnoreCase),
            _ => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase)
        };

        return descending ? -result : result;
    }
}
