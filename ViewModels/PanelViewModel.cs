using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Services;
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

    public IFileSystem CurrentFileSystem
    {
        get => _fs;
        set => _fs = value;
    }

    public bool IsInsideArchive => _fs is FileSystem.ZipArchiveFileSystem;

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _fwd.Count > 0;

    public int SelectedCount => Items.Count(i => i.IsSelected && !i.IsParent);
    public long SelectedBytes => Items.Where(i => i.IsSelected && !i.IsParent && !i.IsDirectory).Sum(i => i.Size);
    public int TotalCount => Items.Count(i => !i.IsParent);

    public string FreeSpaceDisplay { get; private set; } = "";
    public string CursorInfo { get; private set; } = "";

    public event EventHandler? ItemsChanged;
    public event EventHandler? PathChanged;

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
        _ = RefreshAsync();
        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSortDescendingChanged(bool value)
    {
        SaveSortSettings();
        _ = RefreshAsync();
        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnDirectoriesFirstChanged(bool value)
    {
        SaveSortSettings();
        _ = RefreshAsync();
        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveSortSettings()
    {
        var s = SettingsService.Load();
        s.SortColumn = SortColumn;
        s.SortDescending = SortDescending;
        s.DirectoriesFirst = DirectoriesFirst;
        SettingsService.Save(s);
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

        if (!await _fs.ExistsAsync(path).ConfigureAwait(false))
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

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = IsFlatView
                ? await _fs.EnumerateDeepAsync(path, ShowHidden, ct).ConfigureAwait(false)
                : await _fs.EnumerateAsync(path, ShowHidden, ct).ConfigureAwait(false);

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
        _allItems = [.. _allItems.OrderBy(i => i, new FileComparer(DirectoriesFirst, SortColumn, SortDescending))];
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
            var (free, total) = await _fs.GetDriveSpaceAsync(path).ConfigureAwait(false);
            FreeSpaceDisplay = total > 0 ? $"{FormatSize(free)} / {FormatSize(total)}" : "";
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
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
    }

    public void SelectAll()
    {
        foreach (var i in Items) if (!i.IsParent) i.IsSelected = true;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
    }

    public void DeselectAll()
    {
        foreach (var i in Items) i.IsSelected = false;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
    }

    public void InvertSelection()
    {
        foreach (var i in Items) if (!i.IsParent) i.IsSelected = !i.IsSelected;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
    }

    public void SelectByPattern(string pattern)
    {
        foreach (var i in Items)
        {
            if (i.IsParent) continue;
            i.IsSelected = MatchesPattern(i.Name, pattern);
        }
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
    }

    public void DeselectByPattern(string pattern)
    {
        foreach (var i in Items)
        {
            if (i.IsParent) continue;
            if (MatchesPattern(i.Name, pattern))
                i.IsSelected = false;
        }
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
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

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{s:0.##} {u[i]}";
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

    public void Dispose()
    {
        StopWatcher();
        var cts = _navCts;
        _navCts = null;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        cts?.Dispose();
        _refreshLock.Dispose();
    }
}

/// <summary>
/// Comparer for file items (directories first, then by column).
/// </summary>
sealed class FileComparer(bool dirsFirst, string column, bool descending) : IComparer<FileSystemItem>
{
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
