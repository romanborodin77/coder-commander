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
        LeftPanel.ItemsChanged += OnPanelItemsChanged;
        RightPanel.ItemsChanged += OnPanelItemsChanged;

        // Wire operation manager events
        Operations.OperationChanged += OnOperationChanged;

        RegisterCommands();
        Hotkeys.Reload(SettingsService.Load().CustomHotkeys);
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
        Commands.Register(CommandIds.RefreshDrives, p => { _ = SafeExecuteAsync(() => DriveCatalog.Instance.RefreshAsync(), "RefreshDrives"); });
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
        Commands.Register(CommandIds.CalculateFolderSize, _ => CalculateFolderSize());
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
        Commands.Register(CommandIds.FindFiles, _ => FindFilesRequested?.Invoke(this, EventArgs.Empty));
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
                unpackDestFs, destPath, options, removeSource: move);
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
        await Operations.RunAsync(op, displayName).ConfigureAwait(true);
        if (op.State != OperationState.Completed) return;

        LeftPanel.MarkArchiveLeaseDirtyIfMatches(destArchive);
        RightPanel.MarkArchiveLeaseDirtyIfMatches(destArchive);
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

            var op = new UnpackOperation(archiveFs, archive.FullPath, Array.Empty<FileEntry>(), "", destFs, destPath, options);
            _ = Operations.RunAsync(op, Services.LocalizationService.Current.GetString("Op.DisplayUnpack", archive.Name, destPath));
        }
#pragma warning restore CA2000
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
        var text = $"{panel.CursorInfo}  |  {L.GetString("Panel.Selected", panel.SelectedCount)}  ({FormatUtils.FormatSize(panel.SelectedBytes)})";

        // Only when there is a number to show. A connection reports no free space - the protocols
        // either have no such notion or make it optional - and appending the separator regardless
        // left a bar ending in a lone "|" with nothing after it.
        if (panel.FreeSpaceDisplay.Length > 0)
            text += $"  |  {panel.FreeSpaceDisplay}";

        StatusText = text;
    }

    /// <summary>Unsubscribes event handlers and disposes both panels and the operation manager.</summary>
    public void Dispose()
    {
        Operations.OperationChanged -= OnOperationChanged;
        LeftPanel.PathChanged -= OnPanelPathChanged;
        RightPanel.PathChanged -= OnPanelPathChanged;
        LeftPanel.PropertyChanged -= OnPanelPropertyChanged;
        RightPanel.PropertyChanged -= OnPanelPropertyChanged;
        LeftPanel.ItemsChanged -= OnPanelItemsChanged;
        RightPanel.ItemsChanged -= OnPanelItemsChanged;
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
