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
    // Bumped at the start of every NavigateAsync/GoBackAsync/GoForwardAsync call, before any
    // await - lets NavigateAsync tell, after its own ExistsAsync await resumes, whether a newer
    // navigation call has since started (see NavigateAsync's own comment for the race this
    // closes: two fast navigations resolving out of order used to let whichever one's await
    // happened to finish last clobber CurrentPath, regardless of which was actually clicked last).
    private long _navSeq;

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

    /// <summary>
    /// <c>true</c> when this panel is looking at a virtual tree rather than the real filesystem,
    /// i.e. inside an archive of any format.
    ///
    /// Used to reject operations that have to reach around the provider to real paths - secure
    /// wipe, folder-size calculation, creating an archive. It used to be <c>_fs is
    /// ZipArchiveFileSystem</c>, which is blind to <see cref="Archives.ArchiveFileSystem"/>, the
    /// provider backing TAR, TAR.GZ, TAR.BZ2, TAR.XZ, 7z and RAR: inside any of those it answered
    /// false, so every one of those guards was skipped and the operation ran as if on a disk.
    ///
    /// Asking for the capability instead makes the answer correct for providers nobody has written
    /// yet, which is the whole point - a remote provider will have no native paths either, and the
    /// same operations must be refused there for the same reason.
    /// </summary>
    public bool IsInsideArchive => !_fs.Capabilities.HasFlag(FileSystem.FileSystemCapabilities.NativePaths);

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

        // The trailing separator is a Windows-path convention (it is what makes "C:" mean the root
        // of C: rather than the process's current directory on C:). Neither virtual flavour uses
        // it, and appending a backslash to "dav://host" would put one inside the host component -
        // where RemotePath.HostOf would then read it as part of the host name.
        bool isVirtualPath = FileSystem.ZipArchiveFileSystem.IsArchivePath(path)
            || FileSystem.RemotePath.IsRemote(path);
        if (!isVirtualPath)
            path = path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!AdoptFileSystemFor(path)) return;

        // Claim "newest navigation" before the await below, not after: two overlapping
        // NavigateAsync calls (fast double-click into folder A then quickly into folder B, or a
        // slow network path racing a fast local one) used to resolve ExistsAsync in whichever
        // order the I/O happened to finish, and the one that finished LAST always won -
        // regardless of which was actually clicked last. Capturing mySeq up front and checking it
        // after the await makes the one that STARTED last win instead, matching user intent.
        var mySeq = Interlocked.Increment(ref _navSeq);

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

        // A newer NavigateAsync/GoBackAsync/GoForwardAsync call may have already started (and
        // possibly finished) while we were awaiting ExistsAsync above - if so, committing our own
        // (now stale) path would clobber whatever that newer call already settled on.
        if (Interlocked.Read(ref _navSeq) != mySeq) return;

        var ct = BeginNavigation();

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
        await RefreshAsync(ct);
    }

    /// <summary>
    /// Makes sure the filesystem this panel is holding is the one that can actually serve
    /// <paramref name="path"/>, switching it when it is not.
    ///
    /// <para><b>Why this is necessary at all.</b> A panel keeps one filesystem and every navigation
    /// used it, whatever the path. That is fine while the path flavour never changes, and it stops
    /// being fine the moment a panel can be inside a connection: clicking the C: button then asked a
    /// WebDAV server about "C:\". The failure was not a clean refusal - the server answered the
    /// probe, so the panel set its path to C:\ and then listed the server's root under it. The panel
    /// claimed to be on a local drive while showing remote files, which is the state in which a
    /// delete does something other than what the screen says.</para>
    ///
    /// <para><b>Deliberately narrow.</b> It fires only when leaving a connection for a
    /// non-remote path, or when entering one - the two cases where the flavours genuinely disagree.
    /// Archives are left alone: they are entered and left by code that already swaps the filesystem
    /// itself, and a panel backed by some other implementation over ordinary paths is none of this
    /// method's business.</para>
    /// </summary>
    /// <returns><c>false</c> when the navigation must not proceed - a remote path whose connection
    /// is not open. Continuing would hand the path to whatever filesystem happened to be there.</returns>
    private bool AdoptFileSystemFor(string path)
    {
        if (FileSystem.RemotePath.IsRemote(path))
        {
            var connection = Services.ConnectionManager.Instance.GetConnectedForPath(path);
            if (connection is null)
            {
                LogService.Warning($"No open connection serves {FileSystem.RemotePath.GetRoot(path)}");
                return false;
            }

            if (!ReferenceEquals(connection, _fs)) _fs = connection;
            return true;
        }

        // An archive path is served by the filesystem that was installed when the archive was
        // entered; that machinery swaps it back on the way out and is none of this method's business.
        if (FileSystem.ZipArchiveFileSystem.IsArchivePath(path)) return true;

        // Leaving a connection for an ordinary path. Keyed off the filesystem being one the
        // connection manager has open - not off the current path, which is the mistake that made
        // the first version of this useless: the panel can be holding a connection's filesystem
        // while its path is still the local one from before, and that is exactly the case that
        // needs catching. Asking the manager also leaves archives and the fakes tests use alone,
        // since neither is ever one of its live connections.
        if (Services.ConnectionManager.Instance.IsConnectionFileSystem(_fs))
            _fs = new FileSystem.LocalFileSystem();

        return true;
    }

    /// <summary>Cancels any in-flight navigation's own RefreshAsync and starts tracking a new
    /// one, returning the token this navigation's RefreshAsync should use so a later navigation
    /// can in turn cancel it. Shared by NavigateAsync/GoBackAsync/GoForwardAsync so none of them
    /// can leave a superseded refresh running against what's now the wrong CurrentPath.</summary>
    private CancellationToken BeginNavigation()
    {
        lock (_navLock)
        {
            _navCts?.Cancel();
            _navCts?.Dispose();
            _navCts = new CancellationTokenSource();
            return _navCts.Token;
        }
    }

    /// <summary>
    /// Navigates to parent directory.
    /// </summary>
    public async Task GoToParentAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        if (FileSystem.RemotePath.IsRemote(CurrentPath))
        {
            // Path.GetFullPath on "dav://host/dir\.." does not fail loudly - it resolves the string
            // against the process's current directory and hands back a local path that has nothing
            // to do with the server. Going up one level in a connection has to be remote-path
            // arithmetic, and at the connection root there is simply nowhere up to go.
            var remoteParent = FileSystem.VfsPath.GetParent(CurrentPath);
            if (!string.IsNullOrEmpty(remoteParent))
            {
                await NavigateAsync(remoteParent);
                return;
            }

            // Already at the connection's root, so "up" means out of it - the same thing ".." does
            // at the root of an archive. Without this the panel had no way back to a local drive
            // from inside a connection at all.
            await NavigateAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            return;
        }

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
        Interlocked.Increment(ref _navSeq);
        var ct = BeginNavigation();
        var (fs, path) = _back.Pop();
        _fwd.Push((_fs, CurrentPath));
        _fs = fs;
        CurrentPath = path;
        await RefreshAsync(ct);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    /// <summary>Navigates forward to the next directory in the history stack.</summary>
    public async Task GoForwardAsync()
    {
        if (_fwd.Count == 0) return;
        Interlocked.Increment(ref _navSeq);
        var ct = BeginNavigation();
        var (fs, path) = _fwd.Pop();
        _back.Push((_fs, CurrentPath));
        _fs = fs;
        CurrentPath = path;
        await RefreshAsync(ct);
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

            // Every entry becomes a brand-new FileSystemItem below, so IsSelected/SelectedItem
            // would otherwise silently reset on every refresh - including one the user never
            // asked for (a FileSystemWatcher debounce firing because an antivirus/sync client
            // touched the folder). Carried across by FullPath, the one thing that still
            // identifies "the same entry" once the old objects are discarded.
            var selectedPaths = new HashSet<string>(
                _allItems.Where(i => !i.IsParent && i.IsSelected).Select(i => i.FullPath),
                StringComparer.OrdinalIgnoreCase);
            var selectedItemPath = SelectedItem is { IsParent: false } si ? si.FullPath : null;

            _allItems.Clear();

            var root = _fs.GetRootPath(path);
            var isAtRoot = string.Equals(path.TrimEnd(Path.DirectorySeparatorChar, '/'),
                root.TrimEnd(Path.DirectorySeparatorChar, '/'), StringComparison.OrdinalIgnoreCase);
            // Always show ".." inside an archive or a connection, even at their root, because there
            // it is what leaves them. A panel with no ".." at the root of a connection has no
            // visible way out.
            var showParent = !isAtRoot
                || FileSystem.ArchivePath.IsArchivePath(path)
                || FileSystem.RemotePath.IsRemote(path);
            if (showParent)
                _allItems.Add(FileSystemItem.CreateParent(path));

            foreach (var e in entries)
            {
                var item = IsFlatView
                    // VfsPath, not Path: in flat view over an archive or a connection, the two
                    // paths are not Windows paths and GetRelativePath would resolve them against
                    // the process's current directory.
                    ? new FileSystemItem(e) { DisplayName = FileSystem.VfsPath.GetRelative(path, e.FullPath) }
                    : new FileSystemItem(e);
                if (selectedPaths.Contains(item.FullPath))
                    item.IsSelected = true;
                _allItems.Add(item);
            }

            SortAllItems();
            ApplyFilter();

            // Restored after ApplyFilter (which nulls SelectedItem if its old, now-stale object
            // reference isn't among the new items) so the newly-created item with a matching
            // FullPath - rather than the discarded old one - becomes the cursor.
            if (selectedItemPath != null)
                SelectedItem = _allItems.FirstOrDefault(i =>
                    !i.IsParent && string.Equals(i.FullPath, selectedItemPath, StringComparison.OrdinalIgnoreCase));

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
        var visible = new HashSet<FileSystemItem>();
        foreach (var item in _allItems)
        {
            if (string.IsNullOrEmpty(Filter) || item.IsParent ||
                item.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase))
            {
                Items.Add(item);
                visible.Add(item);
            }
        }

        // A filter must never leave something checkbox-selected, or the cursor item, hidden from
        // view: GetSelectedOrActive falls back to SelectedItem when nothing is checkbox-selected
        // (an operation could then silently target a file the filter is hiding), and the bulk
        // Select All/Deselect All/Invert commands only ever touch the currently-visible Items -
        // so a checkbox-selected item that becomes hidden here would otherwise survive untouched
        // by those commands and resurface, still selected, the moment the filter is cleared or
        // widened, with the selection count shown while filtered not even including it.
        foreach (var item in _allItems)
        {
            if (!visible.Contains(item))
                item.IsSelected = false;
        }
        if (SelectedItem != null && !visible.Contains(SelectedItem))
            SelectedItem = null;

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

    /// <summary>
    /// Re-raises <see cref="ItemsChanged"/> to make the panel redraw its rows from the
    /// already-loaded <see cref="Items"/>, without touching the file system - for an out-of-band
    /// mutation of an existing item (e.g. a background folder-size calculation completing) that
    /// needs the display refreshed but doesn't warrant a full <see cref="RefreshAsync"/>.
    /// </summary>
    public void RefreshDisplay() => ItemsChanged?.Invoke(this, EventArgs.Empty);

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
        if (!_fs.Capabilities.HasFlag(FileSystem.FileSystemCapabilities.FileWatch)) return;
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

        if (result != 0)
            return descending ? -result : result;

        // Tie-breaker: List<T>.Sort (introsort) isn't stable, so two entries with an equal
        // primary-column key (e.g. same Size, same Modified) can swap position on every re-sort
        // with no visible cause - toggling DirectoriesFirst, a FileSystemWatcher-triggered
        // RefreshAsync, etc. Always ascending by name regardless of `descending`, so ties settle
        // into one consistent order no matter which direction the primary sort runs.
        return column == "Name" ? 0 : string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
    }
}
