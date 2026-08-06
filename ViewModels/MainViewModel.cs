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

    /// <summary>Left file panel ViewModel.</summary>
    public PanelViewModel LeftPanel { get; }

    /// <summary>Right file panel ViewModel.</summary>
    public PanelViewModel RightPanel { get; }

    /// <summary>Currently focused panel (left or right).</summary>
    [ObservableProperty] private PanelViewModel _activePanel;

    /// <summary>Text shown in the main status bar (cursor info, selection, free space).</summary>
    [ObservableProperty] private string _statusText = "";

    /// <summary>Non-empty when one or more background operations are queued.</summary>
    [ObservableProperty] private string _operationQueueText = "";

    /// <summary>The panel that is <em>not</em> currently focused — used as the transfer destination.</summary>
    public PanelViewModel InactivePanel => ActivePanel == LeftPanel ? RightPanel : LeftPanel;

    /// <summary>Initialises the file system, operation manager, command engine and both panels.</summary>
    public MainViewModel()
    {
        FileSystem = new LocalFileSystem();
        Operations = new OperationManager();
        Commands = new CommandEngine();
        Hotkeys = new HotkeyManager(Commands);

        LeftPanel = new PanelViewModel(FileSystem);
        RightPanel = new PanelViewModel(FileSystem);
        ActivePanel = LeftPanel;

        LeftPanel.IsActive = true;
        RightPanel.IsActive = false;

        // Wire panel activation
        LeftPanel.PathChanged += OnPanelPathChanged;
        RightPanel.PathChanged += OnPanelPathChanged;
        LeftPanel.PropertyChanged += OnPanelPropertyChanged;
        RightPanel.PropertyChanged += OnPanelPropertyChanged;

        // Wire operation manager events
        Operations.OperationChanged += OnOperationChanged;

        RegisterCommands();
        Hotkeys.RegisterDefaults();
    }

    // ── Panel management ──

    /// <summary>Makes <paramref name="panel"/> the active panel, deactivating the other.</summary>
    public void SetActivePanel(PanelViewModel panel)
    {
        if (ActivePanel == panel) return;
        ActivePanel.IsActive = false;
        ActivePanel = panel;
        ActivePanel.IsActive = true;
        UpdateStatus();
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
        Commands.Register(CommandIds.GoToParent, p => { _ = SafeExecuteAsync(() => ActivePanel.GoToParentAsync(), "GoToParent"); });
        Commands.Register(CommandIds.Refresh, p => { _ = SafeExecuteAsync(() => ActivePanel.RefreshAsync(), "Refresh"); });
        Commands.Register(CommandIds.SelectAll, _ => ActivePanel.SelectAll());
        Commands.Register(CommandIds.DeselectAll, _ => ActivePanel.DeselectAll());
        Commands.Register(CommandIds.InvertSelection, _ => ActivePanel.InvertSelection());
        Commands.Register(CommandIds.SwapPanels, _ => SwapPanels());
        Commands.Register(CommandIds.TargetEqualSource, _ => TargetEqualSource());
        Commands.Register(CommandIds.ToggleHidden, _ => ActivePanel.ShowHidden = !ActivePanel.ShowHidden);
        Commands.Register(CommandIds.ToggleFlatView, _ => ActivePanel.IsFlatView = !ActivePanel.IsFlatView);
        Commands.Register(CommandIds.View, p => { _ = SafeExecuteAsync(() => ViewFileAsync(), "View"); });
        Commands.Register(CommandIds.Edit, p => { _ = SafeExecuteAsync(() => EditFileAsync(), "Edit"); });
        Commands.Register(CommandIds.SetTheme, param => SetTheme(param ?? "Dark"));
        Commands.Register(CommandIds.Exit, _ => ExitRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.About, _ => AboutRequested?.Invoke(this, EventArgs.Empty));
        Commands.Register(CommandIds.ShowProperties, _ => ShowProperties());
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
        var verb = move ? "Move" : "Copy";

        if (fromArchive && intoArchive &&
            string.Equals(sourceArchive, destArchive, StringComparison.OrdinalIgnoreCase))
        {
            OperationRejected?.Invoke(this, "Archive.SameArchiveTransfer");
            return;
        }

        IFileOperation op;

        if (intoArchive)
        {
            op = new PackOperation(sourceFs, entries, sourceBase, destArchive,
                VfsPath.GetInner(destPath), options, removeSource: move);
        }
        else if (fromArchive)
        {
            op = new UnpackOperation(sourceArchive, entries, VfsPath.GetInner(sourceBase),
                ResolveFileSystem(destPath), destPath, options, removeSource: move);
        }
        else
        {
            if (IsDestinationInsideSource(sourceBase, destPath, entries))
            {
                OperationRejected?.Invoke(this, "Transfer.SourceEqualsDestination");
                return;
            }

            var destFs = ResolveFileSystem(destPath);
            op = move
                ? new MoveOperation(sourceFs, destFs, entries, sourceBase, destPath, options)
                : new CopyOperation(sourceFs, destFs, entries, sourceBase, destPath, options);
        }

        _ = Operations.RunAsync(op, $"{verb} {entries.Count} item(s) to {destPath}");
    }

    /// <summary>Picks the provider that can serve an arbitrary (possibly hand-typed) path.</summary>
    private IFileSystem ResolveFileSystem(string path) =>
        VfsPath.IsArchive(path)
            ? ArchiveFormatRegistry.CreateFileSystem(VfsPath.GetArchiveFile(path)) ?? new ZipArchiveFileSystem(VfsPath.GetArchiveFile(path))
            : FileSystem;

    /// <summary>
    /// Guards against transferring a plain (non-archive) selection onto itself: the destination
    /// folder is the same as the source folder, or is a subfolder of one of the selected directories.
    /// Without this check, copying/moving with both panels on the same path (or one being a
    /// descendant of the other) tries to open the same file for read and write at once.
    /// </summary>
    private static bool IsDestinationInsideSource(string sourceBase, string destPath, IReadOnlyList<FileEntry> entries)
    {
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
        var entries = files.Select(f => f.Entry).ToList();
        // The shell Recycle Bin only understands real paths.
        var op = new DeleteOperation(fs, entries)
        {
            UseRecycleBin = fs is LocalFileSystem,
            ConfirmPermanentDelete = remainingPaths =>
            {
                if (ConfirmPermanentDeleteRequested == null) return false;
                var args = new ConfirmPermanentDeleteEventArgs(remainingPaths);
                ConfirmPermanentDeleteRequested.Invoke(this, args);
                return args.Proceed;
            }
        };
        _ = Operations.RunAsync(op, Services.LocalizationService.Current.GetString("Op.DisplayDelete", files.Count));
    }

    /// <summary>Securely wipes selected items (bypasses Recycle Bin). Not supported inside archives.</summary>
    public void Wipe()
    {
        var files = ActivePanel.GetSelectedOrActive();
        if (files.Count == 0) return;

        if (ActivePanel.IsInsideArchive)
        {
            OperationRejected?.Invoke(this, "Archive.WipeUnsupported");
            return;
        }

        var entries = files.Select(f => f.Entry).ToList();
        var op = new WipeOperation(ActivePanel.CurrentFileSystem, entries);
        _ = Operations.RunAsync(op, Services.LocalizationService.Current.GetString("Op.DisplayWipe", files.Count));
    }
    /// <summary>Raises <see cref="MakeDirRequested"/> so the UI can prompt for a new directory name.</summary>
    public void MakeDir()
    {
        MakeDirRequested?.Invoke(this, ActivePanel.CurrentPath);
    }

    /// <summary>Raises <see cref="RenameRequested"/> for the currently selected item.</summary>
    public void Rename()
    {
        if (ActivePanel.SelectedItem != null && !ActivePanel.SelectedItem.IsParent)
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
            var root = Path.GetPathRoot(ActivePanel.CurrentPath);
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

        if (ActivePanel.IsInsideArchive)
        {
            OperationRejected?.Invoke(this, "Archive.SameArchiveTransfer");
            return;
        }

        // Use the opposite panel as the place for the new archive.
        var suggestedDir = InactivePanel.IsInsideArchive ? ActivePanel.CurrentPath : InactivePanel.CurrentPath;
        PackRequested?.Invoke(this, (files, ActivePanel.CurrentPath, suggestedDir));
    }

    /// <summary>Starts a pack once the UI has settled on an archive path.</summary>
    public void ExecutePack(IReadOnlyList<Models.FileSystemItem> files, string archivePath, TransferOptions options, bool move)
    {
        if (files.Count == 0) return;
        var entries = files.Select(f => f.Entry).ToList();
        var op = new PackOperation(ActivePanel.CurrentFileSystem, entries, ActivePanel.CurrentPath,
            archivePath, "", options, removeSource: move);
        _ = Operations.RunAsync(op, Services.LocalizationService.Current.GetString("Op.DisplayPack", entries.Count, Path.GetFileName(archivePath)));
    }

    /// <summary>Raises <see cref="UnpackRequested"/> for selected archive files.</summary>
    public void UnpackFiles()
    {
        var archives = ActivePanel.GetSelectedOrActive()
            .Where(i => !i.IsParent && !i.IsDirectory && ArchiveFormatRegistry.FromExtension(i.FullPath) != null)
            .ToList();

        if (archives.Count == 0) return;

        var suggestedDir = InactivePanel.IsInsideArchive ? ActivePanel.CurrentPath : InactivePanel.CurrentPath;
        UnpackRequested?.Invoke(this, (archives, suggestedDir));
    }

    /// <summary>Extracts whole archives into a folder.</summary>
    public void ExecuteUnpack(IReadOnlyList<Models.FileSystemItem> archives, string destPath, TransferOptions options)
    {
        var destFs = ResolveFileSystem(destPath);
        foreach (var archive in archives)
        {
            var op = new UnpackOperation(archive.FullPath, Array.Empty<FileEntry>(), "", destFs, destPath, options);
            _ = Operations.RunAsync(op, Services.LocalizationService.Current.GetString("Op.DisplayUnpack", archive.Name, destPath));
        }
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
    /// <summary>
    /// Raised when the shell Recycle Bin failed for one or more files that still exist on disk and
    /// permanently deleting them is the only remaining option. Handler must set <see cref="ConfirmPermanentDeleteEventArgs.Proceed"/>.
    /// Invoked from a background thread - the handler is responsible for marshaling to the UI thread.
    /// </summary>
    public event EventHandler<ConfirmPermanentDeleteEventArgs>? ConfirmPermanentDeleteRequested;
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
    /// <summary>Raised when the user toggles the embedded terminal panel.</summary>
    public event EventHandler? ToggleTerminalRequested;
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
    /// <summary>Raised when an unpack operation needs UI input (destination path).</summary>
    public event EventHandler<(IReadOnlyList<Models.FileSystemItem> archives, string destPath)>? UnpackRequested;

    /// <summary>Raised with a localization key when a requested transfer is not possible.</summary>
    public event EventHandler<string>? OperationRejected;

    // ── Internal handlers ──

    private void OnPanelPathChanged(object? sender, EventArgs e)
    {
        UpdateStatus();
    }

    private void OnPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PanelViewModel.SelectedCount) or nameof(PanelViewModel.SelectedBytes) or nameof(PanelViewModel.CursorInfo))
            UpdateStatus();
    }

    private void OnOperationChanged(object? sender, OperationManagerEventArgs e)
    {
        var active = Operations.Operations.Count;
        OperationQueueText = active > 0 ? Services.LocalizationService.Current.GetString("Main.OperationsActive", active) : "";
        UpdateStatus();

        if (e.Change == OperationChangeType.Started)
            OperationStarted?.Invoke(this, (e.Operation.Operation, e.Operation.DisplayName));

        // Refresh panels when an operation completes
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
        StatusText = $"{panel.CursorInfo}  |  {L.GetString("Panel.Selected", panel.SelectedCount)}  ({FormatUtils.FormatSize(panel.SelectedBytes)})  |  {panel.FreeSpaceDisplay}";
    }

    /// <summary>Unsubscribes event handlers and disposes both panels and the operation manager.</summary>
    public void Dispose()
    {
        Operations.OperationChanged -= OnOperationChanged;
        LeftPanel.PathChanged -= OnPanelPathChanged;
        RightPanel.PathChanged -= OnPanelPathChanged;
        LeftPanel.PropertyChanged -= OnPanelPropertyChanged;
        RightPanel.PropertyChanged -= OnPanelPropertyChanged;
        LeftPanel.Dispose();
        RightPanel.Dispose();
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
