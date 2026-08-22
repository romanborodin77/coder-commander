using CoderCommander.Archives;
using CoderCommander.Commands;
using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Operations;
using CoderCommander.Services;
using CoderCommander.Utils;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoderCommander.ViewModels;

/// <summary>
/// Main application ViewModel: owns both panels, operation manager, command engine.
//// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>Provides local file-system access used by both panels.</summary>
    public IFileSystem FileSystem { get; }

    /// <summary>Queue and scheduler for copy, move, delete, pack and unpack operations.</summary>
    public OperationManager Operations { get; }

    /// <summary>Registry of named commands that the UI and hotkeys invoke.</summary>
    public CommandEngine Commands { get; }

    /// <summary>Maps keyboard shortcuts to <see cref="Commands"/> entries.</summary>
    public HotkeyManager Hotkeys { get; }

    /// <summary>Every tab on the left side - <see cref="LeftPanel"/> is <c>_leftTabs.Active</c>.
    /// A single-tab set until Ф3's tab UI (Ctrl+T et al.) actually adds more; every existing
    /// caller of <see cref="LeftPanel"/> keeps working unmodified against whichever tab is active.</summary>
    private readonly PanelTabSet _leftTabs = new();

    /// <summary>Every tab on the right side - see <see cref="_leftTabs"/>' own doc comment.</summary>
    private readonly PanelTabSet _rightTabs = new();

    /// <summary>Left panel ViewModel - resolves to whichever tab is currently active on the left
    /// side (see <see cref="PanelTabSet"/>), so a tab switch is transparent to every one of this
    /// property's many existing readers.</summary>
    public PanelViewModel LeftPanel => _leftTabs.Active;

    /// <summary>Right panel ViewModel - see <see cref="LeftPanel"/>'s own doc comment.</summary>
    public PanelViewModel RightPanel => _rightTabs.Active;

    /// <summary>Every panel across both sides, across every tab - not just the two currently
    /// active ones (<see cref="LeftPanel"/>/<see cref="RightPanel"/>). Used by maintenance sweeps
    /// (archive-lease dirty marking after a pack/unpack, evicting a panel from a connection that
    /// just closed) that must not silently skip a background tab.</summary>
    public IEnumerable<PanelViewModel> AllPanels => _leftTabs.Tabs.Concat(_rightTabs.Tabs);

    /// <summary>Every tab on the left side, in display order - for tab-strip UI and settings
    /// persistence. Use <see cref="LeftPanel"/> for "the currently active one."</summary>
    public IReadOnlyList<PanelViewModel> LeftTabs => _leftTabs.Tabs;

    /// <summary>Right-side counterpart of <see cref="LeftTabs"/>.</summary>
    public IReadOnlyList<PanelViewModel> RightTabs => _rightTabs.Tabs;

    /// <summary>Index of <see cref="LeftPanel"/> within <see cref="LeftTabs"/> - for settings
    /// persistence (which tab was active when the window closed).</summary>
    public int LeftActiveTabIndex => _leftTabs.ActiveIndex;

    /// <summary>Right-side counterpart of <see cref="LeftActiveTabIndex"/>.</summary>
    public int RightActiveTabIndex => _rightTabs.ActiveIndex;

    /// <summary>Raised whenever the left side's tab set changes shape or its active tab switches -
    /// a tab-strip UI redraws its buttons/highlight from <see cref="LeftTabs"/>/
    /// <see cref="LeftActiveTabIndex"/> in response. Does <em>not</em> fire when a tab merely
    /// navigates to a different path within itself - see <see cref="PanelPathChanged"/> for that.</summary>
    public event EventHandler? LeftTabsChanged;

    /// <summary>Right-side counterpart of <see cref="LeftTabsChanged"/>.</summary>
    public event EventHandler? RightTabsChanged;

    /// <summary>Activates the tab at <paramref name="index"/> on one side - what a tab-strip click
    /// ultimately calls. Out-of-range or already-active is a no-op (<see cref="PanelTabSet.SetActive"/>'s
    /// own guard).</summary>
    public void SetActiveTabIndex(bool left, int index) => (left ? _leftTabs : _rightTabs).SetActive(index);

    /// <summary>Which side currently has focus - <see cref="ActivePanel"/> resolves against this
    /// plus whichever tab is active on that side, so switching tabs on the focused side keeps
    /// <see cref="ActivePanel"/> pointing at the right instance without every command handler that
    /// reads it needing to re-subscribe to anything.</summary>
    private bool _isLeftActive = true;

    /// <summary>Currently focused panel (left or right). Not <c>[ObservableProperty]</c>-backed -
    /// a plain computed property so it can never itself go stale the way a stored reference would
    /// the moment the focused side's active tab changes; <see cref="SetActivePanel"/> and the tab
    /// sets' own <c>ActiveChanged</c> (wired in the constructor) raise
    /// <see cref="ObservableObject.OnPropertyChanged(string?)"/> for it manually, matching what the
    /// source generator used to do automatically.</summary>
    public PanelViewModel ActivePanel => _isLeftActive ? LeftPanel : RightPanel;

    /// <summary>The panel that is <em>not</em> currently focused — used as the transfer destination.</summary>
    public PanelViewModel InactivePanel => _isLeftActive ? RightPanel : LeftPanel;

    /// <summary>Text shown in the main status bar (cursor info, selection, free space).</summary>
    [ObservableProperty] private string _statusText = "";

    /// <summary>Non-empty when one or more background operations are queued.</summary>
    [ObservableProperty] private string _operationQueueText = "";

    /// <summary>Captured on the UI thread at construction time so that
    /// <see cref="OnOperationChanged"/> — which fires from a thread-pool thread after
    /// <see cref="Operations.OperationManager"/> completes an operation — can marshal
    /// <see cref="PanelViewModel.RefreshAsync"/> back to the UI thread. Without this,
    /// RefreshAsync's ObservableCollection mutations run on a background thread and race
    /// against the ListView reading them on the UI thread.</summary>
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    /// <summary>Initialises the file system, operation manager, command engine and both panels.</summary>
    public MainViewModel()
    {
        FileSystem = new LocalFileSystem();
        Operations = new OperationManager();
        Commands = new CommandEngine();
        Hotkeys = new HotkeyManager(Commands);

        // Every tab (not just the active one) stays subscribed for its whole lifetime - see
        // PanelTabSet's own doc comment for why re-wiring on activation switch isn't needed.
        // TabCreated also fires for the two ctor-time tabs below, but MainForm has nothing
        // subscribed to it yet at that point (it constructs this ViewModel first, then wires its
        // own events) - harmless, since MainForm's WireEvents sets ConfirmArchiveWriteBack/
        // ArchiveWriteBackFailed on those two tabs directly via a one-time loop over AllPanels.
        // TabCreated exists purely for tabs created AFTER that point (AddTab, tab restore), which
        // have no such loop to fall back on.
        _leftTabs.TabAdded += (_, panel) => { WirePanelEvents(panel); TabCreated?.Invoke(this, panel); };
        _rightTabs.TabAdded += (_, panel) => { WirePanelEvents(panel); TabCreated?.Invoke(this, panel); };
        _leftTabs.TabRemoving += (_, panel) => UnwirePanelEvents(panel);
        _rightTabs.TabRemoving += (_, panel) => UnwirePanelEvents(panel);
        // ActiveChanged fires for a tab switch on EITHER side - LeftPanel/RightPanel always need
        // to re-announce themselves (something may be bound to that specific side regardless of
        // focus), but ActivePanel/InactivePanel only actually changed if the switch happened on
        // whichever side currently has focus.
        // Also the one signal a tab-strip UI needs to know its tab count/order/active-highlight
        // changed (LeftTabsChanged/RightTabsChanged below) - ActiveChanged already fires for every
        // mutation (AddTab and CloseTab both end with it, not just SetActive), so there's no need
        // for a second event per kind of change.
        _leftTabs.ActiveChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(LeftPanel));
            if (_isLeftActive)
            {
                OnPropertyChanged(nameof(ActivePanel));
                OnPropertyChanged(nameof(InactivePanel));
                ActiveSelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            LeftTabsChanged?.Invoke(this, EventArgs.Empty);
        };
        _rightTabs.ActiveChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(RightPanel));
            if (!_isLeftActive)
            {
                OnPropertyChanged(nameof(ActivePanel));
                OnPropertyChanged(nameof(InactivePanel));
                ActiveSelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            RightTabsChanged?.Invoke(this, EventArgs.Empty);
        };

        _leftTabs.AddTab(new PanelViewModel(FileSystem));
        _rightTabs.AddTab(new PanelViewModel(FileSystem));

        LeftPanel.IsActive = true;
        RightPanel.IsActive = false;

        // Wire operation manager events
        Operations.OperationChanged += OnOperationChanged;

        RegisterCommands();
        Hotkeys.Reload(SettingsService.Load().CustomHotkeys);
    }

    private void WirePanelEvents(PanelViewModel panel)
    {
        panel.PathChanged += OnPanelPathChanged;
        panel.PropertyChanged += OnPanelPropertyChanged;
        panel.ItemsChanged += OnPanelItemsChanged;
    }

    private void UnwirePanelEvents(PanelViewModel panel)
    {
        panel.PathChanged -= OnPanelPathChanged;
        panel.PropertyChanged -= OnPanelPropertyChanged;
        panel.ItemsChanged -= OnPanelItemsChanged;
    }

    // ── Panel management ──

    /// <summary>Makes <paramref name="panel"/> the active (focused) side's panel, deactivating the
    /// other side. <paramref name="panel"/> must be the CURRENT <see cref="LeftPanel"/> or
    /// <see cref="RightPanel"/> instance - whichever one it reference-equals decides which side
    /// gains focus.</summary>
    public void SetActivePanel(PanelViewModel panel)
    {
        if (ReferenceEquals(panel, ActivePanel)) return;
        ActivePanel.IsActive = false;
        _isLeftActive = ReferenceEquals(panel, LeftPanel);
        ActivePanel.IsActive = true;
        OnPropertyChanged(nameof(ActivePanel));
        OnPropertyChanged(nameof(InactivePanel));
        UpdateStatus();
        ActiveSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised whenever the active panel's <see cref="PanelViewModel.SelectedItem"/> might
    /// have changed - either its own selection moved, or a different panel/tab became active.
    /// Quick View (Ф4, <c>FilePanelUserControl.RefreshQuickViewPreview</c>) uses this to preview
    /// whatever the ACTIVE panel currently has selected inside whichever OTHER panel has Quick
    /// View turned on - matching Total Commander's own convention of "the passive panel previews
    /// what's selected in the active one," which is also what keeps the file list's own keyboard
    /// focus (and arrow-key browsing) working normally in the panel actually being browsed.</summary>
    public event EventHandler? ActiveSelectionChanged;

    /// <summary>Raised by the <c>Ctrl+Q</c> command - <c>MainForm</c> decides which panel that
    /// means (always the currently inactive one, never the one being browsed) and calls
    /// <c>FilePanelUserControl.SetQuickView</c>.</summary>
    public event EventHandler? QuickViewToggleRequested;

    /// <summary>Toggles Quick View on the inactive panel - the <c>Ctrl+Q</c> command.</summary>
    public void ToggleQuickView() => QuickViewToggleRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Raised right after a new tab is created and fully wired (see
    /// <see cref="WirePanelEvents"/>) - lets <c>MainForm</c> set the per-panel UI-level delegates
    /// (<see cref="PanelViewModel.ConfirmArchiveWriteBack"/>/<see cref="PanelViewModel.ArchiveWriteBackFailed"/>)
    /// it would otherwise only ever set once, at startup, on the two panels that existed then.</summary>
    public event EventHandler<PanelViewModel>? TabCreated;

    /// <summary>Opens a new tab on <paramref name="left"/>'s side, starting at whatever path that
    /// side's currently-active tab is showing (mirrors the "duplicate this tab" convention most
    /// tabbed apps use) - empty for a panel that hasn't navigated anywhere yet, which just leaves
    /// the new tab unnavigated too, same as the two ctor-time tabs before <c>MainForm.InitializeAsync</c>
    /// first calls <c>NavigateAsync</c> on them.</summary>
    public PanelViewModel AddTab(bool left)
    {
        var currentPath = (left ? LeftPanel : RightPanel).CurrentPath;
        var vm = new PanelViewModel(FileSystem);
        (left ? _leftTabs : _rightTabs).AddTab(vm);
        if (!string.IsNullOrEmpty(currentPath))
            _ = vm.NavigateAsync(currentPath);
        return vm;
    }

    /// <summary>Opens a new tab on whichever side currently has focus - the <c>Ctrl+T</c> command.</summary>
    public void AddTabToActiveSide() => AddTab(_isLeftActive);

    /// <summary>Restores a persisted tab set on one side at startup. <paramref name="paths"/> must
    /// be non-empty (the caller falls back to a single default path otherwise - see
    /// <c>MainForm.InitializeAsync</c>). The side's existing ctor-time tab is reused for the first
    /// path rather than closed and replaced, so the UI-level delegates <c>MainForm.WireEvents</c>
    /// already set on it via its startup loop over <see cref="AllPanels"/> keep working unmodified;
    /// only additional entries create genuinely new tabs, which go through <see cref="TabCreated"/>
    /// like any other <see cref="AddTab"/> call. Paths are navigated sequentially, matching how the
    /// pre-tabs single-path restore already awaited the left panel before starting the right one.</summary>
    public async Task RestoreTabsAsync(bool left, IReadOnlyList<string> paths, int activeIndex)
    {
        if (paths.Count == 0) return;
        var set = left ? _leftTabs : _rightTabs;

        await set.Tabs[0].NavigateAsync(paths[0]).ConfigureAwait(true);
        for (var i = 1; i < paths.Count; i++)
        {
            var vm = new PanelViewModel(FileSystem);
            set.AddTab(vm);
            await vm.NavigateAsync(paths[i]).ConfigureAwait(true);
        }

        set.SetActive(Math.Clamp(activeIndex, 0, set.Tabs.Count - 1));
    }

    /// <summary>Closes <paramref name="panel"/>'s tab - a no-op if it's the last tab on its side
    /// (see <see cref="PanelTabSet.CloseTab"/>'s own doc comment for why that's gated here, not
    /// inside the tab set) or if it isn't a currently-open tab on either side at all (already
    /// closed, or a stale reference). If the tab holds a dirty archive lease, offers the same
    /// write-back confirmation an ordinary navigation-away-from-the-archive would
    /// (<see cref="PanelViewModel.ReleaseArchiveLeaseAsync"/>) before the tab and its lease are
    /// torn down - closing a tab must not be a quieter way to discard edits than leaving the
    /// archive normally is.</summary>
    public async Task CloseTabAsync(PanelViewModel panel)
    {
        var set = _leftTabs.IndexOf(panel) >= 0 ? _leftTabs
            : _rightTabs.IndexOf(panel) >= 0 ? _rightTabs
            : null;
        if (set == null || set.Tabs.Count <= 1) return;

        if (panel.HasArchiveLease)
            await panel.ReleaseArchiveLeaseAsync().ConfigureAwait(true);

        set.CloseTab(set.IndexOf(panel));
    }

    /// <summary>Closes the currently focused tab - the <c>Ctrl+W</c> command.</summary>
    public Task CloseActiveTab() => CloseTabAsync(ActivePanel);

    /// <summary>Switches to the next/previous tab on whichever side has focus, wrapping around -
    /// the <c>Ctrl+PageDown</c>/<c>Ctrl+PageUp</c> commands.</summary>
    public void NextTab() => StepActiveTab(1);

    /// <summary>See <see cref="NextTab"/>.</summary>
    public void PreviousTab() => StepActiveTab(-1);

    private void StepActiveTab(int direction)
    {
        var set = _isLeftActive ? _leftTabs : _rightTabs;
        if (set.Tabs.Count <= 1) return;
        var next = ((set.ActiveIndex + direction) % set.Tabs.Count + set.Tabs.Count) % set.Tabs.Count;
        set.SetActive(next);
    }

    /// <summary>Swaps the paths of the left and right panels asynchronously.</summary>
    public void SwapPanels()
    {
        _ = SwapPanelsAsync();
    }

    private async Task SwapPanelsAsync()
    {
        try
        {
            var leftPath = LeftPanel.CurrentPath;
            var rightPath = RightPanel.CurrentPath;
            await LeftPanel.NavigateAsync(rightPath);
            await RightPanel.NavigateAsync(leftPath);
        }
        catch (Exception ex)
        {
            LogService.Error($"SwapPanels failed: {ex.Message}", ex);
        }
    }

    /// <summary>Navigates the inactive panel to the same path as the active panel.</summary>
    public void TargetEqualSource()
    {
        _ = TargetEqualSourceAsync();
    }

    private async Task TargetEqualSourceAsync()
    {
        try
        {
            await InactivePanel.NavigateAsync(ActivePanel.CurrentPath);
        }
        catch (Exception ex)
        {
            LogService.Error($"TargetEqualSource failed: {ex.Message}", ex);
        }
    }

    // ── Command registration ──

    private void RegisterCommands()
    {
        Commands.Register(CommandIds.Copy, _ => Copy());
        Commands.Register(CommandIds.Move, _ => Move());
        Commands.Register(CommandIds.Delete, _ => Delete());
        Commands.Register(CommandIds.Wipe, _ => Wipe());
        Commands.Register(CommandIds.MakeDir, _ => MakeDir());
        Commands.Register(CommandIds.Rename, _ => Rename());
        Commands.Register(CommandIds.GoBack, p => { _ = SafeExecuteAsync(() => ActivePanel.GoBackAsync(), "GoBack"); });
        Commands.Register(CommandIds.GoForward, p => { _ = SafeExecuteAsync(() => ActivePanel.GoForwardAsync(), "GoForward"); });
        Commands.Register(CommandIds.GoToParent, p => { _ = SafeExecuteAsync(() => ActivePanel.GoToParentAsync(), "GoToParent"); });
        Commands.Register(CommandIds.Refresh, p => { _ = SafeExecuteAsync(() => ActivePanel.RefreshAsync(), "Refresh"); });
        Commands.Register(CommandIds.RefreshDrives, p => { _ = SafeExecuteAsync(() => DriveCatalog.Instance.RefreshAsync(), "RefreshDrives"); });
        Commands.Register(CommandIds.SelectAll, _ => ActivePanel.SelectAll());
        Commands.Register(CommandIds.DeselectAll, _ => ActivePanel.DeselectAll());
        Commands.Register(CommandIds.InvertSelection, _ => ActivePanel.InvertSelection());
        Commands.Register(CommandIds.NewTab, _ => AddTabToActiveSide());
        Commands.Register(CommandIds.CloseTab, p => { _ = SafeExecuteAsync(CloseActiveTab, "CloseTab"); });
        Commands.Register(CommandIds.NextTab, _ => NextTab());
        Commands.Register(CommandIds.PreviousTab, _ => PreviousTab());
        Commands.Register(CommandIds.ToggleQuickView, _ => ToggleQuickView());
        Commands.Register(CommandIds.SwapPanels, _ => SwapPanels());
        Commands.Register(CommandIds.TargetEqualSource, _ => TargetEqualSource());
        Commands.Register(CommandIds.SyncDirs, _ => SyncDirs());
        Commands.Register(CommandIds.ToggleHidden, _ => ActivePanel.ShowHidden = !ActivePanel.ShowHidden);
        Commands.Register(CommandIds.ToggleFlatView, _ => ActivePanel.IsFlatView = !ActivePanel.IsFlatView);
        Commands.Register(CommandIds.ToggleQuickFilter, _ => QuickFilterToggleRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.View, p => { _ = SafeExecuteAsync(() => ViewFileAsync(), "View"); });
        Commands.Register(CommandIds.Edit, p => { _ = SafeExecuteAsync(() => EditFileAsync(), "Edit"); });
        Commands.Register(CommandIds.SetTheme, param => SetTheme(param ?? "Dark"));
        Commands.Register(CommandIds.Exit, _ => ExitRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.About, _ => AboutRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.ShowProperties, _ => ShowProperties());
        Commands.Register(CommandIds.CalculateFolderSize, _ => CalculateFolderSize());
        Commands.Register(CommandIds.DiskInfo, p => { _ = SafeExecuteAsync(ShowDiskInfoAsync, "DiskInfo"); });
        Commands.Register(CommandIds.MultiRename, _ => MultiRename());
        Commands.Register(CommandIds.GoToRoot, _ => GoToRoot());
        Commands.Register(CommandIds.GoToHome, _ => GoToHome());
        Commands.Register(CommandIds.ChangeDir, _ => ChangeDir());
        Commands.Register(CommandIds.SelectGroup, _ => SelectGroup());
        Commands.Register(CommandIds.DeselectGroup, _ => DeselectGroup());
        Commands.Register(CommandIds.EditNew, _ => EditNewRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.PackFiles, _ => PackFiles());
        Commands.Register(CommandIds.UnpackFiles, _ => UnpackFiles());
        Commands.Register(CommandIds.Checksum, _ => ChecksumRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.SplitFile, _ => SplitFiles());
        Commands.Register(CommandIds.CombineFiles, _ => CombineFiles());
        Commands.Register(CommandIds.FindFiles, _ => FindFilesRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.FindDuplicates, _ => FindDuplicatesRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.ToggleTerminal, _ => ToggleTerminalRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.CreateTerminalTab, _ => CreateTerminalTabRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.CloseTerminalTab, _ => CloseTerminalTabRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.NextTerminalTab, _ => NextTerminalTabRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.PreviousTerminalTab, _ => PreviousTerminalTabRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.SetSortColumn, param => { if (param != null) ActivePanel.SortColumn = param; });
        Commands.Register(CommandIds.SetSortDescending, _ => ActivePanel.SortDescending = !ActivePanel.SortDescending);
        Commands.Register(CommandIds.SetDirectoriesFirst, _ => ActivePanel.DirectoriesFirst = !ActivePanel.DirectoriesFirst);
        Commands.Register(CommandIds.ToggleShowExtensionInName, _ => ToggleShowExtensionInName());
    }

    // ── File operations ──

    /// <summary>Copies selected items to the inactive panel's directory, respecting overwrite settings.</summary>
    public void Copy()
    {
        var files = ActivePanel.GetSelectedOrActive();
        if (files.Count == 0) return;

        var destPath = InactivePanel.CurrentPath;
        var s = SettingsService.Load();

        if (!s.ConfirmOverwrite)
        {
            var options = new TransferOptions
            {
                CopyAttributes = s.CopyAttributes,
                CopyTimestamps = s.CopyTimestamps,
                Overwrite = true
            };
            ExecuteCopy(files, destPath, options);
            return;
        }

        CopyConfirmRequested?.Invoke(this, (files, ActivePanel.CurrentPath, destPath));
    }

    /// <summary>Queues a copy operation with the given transfer options.</summary>
    /// <param name="files">Items to copy.</param>
    /// <param name="destPath">Destination directory path.</param>
    /// <param name="options">Transfer behaviour flags (overwrite, timestamps, compression).</param>
    public void ExecuteCopy(IReadOnlyList<Models.FileSystemItem> files, string destPath, TransferOptions options)
        => StartTransfer(files, destPath, options, move: false);

    /// <summary>Moves selected items to the inactive panel's directory, respecting overwrite settings.</summary>
    public void Move()
    {
        var files = ActivePanel.GetSelectedOrActive();
        if (files.Count == 0) return;

        var destPath = InactivePanel.CurrentPath;
        LogService.Info($"Move: {files.Count} files, dest={destPath}, ConfirmOverwrite={SettingsService.Load().ConfirmOverwrite}");
        var s = SettingsService.Load();

        if (!s.ConfirmOverwrite)
        {
            var options = new TransferOptions
            {
                CopyAttributes = s.CopyAttributes,
                CopyTimestamps = s.CopyTimestamps,
                Overwrite = true
            };
            ExecuteMove(files, destPath, options);
            return;
        }

        MoveConfirmRequested?.Invoke(this, (files, ActivePanel.CurrentPath, destPath));
    }

    /// <summary>Queues a move operation with the given transfer options.</summary>
    /// <param name="files">Items to move.</param>
    /// <param name="destPath">Destination directory path.</param>
    /// <param name="options">Transfer behaviour flags (overwrite, timestamps, compression).</param>
    public void ExecuteMove(IReadOnlyList<Models.FileSystemItem> files, string destPath, TransferOptions options)
        => StartTransfer(files, destPath, options, move: true);

    private void StartTransfer(
        IReadOnlyList<Models.FileSystemItem> files,
        string destPath,
        TransferOptions options,
        bool move)
        => ExecuteTransfer(ActivePanel.CurrentFileSystem, ActivePanel.CurrentPath,
            files.Select(f => f.Entry).ToList(), destPath, options, move);

    /// <summary>
    /// Routes a copy/move to the operation that fits the two endpoints. Crossing an archive
    /// boundary turns the transfer into a pack or an unpack, which is what makes plain F5/F6
    /// (and drag &amp; drop) work as archive commands.
    /// </summary>
    public void ExecuteTransfer(
        IFileSystem sourceFs,
        string sourceBase,
        IReadOnlyList<FileEntry> entries,
        string destPath,
        TransferOptions options,
        bool move)
    {
        if (entries.Count == 0 || string.IsNullOrWhiteSpace(destPath)) return;

        var sourceArchive = VfsPath.GetArchiveFile(sourceBase);
        var destArchive = VfsPath.GetArchiveFile(destPath);
        var fromArchive = VfsPath.IsArchive(sourceBase);
        var intoArchive = VfsPath.IsArchive(destPath);
        // Was a raw "Move"/"Copy" literal - unlike every other operation's displayName (Delete/
        // Wipe/Pack/Unpack/SyncDirs all already resolve through Op.Display*), this one never went
        // through localization, so the progress dialog header always showed English text under
        // Russian UI (caught by visual inspection of a live build).
        var displayName = Services.LocalizationService.Current.GetString(
            move ? "Op.DisplayMove" : "Op.DisplayCopy", entries.Count, destPath);

        if (fromArchive && intoArchive &&
            string.Equals(sourceArchive, destArchive, StringComparison.OrdinalIgnoreCase))
        {
            OperationRejected?.Invoke(this, "Archive.SameArchiveTransfer");
            return;
        }

        // CA2000 flags every "new XxxOperation(...)" below because it can't see across the
        // Operations.RunAsync(op, ...) call at the bottom of this method - RunAsync (and the
        // OperationManager it queues into) now disposes the operation once it reaches a terminal
        // state (audit Phase 6, DEBUG.md §0.4: this used to be a real, unfixed leak - every
        // operation's CancellationTokenSource lived until the app closed). Ownership genuinely
        // transfers to RunAsync here; the warning is a false positive after that fix, not before it.
#pragma warning disable CA2000
        IFileOperation op;

        if (intoArchive)
        {
            // Guards against packing a folder into an archive file that physically lives inside
            // that same folder (destArchive is the archive's real path, not a VFS path - safe to
            // reuse the same real-disk containment check the plain-filesystem branch below uses).
            // Only relevant when the source is real files (fromArchive == false): packing FROM
            // inside an archive can't have this containment relationship with a destination
            // archive on real disk, and the same-archive case above already handles archive-to-
            // archive. Without this, packing/moving a folder into an archive inside it lets
            // PackOperation write the archive into itself, then (on Move)
            // PackOperation.RemoveSourcesAsync deletes the whole source folder afterward -
            // including the archive it just finished writing.
            if (!fromArchive && IsDestinationInsideSource(sourceBase, destArchive, entries))
            {
                OperationRejected?.Invoke(this, "Transfer.SourceEqualsDestination");
                return;
            }

            var rejectKey = ValidatePackTargetFormat(destArchive);
            if (rejectKey != null)
            {
                OperationRejected?.Invoke(this, rejectKey);
                return;
            }

            var destArchiveFs = ResolveContainerFileSystem(destArchive);
            if (destArchiveFs is null)
            {
                OperationRejected?.Invoke(this, "Conn.NotConnected");
                return;
            }

            op = new PackOperation(sourceFs, entries, sourceBase, destArchiveFs, destArchive,
                VfsPath.GetInner(destPath), options, removeSource: move);
            // Not the shared Operations.RunAsync(op, ...) call at the bottom of this method - a
            // pack into an archive needs the extra step of syncing an already-attached panel lease
            // once the operation actually finishes, see RunPackAndSyncLeaseAsync's own doc comment.
            // options.AddToQueue is deliberately not honored here: RunPackAndSyncLeaseAsync awaits
            // RunAsync itself, so there is no equivalent "start later" hook to defer it to without
            // also deferring the lease-sync step - packing into an archive always starts immediately.
            _ = RunPackAndSyncLeaseAsync(op, displayName, destArchive);
#pragma warning restore CA2000
            return;
        }

        // Re-disabled for the two remaining branches below - the intoArchive branch above already
        // restored it (and returned) once its own op was safely handed off.
#pragma warning disable CA2000
        if (fromArchive)
        {
            var unpackDestFs = ResolveFileSystem(destPath);
            if (unpackDestFs is null)
            {
                OperationRejected?.Invoke(this, "Conn.NotConnected");
                return;
            }

            var sourceArchiveFs = ResolveContainerFileSystem(sourceArchive);
            if (sourceArchiveFs is null)
            {
                OperationRejected?.Invoke(this, "Conn.NotConnected");
                return;
            }

            op = new UnpackOperation(sourceArchiveFs, sourceArchive, entries, VfsPath.GetInner(sourceBase),
                unpackDestFs, destPath, options, removeSource: move)
            {
                RequestPassword = RaiseArchivePasswordRequested
            };
        }
        else
        {
            if (IsDestinationInsideSource(sourceBase, destPath, entries))
            {
                OperationRejected?.Invoke(this, "Transfer.SourceEqualsDestination");
                return;
            }

            var destFs = ResolveFileSystem(destPath);
            if (destFs is null)
            {
                // The destination panel is showing a connection that has since dropped. Falling
                // back to the local filesystem here would be far worse than refusing: the remote
                // path would be resolved against the current directory and the files written
                // somewhere on disk the user never chose.
                OperationRejected?.Invoke(this, "Conn.NotConnected");
                return;
            }

            op = move
                ? new MoveOperation(sourceFs, destFs, entries, sourceBase, destPath, options)
                : new CopyOperation(sourceFs, destFs, entries, sourceBase, destPath, options);
        }

        if (options.AddToQueue)
            Operations.Enqueue(op, displayName);
        else
            _ = Operations.RunAsync(op, displayName);
#pragma warning restore CA2000
    }

    /// <summary>
    /// Runs a <see cref="PackOperation"/> and, once it actually finishes, syncs either panel's
    /// attached archive lease if that lease's own local temp copy is the exact container the pack
    /// just wrote into - see <see cref="PanelViewModel.MarkArchiveLeaseDirtyIfMatches"/> for why a
    /// plain <c>Operations.RunAsync</c> call alone (what every other operation uses) isn't enough
    /// here: <c>PackOperation</c> writes through its own, separate <c>MaterializedFile</c>, whose
    /// passthrough write bypasses the container <see cref="IFileSystem"/> interface entirely for a
    /// local temp copy, so nothing already flowing through this call would ever notice.
    /// </summary>
    private async Task RunPackAndSyncLeaseAsync(IFileOperation op, string displayName, string destArchive)
    {
        try
        {
            await Operations.RunAsync(op, displayName).ConfigureAwait(true);
            if (op.State != OperationState.Completed) return;

            foreach (var panel in AllPanels)
                panel.MarkArchiveLeaseDirtyIfMatches(destArchive);
        }
        catch (Exception ex)
        {
            LogService.Error($"RunPackAndSyncLeaseAsync failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Picks the provider that can serve an arbitrary (possibly hand-typed) path, or <c>null</c>
    /// when the path names a connection that is not open.
    ///
    /// <para>Remote is tested first, for the same reason <see cref="RemotePath.IsRemote"/> documents:
    /// the classification has to be unambiguous, and a remote path must never fall through to a
    /// filesystem that would interpret it as a local one.</para>
    /// </summary>
    private IFileSystem? ResolveFileSystem(string path)
    {
        if (RemotePath.IsRemote(path))
            return Services.ConnectionManager.Instance.GetConnectedForPath(path);

        return VfsPath.IsArchive(path)
            ? ArchiveFormatRegistry.CreateFileSystem(VfsPath.GetArchiveFile(path)) ?? new ZipArchiveFileSystem(VfsPath.GetArchiveFile(path))
            : FileSystem;
    }

    /// <summary>
    /// Whether an arbitrary (possibly hand-typed) path exists, resolved through
    /// <see cref="ResolveFileSystem"/> - archive and connection paths included, not just plain
    /// local ones. Public because <c>MainForm</c>'s <c>BookmarksForm.AddBookmark</c> validation
    /// used to be a bare <c>Directory.Exists</c> call, which is never true for an
    /// "archive.zip|inner/dir" or "sftp://host/dir"-shaped path - a user could never bookmark a
    /// folder inside an archive or on a connection, the two path flavours this whole VFS layer
    /// exists for. A connection that isn't currently open resolves to no filesystem at all
    /// (<see cref="ResolveFileSystem"/> returns null) and is reported as not existing, rather than
    /// throwing - the same "can't verify, so don't accept it" call
    /// <c>MainViewModel.ExecuteTransfer</c> already makes for the same case.
    /// </summary>
    public async Task<bool> PathExistsAsync(string path)
    {
        var fs = ResolveFileSystem(path);
        return fs != null && await fs.ExistsAsync(path).ConfigureAwait(true);
    }

    /// <summary>
    /// The filesystem an archive FILE itself lives on - never the archive's own internal VFS
    /// (contrast <see cref="ResolveFileSystem"/>, which for an archive path returns the browsable
    /// tree inside it). Almost always <see cref="FileSystem"/> (the local filesystem) today, since
    /// nothing yet lets a panel browse an archive that lives on a remote connection or nested
    /// inside another archive - both are refused up front elsewhere (<c>MainForm.EnterArchiveAsync</c>).
    /// Resolving it properly here rather than assuming <see cref="FileSystem"/> is what makes
    /// <see cref="PackOperation"/>/<see cref="UnpackOperation"/> correctly reject (via their own
    /// <see cref="MaterializedFile"/> acquisition) an archive-container path that turns out not to
    /// be reachable, instead of a caller silently handing them the wrong provider.
    /// </summary>
    private IFileSystem? ResolveContainerFileSystem(string archiveFilePath)
    {
        if (RemotePath.IsRemote(archiveFilePath))
            return Services.ConnectionManager.Instance.GetConnectedForPath(archiveFilePath);
        if (VfsPath.IsArchive(archiveFilePath))
            return ResolveFileSystem(archiveFilePath);
        return FileSystem;
    }

    /// <summary>
    /// Guards against transferring a plain (non-archive) selection onto itself: the destination
    /// folder is the same as the source folder, or is a subfolder of one of the selected directories.
    /// Without this check, copying/moving with both panels on the same path (or one being a
    /// descendant of the other) tries to open the same file for read and write at once.
    /// </summary>
    private static bool IsDestinationInsideSource(string sourceBase, string destPath, IReadOnlyList<FileEntry> entries)
    {
        if (RemotePath.IsRemote(sourceBase) || RemotePath.IsRemote(destPath))
            return IsRemoteDestinationInsideSource(sourceBase, destPath, entries);

        if (VfsPath.IsArchive(sourceBase) || VfsPath.IsArchive(destPath))
            return IsArchiveDestinationInsideSource(sourceBase, destPath, entries);

        string Normalize(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string normalizedDest, normalizedSourceBase;
        try
        {
            normalizedDest = Normalize(destPath);
            normalizedSourceBase = Normalize(sourceBase);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (string.Equals(normalizedDest, normalizedSourceBase, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var entry in entries)
        {
            if (!entry.IsDirectory) continue;

            string normalizedEntry;
            try { normalizedEntry = Normalize(entry.FullPath); }
            catch (ArgumentException) { continue; }

            if (string.Equals(normalizedDest, normalizedEntry, StringComparison.OrdinalIgnoreCase) ||
                normalizedDest.StartsWith(normalizedEntry + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The same containment question for remote paths, which <see cref="Path.GetFullPath"/> cannot
    /// answer: given "dav://host/a" it does not throw, it resolves the string against the process's
    /// current directory and returns a local path - so the check above would compare two unrelated
    /// strings and cheerfully answer "not contained".
    ///
    /// <para>Comparison is case-sensitive below the root. Remote filesystems generally are, and
    /// treating "Docs" and "docs" as one directory here would refuse a transfer that is in fact
    /// between two different directories.</para>
    /// </summary>
    private static bool IsRemoteDestinationInsideSource(string sourceBase, string destPath, IReadOnlyList<FileEntry> entries)
    {
        // A local path and a remote one can never contain each other, and neither can two different
        // connections - the local-vs-remote pair is the ordinary "copy a file to the server" case.
        if (!RemotePath.IsRemote(sourceBase) || !RemotePath.IsRemote(destPath)) return false;
        if (!string.Equals(RemotePath.GetRoot(sourceBase), RemotePath.GetRoot(destPath), StringComparison.OrdinalIgnoreCase))
            return false;

        var dest = RemotePath.PathOf(destPath);
        if (string.Equals(dest, RemotePath.PathOf(sourceBase), StringComparison.Ordinal)) return true;

        foreach (var entry in entries)
        {
            if (!entry.IsDirectory) continue;

            var inner = RemotePath.PathOf(entry.FullPath);
            if (inner.Length == 0) continue;
            if (string.Equals(dest, inner, StringComparison.Ordinal) ||
                dest.StartsWith(inner + "/", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The same containment question between two archive-VFS paths (either or both sides inside a
    /// <c>.zip</c>/<c>.tar</c>/...). Before this existed, two-archive containment fell through to
    /// the local-path branch above and called <see cref="Path.GetFullPath"/> on a string containing
    /// <c>|</c> - saved from crashing only by that branch's own <c>catch (ArgumentException)</c>,
    /// which degrades to "not contained" rather than answering the question correctly. Two archives
    /// with different host files can never contain each other, matching the local/remote helpers'
    /// own "different root -> unrelated trees" rule.
    /// </summary>
    private static bool IsArchiveDestinationInsideSource(string sourceBase, string destPath, IReadOnlyList<FileEntry> entries)
    {
        if (!VfsPath.IsArchive(sourceBase) || !VfsPath.IsArchive(destPath)) return false;
        if (!string.Equals(VfsPath.GetArchiveFile(sourceBase), VfsPath.GetArchiveFile(destPath), StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(destPath, sourceBase, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var entry in entries)
        {
            if (!entry.IsDirectory) continue;

            if (string.Equals(destPath, entry.FullPath, StringComparison.OrdinalIgnoreCase) ||
                VfsPath.IsDescendantOf(entry.FullPath, destPath))
                return true;
        }

        return false;
    }

    /// <summary>Deletes selected items, optionally using the Recycle Bin.</summary>
    public void Delete()
    {
        var files = ActivePanel.GetSelectedOrActive();
        if (files.Count == 0) return;

        if (SettingsService.Load().ConfirmDelete)
        {
            // The UI layer handles confirmation dialogs
            DeleteConfirmRequested?.Invoke(this, files);
            return;
        }

        ExecuteDelete(files);
    }

    /// <summary>Queues a delete operation. Uses the Recycle Bin for local files.</summary>
    /// <param name="files">Items to delete.</param>
    public void ExecuteDelete(IReadOnlyList<Models.FileSystemItem> files)
    {
        var fs = ActivePanel.CurrentFileSystem;

        // Gate on Deletable *before* the operation starts, not via a caught NotSupportedException
        // mid-operation (F8 inside a 7z/RAR/TAR.XZ archive - the read-only formats
        // FileSystemCapabilities computes None for - used to reach DeleteOperation, fail on its
        // first entry, and surface a raw exception dialog instead of never having been reachable).
        // This is the same "capability, not a caught exception" gate FileSystemCapabilities.Writable's
        // own doc comment describes for Pack/paste/MakeDir - Delete was the one command that still
        // relied on the operation itself to notice.
        if (!fs.Capabilities.HasFlag(FileSystemCapabilities.Deletable))
        {
            OperationRejected?.Invoke(this, RemotePath.IsRemote(ActivePanel.CurrentPath)
                ? "Conn.DeleteUnsupported" : "Archive.DeleteUnsupported");
            return;
        }

        var entries = files.Select(f => f.Entry).ToList();
        // The shell Recycle Bin only understands real paths.
        // CA2000: ownership transfers to Operations.RunAsync, which disposes it on completion -
        // see the longer explanation at ExecuteTransfer's own CA2000 suppression.
#pragma warning disable CA2000
        var op = new DeleteOperation(fs, entries)
        {
            UseRecycleBin = fs.Capabilities.HasFlag(FileSystemCapabilities.RecycleBin),
            ConfirmPermanentDelete = remainingPaths =>
            {
                if (ConfirmPermanentDeleteRequested == null) return false;
                var args = new ConfirmPermanentDeleteEventArgs(remainingPaths);
                ConfirmPermanentDeleteRequested.Invoke(this, args);
                return args.Proceed;
            }
        };
        _ = Operations.RunAsync(op, Services.LocalizationService.Current.GetString("Op.DisplayDelete", files.Count));
#pragma warning restore CA2000
    }

    /// <summary>Securely wipes selected items (bypasses Recycle Bin). Not supported inside archives.</summary>
    public void Wipe()
    {
        var files = ActivePanel.GetSelectedOrActive();
        if (files.Count == 0) return;

        if (ActivePanel.IsVirtual)
        {
            OperationRejected?.Invoke(this, RemotePath.IsRemote(ActivePanel.CurrentPath)
                ? "Conn.WipeUnsupported" : "Archive.WipeUnsupported");
            return;
        }

        // Unlike Delete, always confirmed - Wipe is irreversible and (per ExecuteWipe's caveat)
        // doesn't even guarantee what it promises on an SSD, so skipping confirmation isn't offered.
        WipeConfirmRequested?.Invoke(this, files);
    }

    /// <summary>Queues a secure-wipe operation once the user has confirmed it.</summary>
    public void ExecuteWipe(IReadOnlyList<Models.FileSystemItem> files)
    {
        var entries = files.Select(f => f.Entry).ToList();
        // CA2000: ownership transfers to Operations.RunAsync - see ExecuteTransfer's suppression.
#pragma warning disable CA2000
        var op = new WipeOperation(ActivePanel.CurrentFileSystem, entries);
        _ = Operations.RunAsync(op, Services.LocalizationService.Current.GetString("Op.DisplayWipe", files.Count));
#pragma warning restore CA2000
    }

    /// <summary>
    /// Recursively computes and displays the total size of the selected directories (or the one
    /// under the cursor) in the active panel - each runs on the thread pool and updates its own
    /// <see cref="Models.FileSystemItem.CalculatedSize"/> independently, without re-reading the
    /// directory listing itself. Local filesystem only - a ZIP-backed panel has no real paths to
    /// walk with <see cref="DirectoryInfo"/>.
    /// </summary>
    public void CalculateFolderSize()
    {
        if (ActivePanel.IsVirtual)
        {
            OperationRejected?.Invoke(this, RemotePath.IsRemote(ActivePanel.CurrentPath)
                ? "Conn.CalculateSizeUnsupported" : "Archive.CalculateSizeUnsupported");
            return;
        }

        var panel = ActivePanel;
        var dirs = panel.GetSelectedOrActive().Where(i => i.IsDirectory && !i.IsParent).ToList();
        foreach (var dir in dirs)
            _ = CalculateFolderSizeAsync(panel, dir);
    }

    private static async Task CalculateFolderSizeAsync(PanelViewModel panel, Models.FileSystemItem item)
    {
        if (item.IsCalculatingSize) return;
        item.IsCalculatingSize = true;
        panel.RefreshDisplay();

        try
        {
            var size = await Task.Run(() => ComputeDirectorySize(item.FullPath));
            item.CalculatedSize = size;
        }
        catch (Exception ex)
        {
            Services.LogService.Warning($"CalculateFolderSize failed for {item.FullPath}: {ex.Message}");
        }
        finally
        {
            item.IsCalculatingSize = false;
            panel.RefreshDisplay();
        }
    }

    /// <summary>Raised once <see cref="ShowDiskInfoAsync"/> has a formatted message to show.</summary>
    public event EventHandler<string>? DiskInfoReady;

    /// <summary>
    /// Reports free/used/total space for the active panel's current location via
    /// <see cref="IFileSystem.GetDriveSpaceAsync"/> - works for a local disk the same way it works
    /// for SFTP/SMB, both of which report genuine numbers. A provider with no real concept of
    /// drive space (archives, WebDAV, FTP, MTP - see each one's own <c>GetDriveSpaceAsync</c>) is
    /// told apart by its result (<c>total &lt;= 0</c>) rather than by provider type, since guessing
    /// from <see cref="FileSystemCapabilities"/> would have to special-case exactly that same list
    /// by hand for no better an answer.
    /// </summary>
    public async Task ShowDiskInfoAsync()
    {
        var panel = ActivePanel;
        var fs = panel.CurrentFileSystem;
        var path = panel.CurrentPath;

        (long free, long total) space;
        try
        {
            space = await fs.GetDriveSpaceAsync(path);
        }
        catch (Exception ex)
        {
            LogService.Warning($"DiskInfo failed for {path}: {ex.Message}");
            space = (0, 0);
        }

        if (space.total <= 0)
        {
            OperationRejected?.Invoke(this, "DiskInfo.Unavailable");
            return;
        }

        var used = Math.Max(0, space.total - space.free);
        var percentFree = (double)space.free / space.total * 100.0;
        var message = LocalizationService.Current.GetString("DiskInfo.Message",
            path,
            FormatUtils.FormatSize(space.total),
            FormatUtils.FormatSize(used),
            FormatUtils.FormatSize(space.free),
            percentFree.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture));
        DiskInfoReady?.Invoke(this, message);
    }

    private static long ComputeDirectorySize(string path)
    {
        long total = 0;
        // ReparsePointGuard.SkipRecursion: without it a junction inside the folder being measured
        // pulls in the size of whatever it points at, inflating the reported total with bytes that
        // are not really inside the selected folder.
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | ReparsePointGuard.SkipRecursion
        };
        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                try { total += file.Length; }
                catch { /* vanished or became inaccessible mid-scan */ }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Root itself inaccessible - report whatever was tallied before the failure (0 if it
            // failed immediately) rather than throwing and leaving the item stuck showing "…".
        }
        return total;
    }
    /// <summary>Raises <see cref="MakeDirRequested"/> so the UI can prompt for a new directory name -
    /// unless the active panel's provider can't accept new content at all (a read-only archive
    /// format: 7z/RAR/TAR.XZ), in which case the request never reaches the UI and the user sees why
    /// instead of a dialog for a name that will fail to create.</summary>
    public void MakeDir()
    {
        if (!ActivePanel.CurrentFileSystem.Capabilities.HasFlag(FileSystemCapabilities.Writable))
        {
            OperationRejected?.Invoke(this, RemotePath.IsRemote(ActivePanel.CurrentPath)
                ? "Conn.MakeDirUnsupported" : "Archive.MakeDirUnsupported");
            return;
        }
        MakeDirRequested?.Invoke(this, ActivePanel.CurrentPath);
    }

    /// <summary>Raises <see cref="RenameRequested"/> for the currently selected item - gated the
    /// same way <see cref="MakeDir"/> is, since a rename is implemented as a
    /// <see cref="IFileSystem.MoveAsync"/> within the same provider and needs the same write
    /// capability a read-only archive format lacks.</summary>
    public void Rename()
    {
        if (ActivePanel.SelectedItem == null || ActivePanel.SelectedItem.IsParent) return;

        if (!ActivePanel.CurrentFileSystem.Capabilities.HasFlag(FileSystemCapabilities.Writable))
        {
            OperationRejected?.Invoke(this, RemotePath.IsRemote(ActivePanel.CurrentPath)
                ? "Conn.RenameUnsupported" : "Archive.RenameUnsupported");
            return;
        }
        RenameRequested?.Invoke(this, ActivePanel.SelectedItem);
    }

    /// <summary>Raises <see cref="ViewRequested"/> for the selected non-directory item.</summary>
    public Task ViewFileAsync()
    {
        var item = ActivePanel.SelectedItem;
        if (item == null || item.IsDirectory || item.IsParent) return Task.CompletedTask;
        ViewRequested?.Invoke(this, item);
        return Task.CompletedTask;
    }

    /// <summary>Raises <see cref="EditRequested"/> for the selected non-directory item.</summary>
    public Task EditFileAsync()
    {
        var item = ActivePanel.SelectedItem;
        if (item == null || item.IsDirectory || item.IsParent) return Task.CompletedTask;
        EditRequested?.Invoke(this, item);
        return Task.CompletedTask;
    }

    /// <summary>Raises <see cref="PropertiesRequested"/> for the selected items.</summary>
    public void ShowProperties()
    {
        var items = ActivePanel.GetSelectedOrActive();
        if (items.Count == 0) return;
        PropertiesRequested?.Invoke(this, items);
    }

    /// <summary>Raises <see cref="MultiRenameRequested"/> for the selected items.</summary>
    public void MultiRename()
    {
        var files = ActivePanel.GetSelectedOrActive();
        if (files.Count == 0) return;

        MultiRenameRequested?.Invoke(this, (files, ActivePanel.CurrentPath));
    }

    /// <summary>Navigates the active panel to the root of the current drive.</summary>
    public void GoToRoot()
    {
        _ = SafeExecuteAsync(async () =>
        {
            var path = ActivePanel.CurrentPath;
            string root;
            if (RemotePath.IsRemote(path))
                root = RemotePath.GetRoot(path);
            else if (VfsPath.IsArchive(path))
                root = VfsPath.GetArchiveFile(path);
            else
                root = Path.GetPathRoot(path) ?? "";
            if (!string.IsNullOrEmpty(root))
                await ActivePanel.NavigateAsync(root);
        }, "GoToRoot");
    }

    /// <summary>Navigates the active panel to the user's profile directory.</summary>
    public void GoToHome()
    {
        _ = SafeExecuteAsync(async () =>
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            await ActivePanel.NavigateAsync(home);
        }, "GoToHome");
    }

    /// <summary>Raises <see cref="ChangeDirRequested"/> so the UI can prompt for a path.</summary>
    public void ChangeDir()
    {
        ChangeDirRequested?.Invoke(this, ActivePanel.CurrentPath);
    }

    /// <summary>Raises <see cref="SelectGroupRequested"/> so the UI can prompt for a wildcard pattern.</summary>
    public void SelectGroup()
    {
        SelectGroupRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raises <see cref="DeselectGroupRequested"/> so the UI can prompt for a wildcard pattern.</summary>
    public void DeselectGroup()
    {
        DeselectGroupRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raises <see cref="SyncDirsRequested"/> with both panel paths.</summary>
    public void SyncDirs()
    {
        SyncDirsRequested?.Invoke(this, (LeftPanel.CurrentPath, RightPanel.CurrentPath));
    }

    /// <summary>Raises <see cref="PackRequested"/> so the UI can prompt for archive options.</summary>
    public void PackFiles()
    {
        var files = ActivePanel.GetSelectedOrActive();
        if (files.Count == 0) return;

        if (ActivePanel.IsVirtual)
        {
            OperationRejected?.Invoke(this, RemotePath.IsRemote(ActivePanel.CurrentPath)
                ? "Conn.PackUnsupported" : "Archive.PackUnsupported");
            return;
        }

        // Use the opposite panel as the place for the new archive.
        var suggestedDir = InactivePanel.IsVirtual ? ActivePanel.CurrentPath : InactivePanel.CurrentPath;
        PackRequested?.Invoke(this, (files, ActivePanel.CurrentPath, suggestedDir));
    }

    /// <summary>
    /// Checked before any <see cref="PackOperation"/> starts, whether from the explicit Pack
    /// command (<see cref="ExecutePack"/>) or from a plain copy/move whose destination panel is
    /// browsing an archive (<see cref="ExecuteTransfer"/>'s <c>intoArchive</c> branch): without
    /// this, writing into a read-only format (7z/RAR/TAR.XZ) only fails once <c>PackOperation</c>
    /// has already read the archive's directory and reaches <c>format.OpenWrite</c> mid-operation,
    /// as a raw <see cref="NotSupportedException"/> rather than a clear up-front rejection.
    /// <see cref="PackDialogForm"/> already restricts its own format picker to
    /// <see cref="ArchiveFormatRegistry.Creatable"/> for a brand-new archive, so this mainly closes
    /// the "add to an existing read-only archive" path that dialog never gates.
    /// Returns the localization key to reject with, or <c>null</c> when writing is possible.
    /// </summary>
    private static string? ValidatePackTargetFormat(string archivePath)
    {
        var format = ArchiveFormatRegistry.Detect(archivePath);
        if (format == null) return "Archive.PackTargetUnsupported";
        if (!format.Capabilities.HasFlag(ArchiveCapabilities.Create) &&
            !format.Capabilities.HasFlag(ArchiveCapabilities.AddEntries))
            return "Archive.PackTargetReadOnly";
        return null;
    }

    /// <summary>Starts a pack once the UI has settled on an archive path.</summary>
    public void ExecutePack(IReadOnlyList<Models.FileSystemItem> files, string archivePath, TransferOptions options, bool move)
    {
        if (files.Count == 0) return;

        var rejectKey = ValidatePackTargetFormat(archivePath);
        if (rejectKey != null)
        {
            OperationRejected?.Invoke(this, rejectKey);
            return;
        }

        var archiveFs = ResolveContainerFileSystem(archivePath);
        if (archiveFs is null)
        {
            OperationRejected?.Invoke(this, "Conn.NotConnected");
            return;
        }

        var entries = files.Select(f => f.Entry).ToList();
        // CA2000: ownership transfers to Operations.RunAsync - see ExecuteTransfer's suppression.
#pragma warning disable CA2000
        var op = new PackOperation(ActivePanel.CurrentFileSystem, entries, ActivePanel.CurrentPath,
            archiveFs, archivePath, "", options, removeSource: move);
        // See RunPackAndSyncLeaseAsync's own doc comment - the explicit Pack command needs the same
        // lease-sync ExecuteTransfer's intoArchive branch does, for the same reason.
        _ = RunPackAndSyncLeaseAsync(op, Services.LocalizationService.Current.GetString("Op.DisplayPack", entries.Count, Path.GetFileName(archivePath)), archivePath);
#pragma warning restore CA2000
    }

    /// <summary>Raises <see cref="SplitRequested"/> for the selected non-directory files. Same
    /// virtual-panel restriction as <see cref="PackFiles"/> - Split/Combine only ever touch a real
    /// local (or UNC) filesystem, never an archive's own VFS or a remote connection.</summary>
    public void SplitFiles()
    {
        var files = ActivePanel.GetSelectedOrActive().Where(f => !f.IsDirectory).ToList();
        if (files.Count == 0) return;

        if (ActivePanel.IsVirtual)
        {
            OperationRejected?.Invoke(this, RemotePath.IsRemote(ActivePanel.CurrentPath)
                ? "Conn.SplitUnsupported" : "Archive.SplitUnsupported");
            return;
        }

        SplitRequested?.Invoke(this, (files, ActivePanel.CurrentPath));
    }

    /// <summary>Starts a split once the UI has settled on part size/CRC/delete options.</summary>
    public void ExecuteSplit(IReadOnlyList<Models.FileSystemItem> files, string destDir, long partSizeBytes, bool writeCrc, bool deleteSource)
    {
        if (files.Count == 0 || partSizeBytes <= 0) return;

        var entries = files.Select(f => f.Entry).ToList();
        // CA2000: ownership transfers to Operations.RunAsync - see ExecuteTransfer's suppression.
#pragma warning disable CA2000
        var op = new SplitOperation(ActivePanel.CurrentFileSystem, entries, destDir, partSizeBytes, writeCrc, deleteSource);
        _ = Operations.RunAsync(op, Services.LocalizationService.Current.GetString("Op.DisplaySplit", entries.Count));
#pragma warning restore CA2000
    }

    /// <summary>Raises <see cref="CombineRequested"/> for the single selected part file. Combine
    /// always starts from one part (typically <c>.001</c>, but any part in the sequence works -
    /// <see cref="CombineOperation"/> discovers the rest); the user picks which file via the panel
    /// selection the same way every other single-target command does.</summary>
    public void CombineFiles()
    {
        var files = ActivePanel.GetSelectedOrActive().Where(f => !f.IsDirectory).ToList();
        if (files.Count != 1) return;

        if (ActivePanel.IsVirtual)
        {
            OperationRejected?.Invoke(this, RemotePath.IsRemote(ActivePanel.CurrentPath)
                ? "Conn.CombineUnsupported" : "Archive.CombineUnsupported");
            return;
        }

        CombineRequested?.Invoke(this, (files[0], ActivePanel.CurrentPath));
    }

    /// <summary>Starts a combine once the UI has settled on the output name/CRC-verify/delete
    /// options. Returns the queued operation (not yet started) so the caller can subscribe to its
    /// <see cref="IFileOperation.StateChanged"/> and read <see cref="CombineOperation.CrcVerified"/>
    /// once it completes - CRC verification has no other outward signal (a mismatch doesn't fail
    /// the operation; the combined file is already written either way).</summary>
    public CombineOperation? ExecuteCombine(string firstPartPath, string destPath, bool verifyCrc, bool deleteSource)
    {
        if (string.IsNullOrWhiteSpace(firstPartPath) || string.IsNullOrWhiteSpace(destPath)) return null;

        // CA2000: ownership transfers to Operations.RunAsync - see ExecuteTransfer's suppression.
#pragma warning disable CA2000
        var op = new CombineOperation(ActivePanel.CurrentFileSystem, firstPartPath, destPath, verifyCrc, deleteSource);
        _ = Operations.RunAsync(op, Services.LocalizationService.Current.GetString("Op.DisplayCombine", Path.GetFileName(destPath)));
#pragma warning restore CA2000
        return op;
    }

    /// <summary>Raises <see cref="UnpackRequested"/> for selected archive files.</summary>
    public void UnpackFiles()
    {
        var archives = ActivePanel.GetSelectedOrActive()
            .Where(i => !i.IsParent && !i.IsDirectory && ArchiveFormatRegistry.FromExtension(i.FullPath) != null)
            .ToList();

        if (archives.Count == 0) return;

        var suggestedDir = InactivePanel.IsVirtual ? ActivePanel.CurrentPath : InactivePanel.CurrentPath;
        UnpackRequested?.Invoke(this, (archives, suggestedDir));
    }

    /// <summary>Extracts whole archives into a folder.</summary>
    public void ExecuteUnpack(IReadOnlyList<Models.FileSystemItem> archives, string destPath, TransferOptions options)
    {
        var destFs = ResolveFileSystem(destPath);
        if (destFs is null)
        {
            // Unpacking into a connection that is no longer open. Falling through to the local
            // filesystem would resolve the remote path against the process's current directory and
            // extract the archive somewhere on disk the user never chose - the same trap the
            // copy/move path guards against.
            OperationRejected?.Invoke(this, "Conn.NotConnected");
            return;
        }

        // CA2000: ownership transfers to Operations.RunAsync - see ExecuteTransfer's suppression.
#pragma warning disable CA2000
        foreach (var archive in archives)
        {
            var archiveFs = ResolveContainerFileSystem(archive.FullPath);
            if (archiveFs is null)
            {
                OperationRejected?.Invoke(this, "Conn.NotConnected");
                continue;
            }

            var op = new UnpackOperation(archiveFs, archive.FullPath, Array.Empty<FileEntry>(), "", destFs, destPath, options)
            {
                RequestPassword = RaiseArchivePasswordRequested
            };
            _ = Operations.RunAsync(op, Services.LocalizationService.Current.GetString("Op.DisplayUnpack", archive.Name, destPath));
        }
#pragma warning restore CA2000
    }

    /// <summary>Bridges <see cref="UnpackOperation.RequestPassword"/> (a synchronous
    /// operation-level callback, invoked from a background thread) to <see cref="ArchivePasswordRequested"/>
    /// (a UI-facing event) - the same "no subscriber = safe default" shape
    /// <see cref="ExecuteDelete"/>'s own <c>ConfirmPermanentDelete</c> lambda uses for
    /// <see cref="ConfirmPermanentDeleteRequested"/>.</summary>
    private string? RaiseArchivePasswordRequested(string archivePath)
    {
        if (ArchivePasswordRequested == null) return null;
        var args = new ArchivePasswordRequestedEventArgs(archivePath);
        ArchivePasswordRequested.Invoke(this, args);
        return args.Password;
    }

    private async Task SafeExecuteAsync(Func<Task> action, string operationName)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            LogService.Error($"{operationName} failed: {ex.Message}", ex);
        }
    }

    /// <summary>Saves the selected theme and notifies listeners to re-render.</summary>
    /// <param name="theme">Theme name ("Dark" or "Light").</param>
    public void SetTheme(string theme)
    {
        var s = SettingsService.Load();
        s.Theme = theme;
        SettingsService.Save(s);
        ThemeService.ApplyTheme(theme);
        ThemeChanged?.Invoke(this, theme);
    }

    /// <summary>Toggles whether file extensions are displayed in the name column and persists the setting.</summary>
    public void ToggleShowExtensionInName()
    {
        var s = SettingsService.Load();
        s.ShowExtensionInName = !s.ShowExtensionInName;
        SettingsService.Save(s);
        ShowExtensionInNameChanged?.Invoke(this, s.ShowExtensionInName);
    }

    // ── Events for UI layer ──

    /// <summary>Raised when the user requests the application to close.</summary>
    public event EventHandler? ExitRequested;
    /// <summary>Raised when the user opens the About dialog.</summary>
    public event EventHandler? AboutRequested;
    /// <summary>Raised when a delete needs user confirmation before proceeding.</summary>
    public event EventHandler<IReadOnlyList<Models.FileSystemItem>>? DeleteConfirmRequested;
    /// <summary>Raised when a wipe operation needs user confirmation before proceeding.</summary>
    public event EventHandler<IReadOnlyList<Models.FileSystemItem>>? WipeConfirmRequested;
    /// <summary>
    /// Raised when the shell Recycle Bin failed for one or more files that still exist on disk and
    /// permanently deleting them is the only remaining option. Handler must set <see cref="ConfirmPermanentDeleteEventArgs.Proceed"/>.
    /// Invoked from a background thread - the handler is responsible for marshaling to the UI thread.
    /// </summary>
    public event EventHandler<ConfirmPermanentDeleteEventArgs>? ConfirmPermanentDeleteRequested;
    /// <summary>
    /// Raised when an unpack selection contains an entry the archive format can decrypt given a
    /// password (see <see cref="Archives.ArchiveCapabilities.PasswordProtectedRead"/>). Handler
    /// must set <see cref="ArchivePasswordRequestedEventArgs.Password"/>; leaving it null proceeds
    /// without one (encrypted entries are then skipped). Invoked from a background thread - the
    /// handler is responsible for marshaling to the UI thread, same as
    /// <see cref="ConfirmPermanentDeleteRequested"/>.
    /// </summary>
    public event EventHandler<ArchivePasswordRequestedEventArgs>? ArchivePasswordRequested;
    /// <summary>Raised when a copy operation needs user confirmation before proceeding.</summary>
    public event EventHandler<(IReadOnlyList<Models.FileSystemItem> files, string sourcePath, string destPath)>? CopyConfirmRequested;
    /// <summary>Raised when a move operation needs user confirmation before proceeding.</summary>
    public event EventHandler<(IReadOnlyList<Models.FileSystemItem> files, string sourcePath, string destPath)>? MoveConfirmRequested;
    /// <summary>Raised when a new directory name is required.</summary>
    public event EventHandler<string>? MakeDirRequested;
    /// <summary>Raised when a rename is requested for the given item.</summary>
    public event EventHandler<Models.FileSystemItem>? RenameRequested;
    /// <summary>Raised when the user wants to view a file's contents.</summary>
    public event EventHandler<Models.FileSystemItem>? ViewRequested;
    /// <summary>Raised when the user wants to edit a file.</summary>
    public event EventHandler<Models.FileSystemItem>? EditRequested;
    /// <summary>Raised when file properties should be displayed.</summary>
    public event EventHandler<IReadOnlyList<FileSystemItem>>? PropertiesRequested;
    /// <summary>Raised when multi-rename is requested for the given items.</summary>
    public event EventHandler<(IReadOnlyList<Models.FileSystemItem> files, string sourcePath)>? MultiRenameRequested;
    /// <summary>Raised when the visual theme has changed.</summary>
    public event EventHandler<string>? ThemeChanged;
    /// <summary>Raised when the "show extension in name" setting has been toggled.</summary>
    public event EventHandler<bool>? ShowExtensionInNameChanged;
    /// <summary>Raised when a new operation starts so the UI can display a progress dialog.</summary>
    public event EventHandler<(IFileOperation operation, string displayName)>? OperationStarted;
    /// <summary>Raised when the user requests navigating to a different directory by typing a path.</summary>
    public event EventHandler<string>? ChangeDirRequested;
    /// <summary>Raised when the user wants to select a group of files by pattern.</summary>
    public event EventHandler? SelectGroupRequested;
    /// <summary>Raised when the user wants to deselect a group of files by pattern.</summary>
    public event EventHandler? DeselectGroupRequested;
    /// <summary>Raised when the user wants to create a new blank file in the editor.</summary>
    public event EventHandler? EditNewRequested;
    /// <summary>Raised when the user wants to compute file checksums.</summary>
    public event EventHandler? ChecksumRequested;

    /// <summary>Raised when the user asks to search for files. The UI layer owns the dialog
    /// because the search runs against the active panel's own file system, which only it knows.</summary>
    public event EventHandler? FindFilesRequested;
    public event EventHandler? FindDuplicatesRequested;
    /// <summary>Raised when the user toggles the embedded terminal panel.</summary>
    public event EventHandler? ToggleTerminalRequested;
    /// <summary>Raised when the user toggles the active panel's quick filter box. The UI layer
    /// owns showing/hiding the actual text box (a View concern) and maps ActivePanel to the
    /// FilePanelUserControl instance that shows it - see MainForm.ToggleQuickFilter.</summary>
    public event EventHandler? QuickFilterToggleRequested;
    /// <summary>Raised when the user requests a new terminal tab with default shell settings.</summary>
    public event EventHandler? CreateTerminalTabRequested;
    /// <summary>Raised when the user requests closing the active terminal tab.</summary>
    public event EventHandler? CloseTerminalTabRequested;
    /// <summary>Raised when the user switches to the next terminal tab.</summary>
    public event EventHandler? NextTerminalTabRequested;
    /// <summary>Raised when the user switches to the previous terminal tab.</summary>
    public event EventHandler? PreviousTerminalTabRequested;
    /// <summary>Raised when the user wants to synchronise the two panel directories.</summary>
    public event EventHandler<(string leftPath, string rightPath)>? SyncDirsRequested;
    /// <summary>Raised when a pack operation needs UI input (archive path, format, compression).</summary>
    public event EventHandler<(IReadOnlyList<Models.FileSystemItem> files, string sourcePath, string destPath)>? PackRequested;

    /// <summary>Raised by <see cref="SplitFiles"/>; the view opens <c>SplitDialogForm</c> and then calls <see cref="ExecuteSplit"/>.</summary>
    public event EventHandler<(IReadOnlyList<Models.FileSystemItem> files, string destDir)>? SplitRequested;

    /// <summary>Raised by <see cref="CombineFiles"/>; the view opens <c>CombineDialogForm</c> and then calls <see cref="ExecuteCombine"/>.</summary>
    public event EventHandler<(Models.FileSystemItem firstPart, string destDir)>? CombineRequested;
    /// <summary>Raised when an unpack operation needs UI input (destination path).</summary>
    public event EventHandler<(IReadOnlyList<Models.FileSystemItem> archives, string destPath)>? UnpackRequested;

    /// <summary>Raised with a localization key when a requested transfer is not possible.</summary>
    public event EventHandler<string>? OperationRejected;

    /// <summary>Forwards <see cref="PanelViewModel.PathChanged"/> for whichever tab raised it -
    /// <c>sender</c> is the originating <see cref="PanelViewModel"/>. Subscribed once, for the
    /// view's whole lifetime, via <see cref="WirePanelEvents"/>/<see cref="UnwirePanelEvents"/>
    /// per tab; a consumer that binds directly to <c>LeftPanel</c>/<c>RightPanel</c> instead would
    /// go stale the moment a tab switch changes which instance those properties resolve to (see
    /// <see cref="LeftPanel"/>'s own doc comment) - this is the one place to subscribe to stay
    /// correct across tab switches.</summary>
    public event EventHandler? PanelPathChanged;

    // ── Internal handlers ──

    private void OnPanelPathChanged(object? sender, EventArgs e)
    {
        UpdateStatus();
        PanelPathChanged?.Invoke(sender, e);
    }

    private readonly Dictionary<PanelViewModel, string> _lastGitStatusPath = new();

    /// <summary>
    /// Kicks off a background git-status refresh exactly once per navigation, not on every
    /// ItemsChanged - RefreshDisplay() (used by this same refresh to repaint once results are in)
    /// and sort/filter changes also raise ItemsChanged for the same CurrentPath, and re-running
    /// git status for those would be both wasteful and a feedback loop.
    /// </summary>
    private void OnPanelItemsChanged(object? sender, EventArgs e)
    {
        if (sender is not PanelViewModel panel) return;
        if (_lastGitStatusPath.TryGetValue(panel, out var last) && last == panel.CurrentPath) return;
        _lastGitStatusPath[panel] = panel.CurrentPath;
        _ = RefreshGitStatusAsync(panel);
    }

    /// <summary>
    /// Computes git status for the panel's current directory on the thread pool and applies it to
    /// the already-listed items by relative path, then repaints via <see cref="PanelViewModel.RefreshDisplay"/> -
    /// mirrors <see cref="CalculateFolderSizeAsync"/>'s "background work, then update the existing
    /// items and repaint" shape. A no-op outside a local, non-archive directory, or when the
    /// directory isn't inside a git repository (or git isn't installed) - <see cref="GitStatusService.GetStatus"/>
    /// simply returns null for all of those.
    /// </summary>
    private static async Task RefreshGitStatusAsync(PanelViewModel panel)
    {
        try
        {
            if (!panel.CurrentFileSystem.Capabilities.HasFlag(FileSystemCapabilities.GitStatus))
                return;

            var path = panel.CurrentPath;
            var snapshot = await Task.Run(() => GitStatusService.GetStatus(path));

            if (panel.CurrentPath != path)
                return; // navigated away while this was computing

            foreach (var item in panel.Items)
                item.GitStatus = item.IsParent || snapshot == null
                    ? GitFileStatus.None
                    : snapshot.Resolve(item.FullPath, item.IsDirectory);

            panel.RefreshDisplay();
        }
        catch (Exception ex)
        {
            LogService.Error($"RefreshGitStatusAsync failed: {ex.Message}", ex);
        }
    }

    private void OnPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // FreeSpaceDisplay belongs here as much as the rest: it is filled in by an asynchronous
        // probe that finishes well after the listing, so the status bar is always built before the
        // number exists. Without this the bar keeps showing the previous drive's free space after
        // switching drives - the panel's own footer updates, the bar below it does not, and the two
        // disagree on screen.
        if (e.PropertyName is nameof(PanelViewModel.SelectedCount) or nameof(PanelViewModel.SelectedBytes)
            or nameof(PanelViewModel.CursorInfo) or nameof(PanelViewModel.FreeSpaceDisplay))
            UpdateStatus();

        // Only the ACTIVE panel's own selection drives Quick View in the other panel - a
        // background tab's cursor moving (e.g. a FileSystemWatcher-triggered re-sync) is not
        // something the user is "browsing" right now.
        if (e.PropertyName == nameof(PanelViewModel.SelectedItem) && ReferenceEquals(sender, ActivePanel))
            ActiveSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnOperationChanged(object? sender, OperationManagerEventArgs e)
    {
        // OperationManager fires this from a thread-pool thread (FileOperation.ExecuteAsync uses
        // ConfigureAwait(false)). The entire handler touches ObservableProperty setters (which raise
        // PropertyChanged for WinForms bindings), reads panel state, and invokes OperationStarted
        // (whose subscriber creates and shows dialogs) — all of which must run on the UI thread.
        if (_uiContext is not null)
        {
            _uiContext.Post(_ => OnOperationChangedCore(e), null);
        }
        else
        {
            OnOperationChangedCore(e);
        }
    }

    private volatile bool _disposed;

    private void OnOperationChangedCore(OperationManagerEventArgs e)
    {
        if (_disposed) return;
        var active = Operations.Operations.Count;
        OperationQueueText = active > 0 ? Services.LocalizationService.Current.GetString("Main.OperationsActive", active) : "";
        UpdateStatus();

        if (e.Change == OperationChangeType.Started)
            OperationStarted?.Invoke(this, (e.Operation.Operation, e.Operation.DisplayName));

        // Refresh panels when an operation completes.
        if (e.Change is OperationChangeType.Completed or OperationChangeType.Canceled or OperationChangeType.Failed)
        {
            _ = LeftPanel.RefreshAsync();
            _ = RightPanel.RefreshAsync();
        }
    }

    /// <summary>Refreshes the status bar text with the active panel's cursor info, selection and free space.</summary>
    public void UpdateStatus()
    {
        var panel = ActivePanel;
        var L = LocalizationService.Current;
        var text = $"{panel.CursorInfo}  |  {L.GetString("Panel.Selected", panel.SelectedCount)}  ({FormatUtils.FormatSize(panel.SelectedBytes)})";

        // Only when there is a number to show. A connection reports no free space - the protocols
        // either have no such notion or make it optional - and appending the separator regardless
        // left a bar ending in a lone "|" with nothing after it.
        if (panel.FreeSpaceDisplay.Length > 0)
            text += $"  |  {panel.FreeSpaceDisplay}";

        StatusText = text;
    }

    /// <summary>Unsubscribes event handlers and disposes every tab on both sides and the
    /// operation manager. <see cref="PanelTabSet.Dispose"/> itself raises <see cref="PanelTabSet.TabRemoving"/>
    /// for each tab before disposing it, which is what actually unwinds the per-panel
    /// <see cref="WirePanelEvents"/> subscriptions - no separate unwiring loop needed here.</summary>
    public void Dispose()
    {
        _disposed = true;
        Operations.OperationChanged -= OnOperationChanged;
        _leftTabs.Dispose();
        _rightTabs.Dispose();
        Operations.Dispose();
    }
}

/// <summary>Event args for <see cref="MainViewModel.ConfirmPermanentDeleteRequested"/>.</summary>
public sealed class ConfirmPermanentDeleteEventArgs(IReadOnlyList<string> paths) : EventArgs
{
    /// <summary>Full paths of files that could not be recycled and require permanent deletion.</summary>
    public IReadOnlyList<string> Paths { get; } = paths;
    /// <summary>Set to <c>true</c> by the handler to allow permanent deletion.</summary>
    public bool Proceed { get; set; }
}

/// <summary>Event args for <see cref="MainViewModel.ArchivePasswordRequested"/>.</summary>
public sealed class ArchivePasswordRequestedEventArgs(string archivePath) : EventArgs
{
    /// <summary>Full path of the archive whose entries need a password to decrypt.</summary>
    public string ArchivePath { get; } = archivePath;
    /// <summary>Set by the handler to the entered password, or left null to proceed without one.</summary>
    public string? Password { get; set; }
}
