using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.ViewModels;
using CoderCommander.Viewers;
using CoderCommander.WinForms;
using CoderCommander.WinForms.Viewers;
using System.Drawing.Drawing2D;

namespace CoderCommander.Views;

/// <summary>
/// In-process drag payload. Unlike <see cref="DataFormats.FileDrop"/> it can describe entries that
/// live inside an archive and it remembers which panel the drag started from.
/// </summary>
public sealed class PanelDragPayload
{
    public const string Format = "CoderCommander.PanelItems";

    public FilePanelUserControl Source { get; }
    public IReadOnlyList<FileSystemItem> Items { get; }

    public PanelDragPayload(FilePanelUserControl source, IReadOnlyList<FileSystemItem> items)
    {
        Source = source;
        Items = items;
    }
}

/// <summary>Describes what was dropped on a panel and where.</summary>
public sealed class PanelDropEventArgs : EventArgs
{
    /// <summary>Panel the items came from; null for drops originating outside the application.</summary>
    public FilePanelUserControl? SourcePanel { get; init; }

    /// <summary>Items that were dropped.</summary>
    public IReadOnlyList<FileSystemItem> Items { get; init; } = [];

    /// <summary>Shell paths of an external drop.</summary>
    public IReadOnlyList<string> ExternalPaths { get; init; } = [];

    /// <summary>Folder that receives the items — the panel directory or the folder under the cursor.</summary>
    public string Destination { get; init; } = "";

    /// <summary><c>true</c> for a copy, <c>false</c> for a move (Alt key held).</summary>
    public bool IsCopy { get; init; }
}

/// <summary>
/// A single file manager panel: path bar, file list, status bar, context menu.
/// </summary>
public sealed class FilePanelUserControl : UserControl
{
    // Not readonly - RebindViewModel() re-points an already-constructed control at a different
    // PanelViewModel (Ф3, panel tabs: one FilePanelUserControl per side, re-bound per tab rather
    // than one control per tab).
    private PanelViewModel _vm;

    private Panel _borderPanel = null!;
    private TextBox _pathBar = null!;
    private FlowLayoutPanel _breadcrumbBar = null!;
    private ListView _fileList = null!;
    private Panel _filterBar = null!;
    private Label _filterLabel = null!;
    private TextBox _filterBox = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _lblCursor = null!;
    private ToolStripStatusLabel _lblSelected = null!;
    private ToolStripStatusLabel _lblFree = null!;
    private ImageList _fileImageList = null!;
    private ToolStrip _driveBar = null!;
    private ListViewScrollbarOverlay? _scrollOverlay;

    // Panel tab strip (Ф3) - one FilePanelUserControl per side hosts N tabs, so this strip only
    // ever renders buttons and reports clicks; it has no PanelTabSet reference of its own (that
    // lives in MainViewModel), so MainForm is what turns TabSelected/TabCloseRequested/
    // NewTabRequested into real PanelTabSet calls and feeds titles back in via SetTabs().
    private ThemedTabControl _tabStrip = null!;
    // Every ThemedTabPage.Content must be non-null, but this strip (OwnsPageContent=false) never
    // actually shows a page's content - the real content (drive bar, file list, etc. below) is
    // this control's own layout, unrelated to which tab is selected. All pages share this one
    // never-shown Panel instead of each allocating (and never disposing) their own.
    private Panel _tabStripDummyContent = null!;
    private RoundedButton _addTabButton = null!;
    private readonly ToolTip _addTabTooltip = new();

    // Quick View (Ф4, Ctrl+Q) - previews whatever is selected in the ACTIVE panel, shown in the
    // OTHER (inactive) panel in place of its own file list. Lazy by construction: nothing here
    // exists until SetQuickView(true), and the host itself isn't built until the first file
    // actually worth previewing comes in (RefreshQuickViewPreview may be called many times with
    // nothing to show - a directory, an MTP path, an oversized file).
    private ViewerHostControl? _quickViewHost;
    private IFileSystem? _quickViewHostFs;
    private System.Windows.Forms.Timer? _quickViewDebounce;
    private string? _quickViewLastPath;
    private FileSystemItem? _pendingQuickViewItem;
    private IFileSystem? _pendingQuickViewFs;
    private bool _quickViewRemoteEnabled;
    private const int QuickViewDebounceMs = 300;

    /// <summary>Whether this panel is currently showing a Quick View preview instead of its own
    /// file list.</summary>
    public bool IsQuickViewActive { get; private set; }

    private readonly List<ToolStripButton> _driveButtons = new();

    private bool _suppressSelectionEvent;
    private bool _updatingItems;
    // Index-based, not a ListViewItem reference: in VirtualMode a ListViewItem returned from
    // HitTest/RetrieveVirtualItem is a transient wrapper, not a stable identity - two calls for the
    // same logical row are not guaranteed to return the same object, so reference comparisons that
    // worked before virtualization (audit finding G047) silently stop matching.
    private int? _hoveredIndex;
    private bool _showExtensionInName = true;

    // Windowed ListViewItem cache for VirtualMode (RetrieveVirtualItem/CacheVirtualItems) - the
    // standard pattern from the ListView.VirtualMode documentation. Invalidated (set to null)
    // whenever the underlying model list changes, so a stale cached row is never served after a
    // refresh, sort, or navigation.
    private ListViewItem[]? _virtualCache;
    private int _virtualCacheStart;

    /// <summary>The ViewModel that provides data and commands for this panel.</summary>
    public PanelViewModel ViewModel => _vm;
    /// <summary>The underlying <see cref="ListView"/> that renders file items.</summary>
    public ListView FileList => _fileList;

    /// <summary>Raised when the panel requests activation (got focus).</summary>
    public event EventHandler? PanelActivated;

    /// <summary>Raised when an item is activated (double-click / Enter).</summary>
    public event EventHandler<FileSystemItem>? ItemActivated;

    /// <summary>Raised when Edit is requested from context menu.</summary>
    public event EventHandler<FileSystemItem?>? EditRequested;

    /// <summary>Raised when View is requested from context menu - distinct from
    /// <see cref="ItemActivated"/> (Enter/double-click, which falls through to ShellExecute for a
    /// non-archive file): this must resolve through the VFS-aware F3 viewer the same way
    /// <c>MainViewModel.ViewFileAsync</c> does, so "View" from the right-click menu works on a file
    /// inside an archive or on a connection exactly like F3 does, not just on local disk.</summary>
    public event EventHandler<FileSystemItem?>? ViewRequested;

    /// <summary>Raised when Copy is requested from context menu.</summary>
    public event EventHandler? CopyRequested;

    /// <summary>Raised when Move is requested from context menu.</summary>
    public event EventHandler? MoveRequested;

    /// <summary>Raised when Rename is requested from context menu.</summary>
    public event EventHandler? RenameRequested;

    /// <summary>Raised when Delete is requested from context menu.</summary>
    public event EventHandler? DeleteRequested;

    /// <summary>Raised when Properties is requested from context menu.</summary>
    public event EventHandler? PropertiesRequested;

    /// <summary>Raised from the background (empty-space) context menu's "New Folder…" item.</summary>
    public event EventHandler? MakeDirRequested;

    /// <summary>Raised from the background context menu's "New File…" item.</summary>
    public event EventHandler? NewFileRequested;

    /// <summary>Raised from the background context menu's "Refresh" item.</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>Raised from the background context menu's "Properties" item - distinct from
    /// <see cref="PropertiesRequested"/>, which acts on the item selection: this one has no
    /// selection to act on (empty space was right-clicked) and targets the panel's own current
    /// directory instead.</summary>
    public event EventHandler? FolderPropertiesRequested;

    /// <summary>Raised from the context menu's "Verify checksums" item, only shown when the
    /// selected item's extension is one of the formats <see cref="Services.ChecksumService"/> can
    /// export/parse (<c>.sfv</c>/<c>.md5</c>/<c>.sha1</c>/<c>.sha256</c>).</summary>
    public event EventHandler<FileSystemItem>? VerifyChecksumRequested;

    /// <summary>Raised from the context menu's "Create Link ▸ Symbolic Link…" item, only shown for
    /// a native-path filesystem.</summary>
    public event EventHandler<FileSystemItem>? CreateSymlinkRequested;

    /// <summary>Raised from the context menu's "Create Link ▸ Hard Link…" item, only shown for a
    /// native-path filesystem. The handler still has to validate same-volume/file-not-directory -
    /// unlike the extension check gating "Verify checksums…", nothing here can cheaply confirm
    /// those without touching the disk.</summary>
    public event EventHandler<FileSystemItem>? CreateHardlinkRequested;

    /// <summary>Raised from the context menu's "Open With…" item, only shown for a native-path,
    /// non-directory item - same restriction Explorer's own "Open with" applies.</summary>
    public event EventHandler<FileSystemItem>? OpenWithRequested;

    /// <summary>Raised from the item menu's "Open in Explorer" - carries the real Windows paths
    /// (already resolved via <see cref="ShellPathOf"/>), not <see cref="FileSystemItem"/>, since
    /// that's all <see cref="WinForms.Shell.ExplorerHelper"/> needs and it keeps this event usable
    /// from the background menu too (where there is no item, only a folder path).</summary>
    public event EventHandler<IReadOnlyList<string>>? OpenInExplorerRequested;

    /// <summary>Raised from the item menu's "Windows Properties" - single target only (Phase 5
    /// upgrades this to the real multi-item property sheet via the <c>IContextMenu</c> host).</summary>
    public event EventHandler<string>? ShellPropertiesRequested;

    /// <summary>Raised from the item menu's "Copy to clipboard" (or Ctrl+C) - puts the current
    /// selection on the system clipboard as a real shell file-drop.</summary>
    public event EventHandler? ClipboardCopyRequested;

    /// <summary>Raised from the item menu's "Cut to clipboard" (or Ctrl+X) - same as
    /// <see cref="ClipboardCopyRequested"/>, marked as a move.</summary>
    public event EventHandler? ClipboardCutRequested;

    /// <summary>Raised from the background menu's "Paste" (or Ctrl+V).</summary>
    public event EventHandler? ClipboardPasteRequested;

    /// <summary>Raised from either context menu's "Open terminal here" - carries the target
    /// folder's real Windows path (a single directory target's own path in the item menu, the
    /// panel's current directory in the background menu or when the target isn't a folder).</summary>
    public event EventHandler<string>? OpenTerminalHereRequested;

    /// <summary>Raised when "Split into parts..." is requested from the context menu.</summary>
    public event EventHandler? SplitRequested;

    /// <summary>Raised when "Combine from parts..." is requested from the context menu.</summary>
    public event EventHandler? CombineRequested;

    /// <summary>Raised when files are dropped onto this panel via drag &amp; drop.</summary>
    public event EventHandler<PanelDropEventArgs>? ItemsDropped;

    /// <summary>Raised when the user tries to enter a recognized archive file (double-click or Enter).</summary>
    public event EventHandler<FileSystemItem>? ArchiveEntered;

    /// <summary>Raised when a connection button in the places bar is clicked. Carries the
    /// profile id; MainForm decides whether that means connect, enter, or retry, because only
    /// it can swap the panel's file system and show a dialog.</summary>
    public event EventHandler<Guid>? ConnectionActivated;

    /// <summary>Raised when the "Network" button in the drive bar is clicked. MainForm opens
    /// <see cref="NetworkBrowseForm"/> and handles navigation.</summary>
    public event EventHandler? NetworkBrowseRequested;

    /// <summary>Raised when an MTP device button is clicked. EventArgs = device ID.</summary>
    public event EventHandler<string>? MtpDeviceActivated;

    /// <summary>Raised when the user clicks a tab button to switch tabs. EventArgs = tab index.
    /// This control does not switch anything itself - MainForm decides what "tab index N" means
    /// (via <c>MainViewModel.SetActiveTabIndex</c>) and feeds the result back through
    /// <see cref="RebindViewModel"/>/<see cref="SetTabs"/>.</summary>
    public event EventHandler<int>? TabSelected;

    /// <summary>Raised when the user clicks a tab's close ("x") button. EventArgs = tab index.</summary>
    public event EventHandler<int>? TabCloseRequested;

    /// <summary>Raised when the user clicks the "+" button to open a new tab on this side.</summary>
    public event EventHandler? NewTabRequested;

    /// <summary>
    /// Whether what this panel is showing lives at real paths on this machine.
    ///
    /// Everything that hands a path to something outside the app - the shell's FileDrop format,
    /// opening a file as an archive, ShellExecute - is only meaningful when this is true. A
    /// <c>dav://host/x.zip</c> string given to any of them is not merely useless: the archive
    /// reader would try to open it as a file, and ShellExecute would look for a handler registered
    /// for the "dav" URL scheme.
    /// </summary>
    private bool HasNativePaths => _vm.CurrentFileSystem.Capabilities.HasFlag(FileSystemCapabilities.NativePaths);

    /// <summary>The real Windows path for <paramref name="item"/>, or null when this panel's
    /// filesystem has none (archive entry, or FTP/SFTP/WebDAV/MTP connection) - what every
    /// shell-integration context menu item (Open in Explorer, clipboard Copy/Cut, the system
    /// menu) gates on. Unlike <see cref="HasNativePaths"/>, this is shown for SMB.</summary>
    private string? ShellPathOf(FileSystemItem item) => _vm.CurrentFileSystem.GetShellPath(item.FullPath);

    /// <summary>The real Windows path of the folder this panel is currently showing, or null when
    /// there isn't one - what the background (empty-space) context menu's shell items target,
    /// since there is no <see cref="FileSystemItem"/> under the cursor there.</summary>
    private string? CurrentShellFolder => _vm.CurrentFileSystem.GetShellPath(_vm.CurrentPath);

    /// <summary>Creates the panel UI and wires up the ViewModel, context menu and drag-and-drop.</summary>
    /// <param name="vm">ViewModel providing data for this panel.</param>
    /// <param name="automationIdPrefix">
    /// Identifies this instance for UI automation - e.g. <c>"LeftPanel"</c>/<c>"RightPanel"</c>,
    /// giving <see cref="_fileList"/> the stable <c>AutomationId</c> <c>"LeftPanel.FileList"</c>
    /// (WinForms' UIA bridge reads <c>AutomationId</c> straight from <see cref="Control.Name"/>, so
    /// no custom automation provider is needed). Before this, a test could only tell the two
    /// panels' file lists apart by comparing on-screen X position - fragile by construction, and
    /// exactly the workaround <c>TabSwitchesActivePanelTests</c>'s own doc comment already flagged.
    /// </param>
    public FilePanelUserControl(PanelViewModel vm, string automationIdPrefix)
    {
        _vm = vm;
        Name = automationIdPrefix;
        _showExtensionInName = SettingsService.Load().ShowExtensionInName;
        Dock = DockStyle.Fill;
        BackColor = ThemeService.Current.PanelBackground;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        DoubleBuffered = true;

        BuildControls();
        WireViewModel();
        ApplyTheme();

        LocalizationService.Current.LanguageChanged += OnLanguageChanged;

        // The drive bar used to be rebuilt only from ApplyTheme(), i.e. at construction and on a
        // theme switch - so a drive plugged in afterwards never appeared. Subscribing here is what
        // makes the bar track reality; MainForm's DeviceChangeWatcher triggers the refresh.
        DriveCatalog.Instance.Changed += OnDrivesChanged;
        // Connections share the same strip, so they share its refresh path - one handler,
        // one rebuild, no second mechanism to keep in step.
        ConnectionManager.Instance.Changed += OnDrivesChanged;
        // MTP devices appear in the drive bar too, but MtpDeviceCatalog has its own change event
        // (not routed through DriveCatalog or ConnectionManager). Without this, a newly connected
        // MTP device's button only appeared after restarting the app.
        MtpDeviceCatalog.Instance.Changed += OnDrivesChanged;
        _ = DriveCatalog.Instance.RefreshAsync();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Relocalize();

    /// <summary>
    /// Redraws the drive bar once this control can actually be marshalled to.
    ///
    /// The first <see cref="DriveCatalog.RefreshAsync"/> is kicked off in the constructor and may
    /// well finish before the handle exists, in which case <see cref="OnDrivesChanged"/> has no
    /// choice but to drop the notification. Without this the bar would then stay empty until some
    /// unrelated event (a theme switch) rebuilt it - exactly the class of bug this whole change
    /// is fixing.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        PopulateDriveBar();

        // See _pendingRebuild's own doc comment - the file-list equivalent of the drive-bar gap
        // this method already exists to close.
        if (_pendingRebuild)
        {
            _pendingRebuild = false;
            RebuildList();
        }
    }

    /// <summary>
    /// <see cref="DriveCatalog.Changed"/> fires on a thread-pool thread, so this marshals before
    /// touching any control. BeginInvoke, never Invoke: a synchronous call from the catalog's
    /// probe continuation into a UI thread that is itself inside a catalog call would deadlock.
    /// </summary>
    private void OnDrivesChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                PopulateDriveBar();
            }));
        }
        catch (ObjectDisposedException)
        {
            // The panel went away between the check above and the marshal - nothing to update.
        }
    }

    private void BuildControls()
    {
        var p = ThemeService.Current;

        // Border panel (shows active/inactive state)
        _borderPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(2),
            BackColor = p.FocusBorder
        };

        // Inner content
        var content = new Panel { Dock = DockStyle.Fill, BackColor = p.PanelBackground };

        // Path bar — kept for direct path typing/pasting (BeginEditPath), hidden by
        // default in favor of the clickable breadcrumb bar below.
        _pathBar = new TextBox
        {
            Dock = DockStyle.Top,
            Visible = false,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = p.GridFont,
            BackColor = p.HeaderBackground,
            ForeColor = p.HeaderForeground,
            Height = 28,
            Padding = new Padding(8, 5, 8, 5),
            Cursor = Cursors.Hand
        };
        _pathBar.Click += (_, _) => ActivatePanel();
        _pathBar.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                CommitOrCancelPathEdit(true);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                CommitOrCancelPathEdit(false);
            }
        };
        _pathBar.Leave += (_, _) =>
        {
            if (!_pathBar.ReadOnly) CommitOrCancelPathEdit(false);
        };

        // Breadcrumb bar — clickable path segments, shown instead of the raw text box.
        _breadcrumbBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = p.HeaderBackground,
            Padding = new Padding(8, 0, 4, 0),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Cursor = Cursors.IBeam
        };
        _breadcrumbBar.Click += (_, _) => BeginEditPath();

        // Tab strip (Ф3) — sits above the breadcrumb/drive bar, one FilePanelUserControl per side.
        // OwnsPageContent=false: this strip exists purely for its button row and never hosts real
        // content of its own (see _tabStripDummyContent's own doc comment) - RebindViewModel, not
        // this control, is what actually changes what the rest of the panel shows.
        _tabStripDummyContent = new Panel { Visible = false };
        _tabStrip = new ThemedTabControl
        {
            Dock = DockStyle.Top,
            Height = 34,
            OwnsPageContent = false,
            CloseButtonTooltip = LocalizationService.Current.GetString("Panel.Tab.Close"),
        };
        _tabStrip.SelectedIndexChanged += (_, _) => TabSelected?.Invoke(this, _tabStrip.SelectedIndex);
        _tabStrip.TabCloseClicked += (_, idx) => TabCloseRequested?.Invoke(this, idx);

        // A drawn "+" rather than the "+" character - mirrors EmbeddedTerminalPanel's own new-tab
        // button exactly (same size/role/icon), just scoped to this panel's tabs instead of the
        // terminal's.
        _addTabButton = new RoundedButton
        {
            Width = 30,
            Height = 30,
            Image = ToolbarIcons.Get("plus"),
            Cursor = Cursors.Hand,
            Margin = new Padding(6, 1, 0, 1),
            CornerRadius = 4,
            UseGradient = false,
            DrawShadow = false,
            TabStop = false,
            AccessibleName = LocalizationService.Current.GetString("Panel.Tab.New"),
            AccessibleRole = AccessibleRole.PushButton,
            Name = $"{Name}.NewTabButton",
        };
        _addTabButton.Role = ThemeRole.ToolbarButton;
        _addTabButton.Click += (_, _) => NewTabRequested?.Invoke(this, EventArgs.Empty);
        _addTabTooltip.SetToolTip(_addTabButton, LocalizationService.Current.GetString("Panel.Tab.New"));
        _tabStrip.SetTrailingControl(_addTabButton);

        // Drive bar — ToolStrip with rounded drive buttons
        var toolbarScale = GetToolbarScale();
        var iconSize = (int)Math.Round(16 * toolbarScale);
        _driveBar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            AutoSize = false,
            Height = (int)Math.Round(38 * toolbarScale),
            BackColor = p.HeaderBackground,
            ForeColor = p.HeaderForeground,
            Renderer = new DriveBarRenderer(),
            Padding = new Padding(6, 3, 6, 3),
            ImageScalingSize = new Size(iconSize, iconSize),
            Font = p.StatusBarFont
        };

        // File list
        _fileList = new ListView
        {
            Name = $"{Name}.FileList",
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = true,
            GridLines = false,
            BorderStyle = BorderStyle.None,
            OwnerDraw = true,
            Font = p.GridFont,
            BackColor = p.PanelBackground,
            ForeColor = p.Foreground,
            HeaderStyle = ColumnHeaderStyle.Clickable,
            // VirtualMode (audit finding G047): a folder with hundreds of thousands of entries
            // used to materialize one ListViewItem + 5 SubItems for every single row on every
            // refresh, including FileSystemWatcher-triggered ones - multi-second UI freezes and
            // millions of objects for a large listing. In VirtualMode the ListView only ever asks
            // for the rows it's actually about to draw (RetrieveVirtualItem/CacheVirtualItems
            // below), regardless of how many entries the model holds.
            VirtualMode = true
        };
        _fileList.HandleCreated += (_, _) => NativeControlThemer.ThemeListView(_fileList);
        _fileList.SelectedIndexChanged += OnSelectedIndexChanged;
        _fileList.DoubleClick += OnItemDoubleClick;
        _fileList.KeyDown += OnFileListKeyDown;
        _fileList.MouseClick += OnFileListMouseClick;
        _fileList.GotFocus += (_, _) => ActivatePanel();
        _fileList.MouseDown += OnFileListMouseDown;
        _fileList.MouseMove += OnFileListMouseMove;
        _fileList.MouseLeave += OnFileListMouseLeave;
        _fileList.ItemDrag += OnFileListItemDrag;
        _fileList.DrawColumnHeader += OnDrawColumnHeader;
        _fileList.ColumnClick += OnFileListColumnClick;
        _fileList.DrawItem += OnDrawItem;
        _fileList.DrawSubItem += OnDrawSubItem;
        _fileList.Paint += OnFileListPaint;
        _fileList.Resize += OnFileListResize;
        _fileList.RetrieveVirtualItem += OnRetrieveVirtualItem;
        _fileList.CacheVirtualItems += OnCacheVirtualItems;
        _fileList.SearchForVirtualItem += OnSearchForVirtualItem;
        _fileList.AllowDrop = true;
        _fileList.DragEnter += OnFileListDragEnter;
        _fileList.DragOver += OnFileListDragOver;
        _fileList.DragDrop += OnFileListDragDrop;
        _fileList.DragLeave += OnFileListDragLeave;

        // File type icons
        _fileImageList = new ImageList
        {
            ColorDepth = ColorDepth.Depth32Bit,
            ImageSize = new Size(16, 16)
        };
        _fileList.SmallImageList = _fileImageList;
        PopulateFileImageList();

        // Columns (localized) — tuned widths
        var L = LocalizationService.Current;
        _fileList.Columns.Add(L.GetString("Panel.Name"), 280);
        _fileList.Columns.Add(L.GetString("Panel.Ext"), 55);
        _fileList.Columns.Add(L.GetString("Panel.Size"), 85, HorizontalAlignment.Right);
        _fileList.Columns.Add(L.GetString("Panel.Modified"), 135);
        _fileList.Columns.Add(L.GetString("Panel.Attributes"), 45);
        FillLastColumnWidth();

        // Status strip — compact with better padding
        _statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            SizingGrip = false,
            BackColor = p.HeaderBackground,
            Padding = new Padding(6, 2, 6, 2)
        };
        _lblCursor = new ToolStripStatusLabel
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = p.DimForeground
        };
        _lblSelected = new ToolStripStatusLabel
        {
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = p.Accent,
            Margin = new Padding(8, 0, 8, 0)
        };
        _lblFree = new ToolStripStatusLabel
        {
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = p.DimForeground
        };
        _statusStrip.Items.AddRange([_lblCursor, _lblSelected, _lblFree]);

        // Quick filter bar (Ctrl+F) - hidden by default. PanelViewModel.Filter/ApplyFilter already
        // existed and worked (matching DisplayName in Flat View, debounced via _filterDebounce);
        // this bar was the only missing piece - the model layer had no UI entry point at all.
        _filterBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Visible = false,
            Height = 30,
            BackColor = p.HeaderBackground,
            Padding = new Padding(8, 3, 8, 3)
        };
        _filterBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = p.GridFont,
            BackColor = p.HeaderBackground,
            ForeColor = p.HeaderForeground
        };
        _filterBox.TextChanged += (_, _) => _vm.Filter = _filterBox.Text;
        _filterBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            HideQuickFilter();
        };
        _filterLabel = new Label
        {
            Dock = DockStyle.Left,
            AutoSize = false,
            Width = 50,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = p.GridFont,
            ForeColor = p.DimForeground,
            Text = L.GetString("Panel.Filter")
        };
        _filterBar.Controls.Add(_filterBox);
        _filterBar.Controls.Add(_filterLabel);

        content.Controls.Add(_fileList);
        content.Controls.Add(_driveBar);
        content.Controls.Add(_pathBar);
        content.Controls.Add(_breadcrumbBar);
        // Added last among the Top-docked controls, so it lands visually topmost (WinForms lays
        // out docked children from the last-added Controls index down to the first) - a tab strip
        // belongs above the breadcrumb/drive bar, not between them.
        content.Controls.Add(_tabStrip);
        content.Controls.Add(_filterBar);
        content.Controls.Add(_statusStrip);
        _borderPanel.Controls.Add(content);
        Controls.Add(_borderPanel);

        // Create scrollbar overlay after the ListView has a parent
        _fileList.HandleCreated += (_, _) =>
        {
            if (_scrollOverlay == null && _fileList.Parent != null)
            {
                _scrollOverlay = new ListViewScrollbarOverlay(_fileList);
                // Toggling a native scrollbar changes ClientSize without reliably raising
                // Resize, so the last column can be sized against a stale width unless it
                // re-fits whenever the overlay notices the native footprint actually changed.
                _scrollOverlay.NativeMetricsChanged += (_, _) => FillLastColumnWidth();
            }
        };
    }

    private void WireViewModel()
    {
        _vm.ItemsChanged += OnItemsChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;
        UpdatePathDisplay();
    }

    private void UnwireViewModel()
    {
        _vm.ItemsChanged -= OnItemsChanged;
        _vm.PropertyChanged -= OnVmPropertyChanged;
    }

    /// <summary>
    /// Re-points this already-constructed control at a different <see cref="PanelViewModel"/>
    /// instance, without recreating any WinForms control - used when switching which tab is active
    /// on this side (Ф3, panel tabs), so the file list/drive bar/breadcrumb/status strip are never
    /// torn down and rebuilt, just repainted from a different model. A no-op if <paramref name="newVm"/>
    /// is already the bound instance.
    ///
    /// <para><b>Ordering is deliberate and must not be reshuffled.</b> This class already carries a
    /// live crash history from exactly this class of bug - <see cref="SyncSelectionFromVm"/>'s own
    /// doc comment documents a reproduced <see cref="ArgumentOutOfRangeException"/> from
    /// <c>_fileList.VirtualListSize</c> lagging <c>_vm.Items.Count</c> by one UI-thread turn - and a
    /// rebind is the same hazard with a much bigger delta: every cached row, every selection index,
    /// the drive bar, the breadcrumb are all keyed off the OLD vm and have to be invalidated before
    /// anything reads the NEW one. Order: <see cref="_updatingItems"/> = true (blocks
    /// <see cref="SyncSelectionFromVm"/>/<see cref="SyncAllSelectionFromModel"/>, the two methods
    /// that class of bug lives in, from touching the ListView mid-transition) → drop the
    /// virtualized row cache and zero <c>VirtualListSize</c> (nothing left referencing the old
    /// model's indices) → unsubscribe from the old vm → swap the field → subscribe to the new vm
    /// (also re-reads the path/breadcrumb) → sync the quick-filter box/bar from the new vm's own
    /// <see cref="PanelViewModel.Filter"/> (never force-cleared: a tab switched away from mid-filter
    /// and back to must show its own filter again, not lose it to whichever tab was active most
    /// recently) → refresh drive bar/active-state chrome → <see cref="RebuildList"/> (which sets
    /// <see cref="_updatingItems"/> back to false itself once the new model's rows/selection are
    /// fully applied) → status bar.
    /// </para>
    /// </summary>
    public void RebindViewModel(PanelViewModel newVm)
    {
        if (ReferenceEquals(newVm, _vm)) return;

        _updatingItems = true;
        _virtualCache = null;
        _hoveredIndex = null;
        _fileList.SelectedIndices.Clear();
        _fileList.VirtualListSize = 0;

        UnwireViewModel();
        _vm = newVm;
        WireViewModel();

        _filterBox.Text = _vm.Filter;
        _filterBar.Visible = _vm.Filter.Length > 0;

        PopulateDriveBar();
        UpdateDriveBarHighlight();
        ApplyActiveState();

        RebuildList();
        UpdateStatus();
    }

    /// <summary>Rebuilds the tab strip from <paramref name="titles"/>, highlighting
    /// <paramref name="activeIndex"/>. Called by MainForm whenever this side's <c>PanelTabSet</c>
    /// changes shape (add/close/switch) or any of its tabs navigates (title = current folder name) -
    /// this control has no reference to <c>PanelTabSet</c> itself, only whichever
    /// <see cref="PanelViewModel"/> is currently bound, so it can't derive this on its own. The
    /// close ("x") glyph is only shown once there's more than one tab - closing the sole tab on a
    /// side is always a no-op downstream (<c>PanelTabSet.CloseTab</c>), so a clickable close button
    /// that can never do anything would just be a dead end.</summary>
    public void SetTabs(IReadOnlyList<string> titles, int activeIndex)
    {
        _tabStrip.ShowCloseButtons = titles.Count > 1;
        _tabStrip.ClearPages();
        foreach (var title in titles)
            _tabStrip.AddPage(new ThemedTabPage(title, _tabStripDummyContent));
        if (activeIndex >= 0 && activeIndex < titles.Count)
            _tabStrip.SelectedIndex = activeIndex;
    }

    // ── Quick View (Ф4, Ctrl+Q) ──────────────────────────────────────────────────────────────

    /// <summary>Turns Quick View on/off for this panel. On: hides this panel's own file list (the
    /// panel keeps its own path/drive bar/breadcrumb/tab strip - only its content area changes;
    /// see <see cref="RefreshQuickViewPreview"/> for whose file gets shown there and why). Off:
    /// tears the preview host down - disposed, not just hidden, so its WebView2 process (if the
    /// last-shown format needed one) doesn't sit idle - and restores the file list.</summary>
    public void SetQuickView(bool enabled)
    {
        if (enabled == IsQuickViewActive) return;
        IsQuickViewActive = enabled;

        if (enabled)
        {
            _quickViewRemoteEnabled = SettingsService.Load().QuickViewRemoteEnabled;
            _quickViewLastPath = null;
            _fileList.Visible = false;
        }
        else
        {
            _quickViewDebounce?.Stop();
            _quickViewLastPath = null;
            DisposeQuickViewHost();
            _fileList.Visible = true;
        }
    }

    /// <summary>Called by MainForm whenever the ACTIVE panel's selection might have changed
    /// (<c>MainViewModel.ActiveSelectionChanged</c>) - a no-op unless this panel currently has
    /// Quick View on. This panel previews whatever is selected in the ACTIVE panel, not its own
    /// selection: Quick View replaces this panel's own file list, so it has nothing of its own
    /// left to browse with arrow keys - the active panel is what the user is actually browsing,
    /// exactly the way Total Commander's own Quick View works.</summary>
    public void RefreshQuickViewPreview(FileSystemItem? item, IFileSystem sourceFs)
    {
        if (!IsQuickViewActive) return;

        _pendingQuickViewItem = item;
        _pendingQuickViewFs = sourceFs;
        _quickViewDebounce ??= CreateQuickViewDebounceTimer();
        _quickViewDebounce.Stop();
        _quickViewDebounce.Start();
    }

    private System.Windows.Forms.Timer CreateQuickViewDebounceTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = QuickViewDebounceMs };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ApplyQuickViewPreview(_pendingQuickViewItem, _pendingQuickViewFs);
        };
        return timer;
    }

    /// <summary>Applies (or refuses) a preview after the debounce settles. Every refusal check is
    /// answered from data the panel already has (capability flags, the listing's own cached
    /// <see cref="FileSystemItem.Size"/>) - never by attempting a read first and catching failure,
    /// so a folder full of oversized/remote files costs nothing to arrow through.</summary>
    private void ApplyQuickViewPreview(FileSystemItem? item, IFileSystem? sourceFs)
    {
        if (!IsQuickViewActive || sourceFs == null) return;

        var refused = item is null || item.IsDirectory || item.IsParent
            || item.Size > ViewerLimits.QuickViewMaxBytes
            || MtpConnectionRegistry.IsMtpFileSystem(sourceFs)
            || (!_quickViewRemoteEnabled && ConnectionManager.Instance.IsConnectionFileSystem(sourceFs));

        var newPath = !refused && item is not null ? item.FullPath : null;
        if (newPath == _quickViewLastPath) return; // unchanged - e.g. a FileSystemWatcher tick
        _quickViewLastPath = newPath;

        if (newPath == null)
        {
            DisposeQuickViewHost();
            return;
        }

        if (_quickViewHost == null || !ReferenceEquals(_quickViewHostFs, sourceFs))
        {
            DisposeQuickViewHost();
            _quickViewHost = new ViewerHostControl(sourceFs, newPath, null, 0, SettingsService.Load())
            {
                Dock = DockStyle.Fill,
                CompactMode = true,
            };
            _quickViewHostFs = sourceFs;
            _fileList.Parent!.Controls.Add(_quickViewHost);
        }
        else
        {
            _quickViewHost.LoadPath(newPath);
        }

        _ = _quickViewHost.LoadCurrentAsync();
    }

    private void DisposeQuickViewHost()
    {
        if (_quickViewHost == null) return;
        _quickViewHost.Parent?.Controls.Remove(_quickViewHost);
        _quickViewHost.Dispose();
        _quickViewHost = null;
        _quickViewHostFs = null;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!IsHandleCreated) return;

        if (e.PropertyName == nameof(PanelViewModel.CurrentPath))
        {
            BeginInvoke(() =>
            {
                UpdatePathDisplay();
                UpdateDriveBarHighlight();
            });
        }
        else if (e.PropertyName == nameof(PanelViewModel.SelectedItem))
        {
            SyncSelectionFromVm();
        }
        else if (e.PropertyName == nameof(PanelViewModel.SelectedCount))
        {
            BeginInvoke(SyncAllSelectionFromModel);
        }
        else if (e.PropertyName == nameof(PanelViewModel.IsActive))
        {
            BeginInvoke(ApplyActiveState);
        }
    }

    // Set when OnItemsChanged fires before the handle exists (same startup race OnHandleCreated's
    // own doc comment already describes for the drive bar - the initial NavigateAsync at startup
    // can complete, and raise ItemsChanged, before this control's handle has been created,
    // especially for whichever of the two docked panels finishes layout second). Without this, that
    // very first listing is silently dropped and the panel sits empty until some unrelated event
    // (a manual Refresh, a theme switch) happens to trigger a rebuild - reproduced consistently for
    // the panel that isn't SetActivePanel'd at startup (audit finding, Ф4 virtualization pass).
    private bool _pendingRebuild;

    private void OnItemsChanged(object? sender, EventArgs e)
    {
        if (!IsHandleCreated) { _pendingRebuild = true; return; }
        BeginInvoke(RebuildList);
    }

    // -- File image list --

    private void PopulateFileImageList()
    {
        _fileImageList.Images.Clear();
        foreach (FileIconType type in Enum.GetValues(typeof(FileIconType)))
        {
            _fileImageList.Images.Add(type.ToString(), FileIcons.Get(type));
        }
    }

    private static string GetFileIconKey(FileSystemItem item)
    {
        if (item.IsParent) return FileIconType.ParentFolder.ToString();
        if (item.IsDirectory) return FileIconType.Folder.ToString();
        return FileIcons.GetIconType(item.Extension).ToString();
    }

    // -- List building --

    /// <summary>Builds the (fully populated but otherwise disconnected) row for one model item -
    /// the single place both <see cref="OnRetrieveVirtualItem"/> and <see cref="OnCacheVirtualItems"/>
    /// go to construct a row on demand. Never sets <see cref="ListViewItem.Selected"/>/<see cref="ListViewItem.Focused"/>
    /// - in <see cref="ListView.VirtualMode"/> those are controlled by the native control's own
    /// per-index state (<see cref="ListView.SelectedIndices"/>/<see cref="ListView.FocusedItem"/>),
    /// not by whatever a transient row object happens to carry.</summary>
    private ListViewItem BuildListViewItem(FileSystemItem item)
    {
        var displayName = item.IsParent
            ? (item.DisplayName ?? item.Name)
            : (_showExtensionInName ? (item.DisplayName ?? item.Name) : (item.DisplayName ?? item.NameWithoutExtension));

        var lvi = new ListViewItem(displayName)
        {
            Tag = item,
            UseItemStyleForSubItems = false,
            ImageKey = GetFileIconKey(item)
        };

        lvi.SubItems.Add(item.TypeDisplay);
        lvi.SubItems.Add(item.SizeDisplay);
        lvi.SubItems.Add(item.ModifiedDisplay);
        lvi.SubItems.Add(item.AttributesDisplay);
        return lvi;
    }

    /// <summary>VirtualMode callback: the ListView needs the row at <see cref="RetrieveVirtualItemEventArgs.ItemIndex"/>
    /// to draw or hit-test it. Serves from the small windowed cache <see cref="OnCacheVirtualItems"/>
    /// maintains when possible, otherwise builds it directly (a cache miss - e.g. a jump via
    /// Home/End/Ctrl+A on a huge list - is still correct, just not pre-warmed).</summary>
    private void OnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (_virtualCache != null && e.ItemIndex >= _virtualCacheStart && e.ItemIndex < _virtualCacheStart + _virtualCache.Length)
        {
            e.Item = _virtualCache[e.ItemIndex - _virtualCacheStart];
            return;
        }

        e.Item = e.ItemIndex >= 0 && e.ItemIndex < _vm.Items.Count
            ? BuildListViewItem(_vm.Items[e.ItemIndex])
            : new ListViewItem();
    }

    /// <summary>VirtualMode callback: the ListView is about to need rows in [StartIndex, EndIndex]
    /// (typically the visible viewport plus a small margin) - pre-build them once instead of one
    /// RetrieveVirtualItem call at a time, and drop anything outside that window so the cache stays
    /// bounded regardless of how large the underlying list is.</summary>
    private void OnCacheVirtualItems(object? sender, CacheVirtualItemsEventArgs e)
    {
        if (_virtualCache != null && e.StartIndex >= _virtualCacheStart && e.EndIndex <= _virtualCacheStart + _virtualCache.Length - 1)
            return; // requested range is already a subset of what's cached

        _virtualCacheStart = e.StartIndex;
        var length = e.EndIndex - e.StartIndex + 1;
        var cache = new ListViewItem[length];
        for (var i = 0; i < length; i++)
        {
            var modelIndex = e.StartIndex + i;
            if (modelIndex < _vm.Items.Count)
                cache[i] = BuildListViewItem(_vm.Items[modelIndex]);
        }
        _virtualCache = cache;
    }

    /// <summary>VirtualMode callback backing native keyboard type-ahead ("type a letter to jump to
    /// a file starting with it") - SysListView32 sends LVN_ODFINDITEM for an owner-data list
    /// exactly the way a non-virtual ListView resolves the same keystrokes against its own Items
    /// collection internally. Without handling this, VirtualMode silently drops type-ahead
    /// entirely (FindItemWithText/FindNearestItem return null per the VirtualMode.SearchForVirtualItem
    /// documentation) - a real behavior regression from virtualizing this list, not merely a
    /// missing optimization.</summary>
    private void OnSearchForVirtualItem(object? sender, SearchForVirtualItemEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;

        var items = _vm.Items;
        var count = items.Count;
        if (count == 0) return;

        var start = Math.Clamp(e.StartIndex, 0, count - 1);
        for (var offset = 0; offset < count; offset++)
        {
            var idx = (start + offset) % count;
            var name = items[idx].DisplayName ?? items[idx].Name;
            if (name.StartsWith(e.Text, StringComparison.OrdinalIgnoreCase))
            {
                e.Index = idx;
                return;
            }
        }
    }

    private void RebuildList()
    {
        _updatingItems = true;
        _suppressSelectionEvent = true;

        var selItem = _vm.SelectedItem;
        var topIndex = _fileList.TopItem?.Index ?? 0;

        // The model changed - any previously cached row would carry a stale display string or
        // even reference a FileSystemItem the panel no longer shows.
        _virtualCache = null;

        int? focusIndex = null;

        _fileList.BeginUpdate();
        try
        {
            _fileList.VirtualListSize = _vm.Items.Count;

            // Restore selection state from the model - VirtualMode tracks selection by index
            // natively (SelectedIndices), not via any per-row .Selected a retrieved item carries,
            // and setting VirtualListSize resets it, so this has to be rebuilt explicitly every
            // time, the same as the old Items.Clear()+Add() rebuild implicitly required.
            _fileList.SelectedIndices.Clear();
            var items = _vm.Items;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.IsSelected)
                    _fileList.SelectedIndices.Add(i);

                // Restore focus - ReferenceEquals instead of Name comparison to handle FlatView,
                // where items from different directories can share the same name.
                if (selItem != null && ReferenceEquals(item, selItem))
                    focusIndex = i;
            }

            if (focusIndex is { } fi)
            {
                _fileList.FocusedItem = _fileList.Items[fi];
                if (!_fileList.SelectedIndices.Contains(fi))
                    _fileList.SelectedIndices.Add(fi);
            }

            // Restore scroll position so a FileSystemWatcher-triggered refresh doesn't jump to top.
            if (topIndex > 0 && topIndex < _fileList.Items.Count)
                _fileList.TopItem = _fileList.Items[topIndex];
        }
        finally
        {
            _fileList.EndUpdate();
        }

        // Ensure the focused item is visible after the list rebuild.
        if (focusIndex is { } visIndex)
            _fileList.EnsureVisible(visIndex);

        _suppressSelectionEvent = false;
        _updatingItems = false;
        UpdateStatus();
    }

    private void SyncSelectionFromVm()
    {
        if (_suppressSelectionEvent || _updatingItems) return;
        var target = _vm.SelectedItem;
        if (target == null) return;

        var items = _vm.Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (!ReferenceEquals(items[i], target)) continue;

            // _fileList.VirtualListSize can still be the OLD (smaller) count here: OnItemsChanged
            // defers RebuildList via BeginInvoke, so a SelectedItem change that lands synchronously
            // right after an Items change (PanelViewModel.RefreshAsync does exactly this) can be
            // observed before the queued rebuild has actually run. SelectedIndices.Add(i) with i
            // past the ListView's own (still stale) VirtualListSize throws ArgumentOutOfRangeException
            // (audit finding: reproduced live, D:\...zip|... Refresh crash, ArgumentOutOfRangeException
            // at SyncSelectionFromVm). Safe to just skip: RebuildList's own selection-restore pass
            // (keyed off item.IsSelected/ReferenceEquals to the same SelectedItem) fully re-syncs
            // selection and focus every time it runs, including this case, once the pending
            // BeginInvoke actually fires - moments later, same UI thread.
            if (i >= _fileList.VirtualListSize) return;

            _suppressSelectionEvent = true;
            if (!_fileList.SelectedIndices.Contains(i))
                _fileList.SelectedIndices.Add(i);
            _fileList.FocusedItem = _fileList.Items[i];
            _fileList.EnsureVisible(i);
            _suppressSelectionEvent = false;
            break;
        }
    }

    /// <summary>
    /// Re-syncs the ListView's native selection state from item.IsSelected for every row. Needed
    /// after bulk selection changes (SelectAll/DeselectAll/InvertSelection/pattern select) which
    /// mutate the model directly without going through the ListView's own selection events.
    /// </summary>
    private void SyncAllSelectionFromModel()
    {
        if (_updatingItems) return;
        _suppressSelectionEvent = true;
        _fileList.BeginUpdate();
        try
        {
            _fileList.SelectedIndices.Clear();
            var items = _vm.Items;
            // Bounded by the ListView's own (possibly still-stale) VirtualListSize, not items.Count -
            // see SyncSelectionFromVm's identical guard for why: a pending RebuildList (queued via
            // BeginInvoke off ItemsChanged) can leave VirtualListSize behind _vm.Items for one UI-thread
            // turn. Entries past it are reconciled once that rebuild runs, which independently re-derives
            // SelectedIndices from IsSelected the same way this loop does.
            var bound = Math.Min(items.Count, _fileList.VirtualListSize);
            for (var i = 0; i < bound; i++)
            {
                if (items[i].IsSelected)
                    _fileList.SelectedIndices.Add(i);
            }
        }
        finally
        {
            _fileList.EndUpdate();
        }
        _suppressSelectionEvent = false;
        UpdateStatus();
    }

    // -- Event handlers --

    private void OnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressSelectionEvent || _updatingItems) return;

        // Sync ListView selection state back to model. WinForms fires this event multiple
        // times during a Shift+Click range select — each call iterates all items, but we only
        // update IsSelected on items whose state actually changed (avoids spurious PropertyChanged
        // notifications that trigger O(n) RecomputeSelectionStats each time). SelectedItems/Items
        // aren't available in VirtualMode (InvalidOperationException) - SelectedIndices (a native,
        // Win32-backed index set) is, and is what every VirtualMode ListView sample uses for this.
        var selectedIndices = new HashSet<int>();
        foreach (int idx in _fileList.SelectedIndices)
            selectedIndices.Add(idx);

        var items = _vm.Items;
        for (var i = 0; i < items.Count; i++)
        {
            var shouldSelect = selectedIndices.Contains(i);
            if (items[i].IsSelected != shouldSelect)
                items[i].IsSelected = shouldSelect;
        }
        _vm.NotifySelectionChanged();

        // Update cursor item
        var focused = _fileList.FocusedItem;
        if (selectedIndices.Count > 0 && focused != null)
        {
            if (focused.Tag is FileSystemItem item)
            {
                _vm.SelectedItem = item;
            }
        }
        else if (selectedIndices.Count == 0 && focused == null)
        {
            _vm.SelectedItem = null;
        }

        UpdateStatus();
    }

    private void OnItemDoubleClick(object? sender, EventArgs e)
    {
        if (_fileList.FocusedItem?.Tag is FileSystemItem item)
        {
            if (item.IsParent)
            {
                _ = _vm.GoToParentAsync();
            }
            else if (item.IsDirectory)
            {
                _ = _vm.NavigateAsync(item.FullPath);
            }
            else if (CanEnterAsArchive(item.FullPath))
            {
                ArchiveEntered?.Invoke(this, item);
            }
            else
            {
                ItemActivated?.Invoke(this, item);
            }
        }
    }

    /// <summary>
    /// Whether Enter/double-click on this item should try to browse it as an archive.
    ///
    /// <para>Not just <see cref="HasNativePaths"/> - a recognized archive file sitting directly on
    /// a connection (FTP/SFTP/WebDAV) is enterable too, materialized to a local temp copy first
    /// (see <c>MainForm.EnterArchiveAsync</c>). A nested archive - one already living inside
    /// another archive - stays refused with zero extra logic here: once inside ANY archive
    /// (materialized or not), the panel's own filesystem never declares <see cref="FileSystemCapabilities.NativePaths"/>
    /// and every item's path is the local temp/real path <c>ArchivePath</c> built, which is never
    /// <see cref="FileSystem.RemotePath.IsRemote"/> either - so both halves of this condition stay
    /// false for a nested item exactly the way <see cref="HasNativePaths"/> alone already did before
    /// remote archives existed.</para>
    /// </summary>
    private bool CanEnterAsArchive(string path) =>
        (HasNativePaths || FileSystem.RemotePath.IsRemote(path)) && ArchiveFormatRegistry.FromExtension(path) != null;

    private void OnFileListKeyDown(object? sender, KeyEventArgs e)
    {
        // Space = toggle selection
        if (e.KeyCode == Keys.Space && _fileList.FocusedItem?.Tag is FileSystemItem item && !item.IsParent)
        {
            item.IsSelected = !item.IsSelected;
            _vm.NotifySelectionChanged();
            RefreshItemColors();
            e.Handled = true;
            e.SuppressKeyPress = true;
            UpdateStatus();
        }

        // Enter = activate item
        if (e.KeyCode == Keys.Enter && _fileList.FocusedItem?.Tag is FileSystemItem enterItem)
        {
            if (enterItem.IsParent)
                _ = _vm.GoToParentAsync();
            else if (enterItem.IsDirectory)
                _ = _vm.NavigateAsync(enterItem.FullPath);
            else if (CanEnterAsArchive(enterItem.FullPath))
                ArchiveEntered?.Invoke(this, enterItem);
            else
                ItemActivated?.Invoke(this, enterItem);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void OnFileListMouseDown(object? sender, MouseEventArgs e)
    {
        // Mouse back/forward side buttons (audit finding G055) - navigates whichever panel the
        // mouse is physically over, activating it first the same way a left-click would.
        if (e.Button == MouseButtons.XButton1)
        {
            ActivatePanel();
            _ = _vm.GoBackAsync();
            return;
        }
        if (e.Button == MouseButtons.XButton2)
        {
            ActivatePanel();
            _ = _vm.GoForwardAsync();
            return;
        }

        // Right-click selects the item under cursor and shows context menu
        if (e.Button == MouseButtons.Right)
        {
            var info = _fileList.HitTest(e.Location);
            ContextMenuStrip menu;
            if (info.Item?.Tag is FileSystemItem { IsParent: false } fsItem)
            {
                // Explorer's own rule: right-clicking a row that is already part of the current
                // selection preserves the whole selection (so the menu acts on all of it);
                // right-clicking a row outside it collapses the selection down to that row.
                // Deliberately NOT wrapped in _suppressSelectionEvent - letting
                // OnSelectedIndexChanged run is what keeps FileSystemItem.IsSelected (what
                // PanelViewModel.GetSelectedOrActive, and therefore every CommandIds-routed menu
                // item, reads) in step with the highlight. The old suppressed version left the
                // two disagreeing for exactly the lifetime of the menu: the highlight showed one
                // row while a marked multi-selection underneath it was still what Copy/Move/
                // Delete would act on.
                var idx = info.Item.Index;
                if (!_fileList.SelectedIndices.Contains(idx))
                {
                    _fileList.SelectedIndices.Clear();
                    _fileList.SelectedIndices.Add(idx);
                }
                _fileList.FocusedItem = info.Item;
                _vm.SelectedItem = fsItem;

                var targets = ResolveContextTargets();
                if (targets.Count == 0) return;
                // The analyzer can't trace ownership across the BuildItemContextMenu() call
                // boundary - disposal happens via AutoDisposeOnClose wired immediately below.
#pragma warning disable CA2000
                menu = BuildItemContextMenu(targets);
#pragma warning restore CA2000
            }
            else
            {
                // Empty space below the list, or ".." - a distinct folder-scoped menu, not the
                // item menu acting on whatever was selected before this click (the previous
                // behavior: BuildContextMenu() was called unconditionally here, so it silently
                // operated on a stale selection with no item under the cursor at all).
#pragma warning disable CA2000
                menu = BuildBackgroundContextMenu();
#pragma warning restore CA2000
            }
            // Not menu.Closed += (_, _) => menu.Dispose() - disposing synchronously inside Closed
            // crashes the app (ObjectDisposedException from SetVisibleCore continuing to touch
            // Handle after Dispose already tore it down); see UiHelpers.AutoDisposeOnClose. Same
            // class of bug this "safe" pattern was believed to avoid (F031) - it didn't, it just
            // hadn't been hit here yet; TerminalCanvas.ShowContextMenu hit it first.
            UiHelpers.AutoDisposeOnClose(menu, this);
            menu.Show(_fileList, e.Location);
        }
    }

    /// <summary>
    /// The items the item-level context menu acts on, resolved from the ListView's own
    /// <c>SelectedIndices</c> right after the right-click handler has reconciled it per Explorer's
    /// rule above - the highlight and <see cref="FileSystemItem.IsSelected"/> agree by
    /// construction at this point (see the right-click branch's own comment), so reading either
    /// one here would give the same answer; <c>SelectedIndices</c> is used because it's what
    /// <see cref="OnFileListItemDrag"/> already established as the VirtualMode-safe way to read a
    /// ListView selection (<c>SelectedItems</c> throws in VirtualMode).
    /// </summary>
    private IReadOnlyList<FileSystemItem> ResolveContextTargets()
    {
        var result = new List<FileSystemItem>();
        var vmItems = _vm.Items;
        foreach (int idx in _fileList.SelectedIndices)
            if (idx >= 0 && idx < vmItems.Count && vmItems[idx] is { IsParent: false } item)
                result.Add(item);
        return result;
    }

    private void OnFileListMouseMove(object? sender, MouseEventArgs e)
    {
        var info = _fileList.HitTest(e.Location);
        var newHoveredIndex = info.Item?.Index is >= 0 ? info.Item.Index : (int?)null;
        if (_hoveredIndex != newHoveredIndex)
        {
            var oldBounds = _hoveredIndex is { } oldIdx && oldIdx < _fileList.Items.Count ? _fileList.Items[oldIdx].Bounds : (Rectangle?)null;
            _hoveredIndex = newHoveredIndex;
            var newBounds = info.Item?.Bounds;

            if (oldBounds is { } ob)
                _fileList.Invalidate(new Rectangle(0, ob.Top, _fileList.ClientSize.Width, ob.Height));
            if (newBounds is { } nb)
                _fileList.Invalidate(new Rectangle(0, nb.Top, _fileList.ClientSize.Width, nb.Height));
        }
    }

    private void OnFileListMouseLeave(object? sender, EventArgs e)
    {
        if (_hoveredIndex is { } idx)
        {
            _hoveredIndex = null;
            if (idx < _fileList.Items.Count)
            {
                var bounds = _fileList.Items[idx].Bounds;
                _fileList.Invalidate(new Rectangle(0, bounds.Top, _fileList.ClientSize.Width, bounds.Height));
            }
        }
    }

    private static readonly string[] SortColumnMap = ["Name", "Extension", "Size", "Modified", "Attributes"];

    private void OnFileListColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (e.Column < 0 || e.Column >= SortColumnMap.Length) return;
        var column = SortColumnMap[e.Column];
        if (_vm.SortColumn == column)
            _vm.SortDescending = !_vm.SortDescending;
        else
            _vm.SortColumn = column;
    }

    private void OnFileListMouseClick(object? sender, MouseEventArgs e)
    {
        ActivatePanel();
    }

    private void ActivatePanel()
    {
        PanelActivated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves real keyboard focus to this panel's file list.
    ///
    /// <para><b>Why this needs to exist at all.</b> Tab-switching panels
    /// (<c>MainForm.OnFormKeyDown</c>) used to only call <c>MainViewModel.SetActivePanel</c> -
    /// updating which panel is logically active (border highlight, F5/F6 targeting) without moving
    /// the real WinForms/Win32 keyboard focus there. The two states would then disagree the instant
    /// Tab was pressed: <c>_fileList.GotFocus</c> on this same panel already calls
    /// <see cref="ActivatePanel"/> to keep them in sync for a <i>mouse</i> click, but nothing did
    /// the equivalent for a <i>keyboard</i> switch. Whoever still held real OS focus (the panel Tab
    /// was pressed <em>away</em> from) would silently reclaim active-panel status the next time
    /// anything brought the window to the foreground and Windows restored focus to its
    /// last-focused child - reverting a Tab the user had already pressed, with nothing on screen
    /// explaining why. Reproduced consistently once a test harness needed the two states to be
    /// exactly consistent to work at all; a human clicking as they go rarely hits the gap, which is
    /// why it went unnoticed.</para>
    /// </summary>
    public void FocusFileList() => _fileList.Focus();

    // -- Quick filter (Ctrl+F) --

    /// <summary>Shows the filter box (focusing and selecting any existing text) if hidden, or
    /// hides it (and clears the filter) if already shown - the same "toggle re-hides" contract
    /// Ctrl+P/ToggleFlatView's own command already has.</summary>
    public void ToggleQuickFilter()
    {
        if (_filterBar.Visible) HideQuickFilter();
        else ShowQuickFilter();
    }

    private void ShowQuickFilter()
    {
        _filterBar.Visible = true;
        _filterBox.Focus();
        _filterBox.SelectAll();
    }

    /// <summary>Hides the bar and clears the filter - matches every mainstream file manager's own
    /// "Esc closes the filter box AND un-filters the list" convention, rather than leaving a
    /// filtered list on screen with no visible indication of why items are missing.</summary>
    private void HideQuickFilter()
    {
        _filterBar.Visible = false;
        _filterBox.Text = "";
        FocusFileList();
    }

    // -- Drag & Drop --

    private void OnFileListItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        // SelectedItems is unavailable in VirtualMode (InvalidOperationException) - SelectedIndices
        // (native, Win32-backed) mapped against the model list stands in for it.
        var items = new List<FileSystemItem>();
        var vmItems = _vm.Items;
        foreach (int idx in _fileList.SelectedIndices)
        {
            if (idx >= 0 && idx < vmItems.Count && vmItems[idx] is { IsParent: false } item)
                items.Add(item);
        }

        if (items.Count == 0) return;

        // The internal payload survives virtual paths, which the shell FileDrop format cannot
        // carry - not archive ones and not remote ones. Handing Explorer a "dav://host/f.txt"
        // through FileDrop would announce a file that does not exist at that path.
        var data = new DataObject();
        data.SetData(PanelDragPayload.Format, new PanelDragPayload(this, items));

        // Was HasNativePaths-gated (items.Where(...).Select(i => i.FullPath)), which meant an SMB
        // panel could never drag out to Explorer at all - GetShellPath answers both "can this
        // leave the app" and "what does it look like once it does" in one call, so an SMB path
        // correctly turns into "\\host\share\..." here instead of being suppressed outright.
        var shellPaths = items.Select(ShellPathOf).OfType<string>().ToArray();
        if (shellPaths.Length > 0)
            data.SetData(DataFormats.FileDrop, shellPaths);

        _ = _fileList.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private static bool CanAccept(IDataObject? data) =>
        data != null && (data.GetDataPresent(PanelDragPayload.Format) || data.GetDataPresent(DataFormats.FileDrop));

    private void OnFileListDragEnter(object? sender, DragEventArgs e)
    {
        if (!CanAccept(e.Data))
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        e.Effect = (e.KeyState & 32) != 0  // Alt key = Move
            ? DragDropEffects.Move
            : DragDropEffects.Copy;
    }

    private void OnFileListDragOver(object? sender, DragEventArgs e)
    {
        if (!CanAccept(e.Data))
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        e.Effect = (e.KeyState & 32) != 0
            ? DragDropEffects.Move
            : DragDropEffects.Copy;

        HighlightDropTarget(FolderUnderCursor(e));
    }

    private void OnFileListDragLeave(object? sender, EventArgs e) => HighlightDropTarget(null);

    private void OnFileListDragDrop(object? sender, DragEventArgs e)
    {
        var dropFolder = FolderUnderCursor(e);
        HighlightDropTarget(null);

        if (!CanAccept(e.Data)) return;

        var isCopy = e.Effect == DragDropEffects.Copy;
        var destination = dropFolder?.FullPath ?? _vm.CurrentPath;

        if (e.Data!.GetData(PanelDragPayload.Format) is PanelDragPayload payload)
        {
            if (ReferenceEquals(payload.Source, this) && dropFolder == null)
                return; // dropped onto itself — nothing to do

            ActivatePanel();
            ItemsDropped?.Invoke(this, new PanelDropEventArgs
            {
                SourcePanel = payload.Source,
                Items = payload.Items,
                Destination = destination,
                IsCopy = isCopy
            });
            return;
        }

        var paths = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (paths == null || paths.Length == 0) return;

        ActivatePanel();
        ItemsDropped?.Invoke(this, new PanelDropEventArgs
        {
            ExternalPaths = paths,
            Destination = destination,
            IsCopy = isCopy
        });
    }

    private FileSystemItem? FolderUnderCursor(DragEventArgs e)
    {
        var point = _fileList.PointToClient(new Point(e.X, e.Y));
        var hit = _fileList.HitTest(point);
        return hit.Item?.Tag is FileSystemItem { IsDirectory: true, IsParent: false } folder ? folder : null;
    }

    private int? _dropHighlightIndex;

    private void HighlightDropTarget(FileSystemItem? folder)
    {
        // Index-based (see _hoveredIndex's own comment) - folder identity is looked up against the
        // model list directly rather than scanning ListView.Items, which VirtualMode disallows
        // enumerating anyway.
        int? target = null;
        if (folder != null)
        {
            var items = _vm.Items;
            for (var i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], folder)) { target = i; break; }
            }
        }

        if (_dropHighlightIndex == target) return;

        _dropHighlightIndex = target;
        // OnDrawSubItem recomputes back/fore color purely from isDropTarget/isSelected/isHovered -
        // it never reads a ListViewItem's own BackColor/ForeColor, so there is nothing further to
        // set here beyond the repaint RefreshItemColors triggers.
        RefreshItemColors();
    }

    // -- Context menu --

    /// <summary>
    /// Builds a fresh context menu, read against the current language/theme at call time. Built
    /// new on every right-click and never stored - self-disposes once closed (see the
    /// <c>Closed</c> subscription at the call site), the same pattern
    /// <c>TerminalCanvas.ShowContextMenu</c> already uses. A persistent, rebuilt-in-place instance
    /// (the previous design) had no safe way to be rebuilt while still open: disposing it out from
    /// under WinForms' own dropdown-tracking/click-dismissal machinery crashed the app on the next
    /// mouse click or item click, however carefully the dispose was ordered - only never keeping a
    /// stale instance around at all sidesteps the whole class of bug.
    /// </summary>
    private ContextMenuStrip BuildItemContextMenu(IReadOnlyList<FileSystemItem> targets)
    {
        var L = LocalizationService.Current;
        var single = targets.Count == 1 ? targets[0] : null;
        // The analyzer can't trace disposal happening via the caller's Closed handler.
#pragma warning disable CA2000
        var menu = NewThemedMenu();
#pragma warning restore CA2000

        // View/Edit/Open With are inherently single-file actions - gated to a single target
        // rather than acting on targets[0] of a larger selection, which would silently ignore
        // the rest of what the user right-clicked.
        CtxItem(menu, "Ctx.View", "view", single != null, () => { if (single != null) ViewRequested?.Invoke(this, single); });
        CtxItem(menu, "Ctx.Edit", "edit", single != null, () => { if (single != null) EditRequested?.Invoke(this, single); });
        // "Open With…" only for a native-path, non-directory item - same restriction Explorer's
        // own "Open with" applies (an archive entry/remote file has no real path an outside
        // process could open, and "open with" on a folder isn't a thing Explorer offers either).
        if (single is { IsDirectory: false } && HasNativePaths)
        {
            var openWithItem = single;
            CtxItem(menu, "Ctx.OpenWith", "view", () => OpenWithRequested?.Invoke(this, openWithItem));
        }
        // "Open in Explorer" - shown for every target with a real Windows path (local disk, SMB;
        // hidden for an archive entry, FTP/SFTP/WebDAV, MTP - see IFileSystem.GetShellPath).
        // Unlike Open With, this is a whole-selection action.
        var explorerPaths = targets.Select(ShellPathOf).OfType<string>().ToList();
        if (explorerPaths.Count > 0)
        {
            CtxItem(menu, "Ctx.OpenInExplorer", "explorer", () => OpenInExplorerRequested?.Invoke(this, explorerPaths));
        }
        // A single directory target opens a terminal there; anything else (a file, or more than
        // one target) falls back to the panel's own current directory - same target resolution
        // "Open in Explorer" would apply, but there's no per-file terminal to open.
        if (CurrentShellFolder is { } terminalFolder)
        {
            var terminalTarget = single is { IsDirectory: true } dirTarget
                ? ShellPathOf(dirTarget) ?? terminalFolder
                : terminalFolder;
            CtxItem(menu, "Ctx.TerminalHere", "terminal", () => OpenTerminalHereRequested?.Invoke(this, terminalTarget));
        }
        menu.Items.Add(new ToolStripSeparator());
        CtxItem(menu, "Ctx.Copy", "copy", () => CopyRequested?.Invoke(this, EventArgs.Empty));
        CtxItem(menu, "Ctx.Move", "move", () => MoveRequested?.Invoke(this, EventArgs.Empty));
        // System-clipboard Copy/Cut - distinct from Ctx.Copy/Ctx.Move above (panel-to-panel F5/F6,
        // no clipboard involved). Gated the same as Open in Explorer: a real Windows path only.
        if (explorerPaths.Count > 0)
        {
            CtxItem(menu, "Ctx.ClipboardCopy", "copy", () => ClipboardCopyRequested?.Invoke(this, EventArgs.Empty));
            CtxItem(menu, "Ctx.ClipboardCut", "cut", () => ClipboardCutRequested?.Invoke(this, EventArgs.Empty));
        }
        CtxItem(menu, "Ctx.Rename", "rename", single != null, () => RenameRequested?.Invoke(this, EventArgs.Empty));
        CtxItem(menu, "Ctx.Delete", "delete", () => DeleteRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        CtxItem(menu, "Ctx.Split", "split", single != null, () => SplitRequested?.Invoke(this, EventArgs.Empty));
        // "Combine from parts..." only makes sense when the selection actually looks like a split
        // part - showing it unconditionally would just error out for every other file/folder.
        // Informational check only (same as MainForm's own preview regex before it opens
        // CombineDialogForm) - the authoritative missing-part validation stays inside
        // Operations.CombineOperation, which re-discovers the sequence itself when it runs.
        if (single is { IsDirectory: false } && SplitPartNameRegex.IsMatch(single.Name))
        {
            CtxItem(menu, "Ctx.Combine", "combine", () => CombineRequested?.Invoke(this, EventArgs.Empty));
        }
        // "Verify checksums..." only for a recognized checksum-file extension - showing it
        // unconditionally would just error out parsing an unrelated file.
        if (single is { IsDirectory: false } && IsChecksumFileExtension(single.Extension))
        {
            var checksumItem = single;
            CtxItem(menu, "Ctx.VerifyChecksum", "properties", () => VerifyChecksumRequested?.Invoke(this, checksumItem));
        }
        // "Create Link" submenu - only for a native-path filesystem: a symlink/hardlink needs a
        // real path the OS can resolve, which an archive entry or remote file has none of.
        if (single != null && HasNativePaths)
        {
            var linkItem = single;
#pragma warning disable CA2000
            var linkMenu = new ToolStripMenuItem(L.GetString("Ctx.CreateLink"), ToolbarIcons.Get("newdir"));
#pragma warning restore CA2000
            CtxSubItem(linkMenu, "Ctx.CreateSymlink", () => CreateSymlinkRequested?.Invoke(this, linkItem));
            CtxSubItem(linkMenu, "Ctx.CreateHardlink", () => CreateHardlinkRequested?.Invoke(this, linkItem));
            menu.Items.Add(linkMenu);
        }
        menu.Items.Add(new ToolStripSeparator());
        CtxItem(menu, "Ctx.Properties", "properties", () => PropertiesRequested?.Invoke(this, EventArgs.Empty));
        // Native Windows Properties sheet - single target only for now (Phase 5 upgrades this to
        // the real multi-item sheet via the IContextMenu host, the way Explorer's own does).
        if (single != null && ShellPathOf(single) is { } shellPropPath)
        {
            CtxItem(menu, "Ctx.ShellProperties", "properties", () => ShellPropertiesRequested?.Invoke(this, shellPropPath));
        }

        // Copy path submenu. Sub-items go into copyPathMenu.DropDownItems below, which goes into
        // menu.Items - menu.Dispose() (via the caller's Closed handler) walks both levels. Joined
        // with CRLF for a multi-target selection - the same convention Explorer's own "Copy as
        // path" uses for more than one selected item.
#pragma warning disable CA2000
        var copyPathMenu = new ToolStripMenuItem(L.GetString("Ctx.CopyPath"), ToolbarIcons.Get("copy"));
#pragma warning restore CA2000
        CtxSubItem(copyPathMenu, "Ctx.CopyPath.Full", () => CopyToClipboard(string.Join(Environment.NewLine, targets.Select(t => t.FullPath))));
        CtxSubItem(copyPathMenu, "Ctx.CopyPath.Name", () => CopyToClipboard(string.Join(Environment.NewLine, targets.Select(t => t.Name))));
        CtxSubItem(copyPathMenu, "Ctx.CopyPath.NoExt", () => CopyToClipboard(string.Join(Environment.NewLine, targets.Select(FullPathWithoutExtension))));
        menu.Items.Add(copyPathMenu);

        menu.Items.Add(new ToolStripSeparator());
        CtxItem(menu, "Ctx.SelectAll", "selectall", () => _vm.SelectAll());
        CtxItem(menu, "Ctx.InvertSelection", "invert", () => _vm.InvertSelection());

        return menu;
    }

    /// <summary>
    /// The folder-scoped context menu shown when right-clicking empty space below the list, or
    /// ".." - there is no <see cref="FileSystemItem"/> under the cursor here, so every item acts
    /// on the panel's current directory rather than a selection.
    /// </summary>
    private ContextMenuStrip BuildBackgroundContextMenu()
    {
#pragma warning disable CA2000
        var menu = NewThemedMenu();
#pragma warning restore CA2000

        // Enabled state is the cheap IsClipboardFormatAvailable probe - never GetDataObject()
        // here, which would OLE round-trip to the clipboard's owning process and could hang this
        // menu build if that process is busy or unresponsive. Greyed rather than hidden, matching
        // Explorer's own "Paste" - a vanished item would look like the feature doesn't exist.
        CtxItem(menu, "Ctx.ClipboardPaste", "paste",
            _vm.CurrentFileSystem.Capabilities.HasFlag(FileSystemCapabilities.Writable) && ClipboardHelper.ContainsFileDrop(),
            () => ClipboardPasteRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        CtxItem(menu, "Ctx.NewFolder", "newdir", () => MakeDirRequested?.Invoke(this, EventArgs.Empty));
        CtxItem(menu, "Ctx.NewFile", "editnew", () => NewFileRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        // "Open in Explorer" here opens the folder itself (no item under the cursor to select) -
        // hidden inside an archive/FTP/SFTP/WebDAV/MTP, shown for local disk and SMB.
        if (CurrentShellFolder is { } folderShellPath)
        {
            CtxItem(menu, "Ctx.OpenInExplorer", "explorer", () => OpenInExplorerRequested?.Invoke(this, new[] { folderShellPath }));
            CtxItem(menu, "Ctx.TerminalHere", "terminal", () => OpenTerminalHereRequested?.Invoke(this, folderShellPath));
        }
        CtxItem(menu, "Ctx.Refresh", "refresh", () => RefreshRequested?.Invoke(this, EventArgs.Empty));
        CtxItem(menu, "Ctx.SelectAll", "selectall", () => _vm.SelectAll());
        CtxItem(menu, "Ctx.InvertSelection", "invert", () => _vm.InvertSelection());
        menu.Items.Add(new ToolStripSeparator());
        CtxItem(menu, "Ctx.FolderProperties", "properties", () => FolderPropertiesRequested?.Invoke(this, EventArgs.Empty));
        if (CurrentShellFolder is { } shellPropPath)
        {
            CtxItem(menu, "Ctx.ShellProperties", "properties", () => ShellPropertiesRequested?.Invoke(this, shellPropPath));
        }

        return menu;
    }

    /// <summary>Builds an empty, themed <see cref="ContextMenuStrip"/> shell shared by both context
    /// menu builders - colors/font/renderer read fresh at call time so a theme switch since the
    /// last right-click is reflected immediately.</summary>
    private static ContextMenuStrip NewThemedMenu() => new()
    {
        BackColor = ThemeService.Current.HeaderBackground,
        ForeColor = ThemeService.Current.Foreground,
        Font = ThemeService.Current.GridFont,
        ImageScalingSize = new Size(16, 16),
        Renderer = new ThemeRenderer()
    };

    /// <summary>Matches a split-part file name (<c>&lt;base&gt;.NNN</c>, 3+ digits) - same pattern
    /// as <see cref="Operations.CombineOperation"/>'s own, used here only to decide whether to show
    /// the "Combine from parts..." context menu item.</summary>
    private static readonly System.Text.RegularExpressions.Regex SplitPartNameRegex =
        new(@"\.\d{3,}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Whether <paramref name="extension"/> (lowercase, dot-inclusive - see
    /// <see cref="FileSystem.FileEntry.GetExtension"/>) is one of the formats
    /// <see cref="Services.ChecksumService"/> exports/parses.</summary>
    private static bool IsChecksumFileExtension(string extension) =>
        extension is ".sfv" or ".md5" or ".sha1" or ".sha256";

    /// <summary>The item's full path with its own extension (if any) trimmed off the end - what
    /// "Copy Path ▸ Path Without Extension" puts on the clipboard. A directory (empty
    /// <see cref="FileSystemItem.Extension"/>) is returned unchanged.</summary>
    private static string FullPathWithoutExtension(FileSystemItem? item)
    {
        if (item == null) return "";
        var ext = item.Extension;
        return ext.Length > 0 && item.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
            ? item.FullPath[..^ext.Length]
            : item.FullPath;
    }

    private static void CtxItem(ContextMenuStrip menu, string key, string iconKey, Action action) =>
        CtxItem(menu, key, iconKey, enabled: true, action);

    private static void CtxItem(ContextMenuStrip menu, string key, string iconKey, bool enabled, Action action)
    {
        var L = LocalizationService.Current;
        var item = new ToolStripMenuItem(L.GetString(key), ToolbarIcons.Get(iconKey)) { Enabled = enabled };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    /// <summary>Adds one entry to an already-created submenu header (e.g. "Create Link ▸", "Copy
    /// Path ▸") - the submenu-building counterpart to <see cref="CtxItem"/> above.</summary>
    private static void CtxSubItem(ToolStripMenuItem parent, string key, Action action)
    {
        var L = LocalizationService.Current;
#pragma warning disable CA2000 // owned by parent.DropDownItems, walked by menu.Dispose()
        var item = new ToolStripMenuItem(L.GetString(key), null, (_, _) => action());
#pragma warning restore CA2000
        parent.DropDownItems.Add(item);
    }

    private static void CopyToClipboard(string text)
    {
        if (!string.IsNullOrEmpty(text))
            ClipboardHelper.TrySetClipboard(text);
    }

    // -- Re-localization --

    private void Relocalize()
    {
        _fileList.BeginUpdate();
        var L = LocalizationService.Current;
        _fileList.Columns[0].Text = L.GetString("Panel.Name");
        _fileList.Columns[1].Text = L.GetString("Panel.Ext");
        _fileList.Columns[2].Text = L.GetString("Panel.Size");
        _fileList.Columns[3].Text = L.GetString("Panel.Modified");
        _fileList.Columns[4].Text = L.GetString("Panel.Attributes");
        _fileList.GridLines = false;
        _fileList.EndUpdate();
        // No context-menu rebuild needed here anymore - BuildContextMenu() runs fresh on every
        // right-click (see the call site in OnFileListMouseDown) and always reads the current
        // language at that point.

        _tabStrip.CloseButtonTooltip = L.GetString("Panel.Tab.Close");
        _tabStrip.RefreshTabStrip();
        var newTabText = L.GetString("Panel.Tab.New");
        _addTabButton.AccessibleName = newTabText;
        _addTabTooltip.SetToolTip(_addTabButton, newTabText);
    }

    // -- Drive bar --

    private void PopulateDriveBar()
    {
        // Dispose old buttons — ToolStripItemCollection.Clear() does NOT call Dispose on items,
        // which would leak each button's Image and event handlers.
        foreach (var btn in _driveButtons)
            btn.Dispose();
        _driveBar.Items.Clear();
        _driveButtons.Clear();

        var p = ThemeService.Current;
        var L = LocalizationService.Current;
        var currentRoot = Path.GetPathRoot(_vm.CurrentPath);
        var toolbarScale = GetToolbarScale();
        var iconW = _driveBar.ImageScalingSize.Width;
        var btnHeight = _driveBar.Height - 6;
        var btnWidth = (int)Math.Round(80 * toolbarScale);

        // Drives come from DriveCatalog's cached snapshot, never from DriveInfo directly: IsReady,
        // VolumeLabel and TotalSize all issue a device query that blocks for seconds on an empty
        // optical drive or a dead network share, and this method runs on the UI thread.
        // An unavailable drive is still drawn (dimmed) rather than filtered out - a button that
        // disappears whenever a disc is missing is worse than one that says so.
        foreach (var drive in DriveCatalog.Instance.Current)
        {
            // Keyed on DriveKind, not the raw DriveType: the latter cannot tell a flash drive
            // from a floppy, and the model is the right place for that distinction.
            var iconKey = drive.Kind switch
            {
                DriveKind.Fixed => "drive_fixed",
                DriveKind.Usb => "drive_usb",
                DriveKind.Floppy => "drive_removable",
                DriveKind.Optical => "drive_cdrom",
                DriveKind.Network => "drive_network",
                DriveKind.RamDisk => "drive_ram",
                _ => "drive"
            };

            var rootPath = drive.RootPath;
            var isCurrent = string.Equals(
                Path.GetPathRoot(rootPath),
                currentRoot,
                StringComparison.OrdinalIgnoreCase);

            var icon = ToolbarIcons.Get(iconKey) ?? ToolbarIcons.Get("drive")!;
            var btn = new ToolStripButton(drive.Letter)
            {
                Image = icon,
                Tag = new DriveButtonState(rootPath),
                ToolTipText = L.GetString("Panel.DriveTooltip", drive.DisplayName),
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextAlign = ContentAlignment.MiddleCenter,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Padding = new Padding((int)Math.Round(8 * toolbarScale), 0, (int)Math.Round(8 * toolbarScale), 0),
                Margin = new Padding((int)Math.Round(3 * toolbarScale), 0, (int)Math.Round(3 * toolbarScale), 0),
                AutoSize = false,
                Overflow = ToolStripItemOverflow.AsNeeded
            };
            btn.Size = new Size(btnWidth, btnHeight);
            btn.Click += (_, _) =>
            {
                ActivatePanel();
                _ = _vm.NavigateAsync(rootPath);
            };

            _driveBar.Items.Add(btn);
            _driveButtons.Add(btn);
        }

        AddNetworkButton(toolbarScale, btnHeight);
        AddMtpDeviceButtons(toolbarScale, btnHeight);
        AddConnectionButtons(toolbarScale, btnHeight);
        UpdateDriveBarDim();
    }

    /// <summary>Adds a "Network" button that opens <see cref="NetworkBrowseForm"/> for browsing
    /// SMB servers and shares on the local network.</summary>
    private void AddNetworkButton(float toolbarScale, int btnHeight)
    {
        var L = LocalizationService.Current;
        var icon = ToolbarIcons.Get("drive_network") ?? ToolbarIcons.Get("drive")!;
        var btn = new ToolStripButton
        {
            Image = icon,
            Text = "",
            ToolTipText = L.GetString("Panel.NetworkButton"),
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Padding = new Padding((int)Math.Round(4 * toolbarScale), 0, (int)Math.Round(4 * toolbarScale), 0),
            Margin = new Padding((int)Math.Round(3 * toolbarScale), 0, (int)Math.Round(3 * toolbarScale), 0),
            AutoSize = false,
            Overflow = ToolStripItemOverflow.AsNeeded
        };
        btn.Size = new Size((int)Math.Round(32 * toolbarScale), btnHeight);
        btn.Click += (_, _) => NetworkBrowseRequested?.Invoke(this, EventArgs.Empty);
        _driveBar.Items.Add(btn);
        _driveButtons.Add(btn);
    }

    /// <summary>Adds MTP device buttons (Android phones, cameras) discovered by
    /// <see cref="MtpDeviceCatalog"/>.</summary>
    private void AddMtpDeviceButtons(float toolbarScale, int btnHeight)
    {
        var devices = MtpDeviceCatalog.Instance.Current;
        if (devices.Count == 0) return;

        var L = LocalizationService.Current;
        var icon = ToolbarIcons.Get("drive_usb") ?? ToolbarIcons.Get("drive")!;

        foreach (var device in devices)
        {
            var btn = new ToolStripButton(device.DisplayName)
            {
                Image = icon,
                Tag = new DriveButtonState(RemotePath.Make("mtp", device.DeviceId)),
                ToolTipText = L.GetString("Panel.MtpDevice", device.DisplayName),
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextAlign = ContentAlignment.MiddleCenter,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Padding = new Padding((int)Math.Round(8 * toolbarScale), 0, (int)Math.Round(8 * toolbarScale), 0),
                Margin = new Padding((int)Math.Round(3 * toolbarScale), 0, (int)Math.Round(3 * toolbarScale), 0),
                AutoSize = true,
                Overflow = ToolStripItemOverflow.AsNeeded
            };
            btn.Height = btnHeight;

            var deviceId = device.DeviceId;
            btn.Click += (_, _) => MtpDeviceActivated?.Invoke(this, deviceId);

            _driveBar.Items.Add(btn);
            _driveButtons.Add(btn);
        }
    }

    /// <summary>
    /// Appends the configured connections after the drives, so "places" is one strip rather than
    /// two competing ones.
    ///
    /// A connection is shown in every state, including failed: hiding a connection the user
    /// configured because its server is down would leave them with no way to retry it. State is
    /// carried by the tooltip and by dimming, not by removing the button.
    /// </summary>
    private void AddConnectionButtons(float toolbarScale, int btnHeight)
    {
        var L = LocalizationService.Current;
        var statuses = ConnectionManager.Instance.Current;
        if (statuses.Count == 0) return;

        foreach (var status in statuses)
        {
            var stateText = L.GetString(status.State switch
            {
                ConnectionState.Connected => "Conn.State.Connected",
                ConnectionState.Connecting => "Conn.State.Connecting",
                ConnectionState.Failed => "Conn.State.Failed",
                _ => "Conn.State.Disconnected",
            });

            var tooltip = status.State == ConnectionState.Failed && status.Error.Length > 0
                ? L.GetString("Conn.Tooltip", status.Name, $"{stateText}: {status.Error}")
                : L.GetString("Conn.Tooltip", status.Name, stateText);

            var btn = new ToolStripButton(status.Name)
            {
                Image = ToolbarIcons.Get("connection"),
                // The same state object the drive buttons carry, so "you are here" is drawn for a
                // connection exactly as it is for a drive. Without it the places bar showed nothing
                // at all once the panel was inside a connection - the drive buttons went dark and
                // no connection lit up, so the bar stopped saying where the panel was.
                Tag = new DriveButtonState(status.RootPath),
                ToolTipText = tooltip,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextAlign = ContentAlignment.MiddleCenter,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Padding = new Padding((int)Math.Round(8 * toolbarScale), 0, (int)Math.Round(8 * toolbarScale), 0),
                Margin = new Padding((int)Math.Round(3 * toolbarScale), 0, (int)Math.Round(3 * toolbarScale), 0),
                AutoSize = true,
                Overflow = ToolStripItemOverflow.AsNeeded,
                // Not connected yet is a normal state, not an error - only a failed attempt is
                // dimmed, so "never tried" and "tried and failed" stay distinguishable.
                ForeColor = status.State == ConnectionState.Failed
                    ? ThemeService.Current.DimForeground
                    : ThemeService.Current.HeaderForeground,
            };
            btn.Height = btnHeight;

            var id = status.ProfileId;
            btn.Click += (_, _) =>
            {
                ActivatePanel();
                ConnectionActivated?.Invoke(this, id);
            };

            _driveBar.Items.Add(btn);
            _driveButtons.Add(btn);
        }
    }

    private void UpdateDriveBarHighlight()
    {
        // Path.GetPathRoot answers "" for a remote path, so asking it alone would mean no button
        // can ever match while the panel is inside a connection.
        var currentRoot = RemotePath.IsRemote(_vm.CurrentPath)
            ? RemotePath.GetRoot(_vm.CurrentPath)
            : Path.GetPathRoot(_vm.CurrentPath);
        var dimmed = !_vm.IsActive;

        // Previously this packed RootPath/IsCurrent/Dimmed into a single delimited string and
        // wrote it back into Tag, then re-derived RootPath via Path.GetPathRoot() on THAT string
        // the next time this ran - which happened to still work for "C:\|True|False" (the drive
        // letter prefix survives) but breaks for UNC roots, where the appended "|True|False"
        // corrupts the \\server\share prefix GetPathRoot expects. DriveButtonState keeps
        // RootPath immutable and separate from the two mutable flags instead.
        foreach (var btn in _driveButtons)
        {
            if (btn.Tag is not DriveButtonState state) continue;
            state.IsCurrent = string.Equals(state.RootPath, currentRoot, StringComparison.OrdinalIgnoreCase);
            state.Dimmed = dimmed;
        }
        _driveBar.Invalidate();
    }

    private void UpdateDriveBarDim()
    {
        var p = ThemeService.Current;
        _driveBar.BackColor = _vm.IsActive ? p.HeaderBackground : ThemeService.DimColor(p.HeaderBackground, 92);
        _driveBar.ForeColor = _vm.IsActive ? p.HeaderForeground : ThemeService.DimColor(p.HeaderForeground, 92);
        UpdateDriveBarHighlight();
    }

    // -- Owner draw --

    private void OnDrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        var p = ThemeService.Current;
        var rect = e.Bounds;

        using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(
            rect, p.ColumnHeaderGradient, p.HeaderBackground, 90f))
            e.Graphics!.FillRectangle(bg, rect);

        if (e.Header == null || e.ColumnIndex < 0)
        {
            using var tailBottom = new Pen(p.GridLine);
            e.Graphics.DrawLine(tailBottom, rect.X, rect.Bottom - 1, rect.Right, rect.Bottom - 1);
            return;
        }

        var textRect = new Rectangle(rect.X + 6, rect.Y, rect.Width - 8, rect.Height);

        if (e.ColumnIndex < SortColumnMap.Length && SortColumnMap[e.ColumnIndex] == _vm.SortColumn)
        {
            var arrowSize = 8;
            var arrowX = rect.Right - arrowSize - 6;
            var arrowY = rect.Y + (rect.Height - arrowSize) / 2;
            var arrowRect = new Rectangle(arrowX, arrowY, arrowSize, arrowSize);
            TextRenderer.DrawText(e.Graphics, _vm.SortDescending ? "\u25BC" : "\u25B2", p.GridFont, arrowRect,
                p.HeaderForeground, TextFormatFlags.Left | TextFormatFlags.Top);
            textRect.Width -= arrowSize + 8;
        }

        TextRenderer.DrawText(e.Graphics, e.Header.Text, p.GridFontBold, textRect,
            p.HeaderForeground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        using var sep = new Pen(p.GridLine);
        e.Graphics.DrawLine(sep, rect.Right - 1, rect.Y + 3, rect.Right - 1, rect.Bottom - 3);

        using var bottom = new Pen(p.GridLine);
        e.Graphics.DrawLine(bottom, rect.X, rect.Bottom - 1, rect.Right, rect.Bottom - 1);
    }

    /// <summary>
    /// Makes the last column fill the remaining width of the ListView,
    /// eliminating the white tail area in the column header.
    /// </summary>
    private void OnFileListResize(object? sender, EventArgs e)
    {
        FillLastColumnWidth();
    }

    private bool _fillingLastColumn;

    private void FillLastColumnWidth()
    {
        if (_fillingLastColumn || _fileList.Columns.Count == 0) return;

        // Re-entrancy guard: setting Columns[^1].Width below can itself trigger a native
        // scrollbar toggle (ListView re-decides whether the columns fit), which raises the
        // overlay's NativeMetricsChanged, which calls back in here.
        _fillingLastColumn = true;
        try
        {
            var totalWidth = 0;
            for (var i = 0; i < _fileList.Columns.Count - 1; i++)
            {
                totalWidth += _fileList.Columns[i].Width;
            }

            // -1: never let the column sum land exactly on ClientSize.Width. Landing exactly on
            // it used to make the ListView decide it needs a horizontal scrollbar of its own
            // (comctl32's "does it fit" check), which fought with this method for control of the
            // last column's width — visible as a spurious horizontal bar in a panel whose columns
            // should exactly fit.
            var remainingWidth = _fileList.ClientSize.Width - totalWidth - 1;
            // Minimum width for the last column to prevent it from disappearing
            var lastColumnWidth = Math.Max(remainingWidth, 45);
            var lastColumn = _fileList.Columns[_fileList.Columns.Count - 1];
            if (lastColumn.Width != lastColumnWidth) lastColumn.Width = lastColumnWidth;
        }
        finally
        {
            _fillingLastColumn = false;
        }
    }

    private void OnDrawItem(object? sender, DrawListViewItemEventArgs e)
    {
        e.DrawDefault = false;
    }

    private void OnFileListPaint(object? sender, PaintEventArgs e)
    {
        var p = ThemeService.Current;
        var rect = _fileList.ClientRectangle;

        if (_fileList.Items.Count == 0)
        {
            using var bg = new SolidBrush(p.PanelBackground);
            e.Graphics!.FillRectangle(bg, rect);
            return;
        }

        var lastItem = _fileList.Items[_fileList.Items.Count - 1];
        var bottomY = lastItem.Bounds.Bottom;
        if (bottomY < rect.Bottom)
        {
            using var bg = new SolidBrush(p.PanelBackground);
            e.Graphics!.FillRectangle(bg, new Rectangle(0, bottomY, rect.Width, rect.Bottom - bottomY));
        }
    }

    private void OnDrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var p = ThemeService.Current;
        var lvi = e.Item!;
        var item = lvi.Tag as FileSystemItem;
        var isSelected = lvi.Selected;
        var isDropTarget = _dropHighlightIndex == lvi.Index;
        var isHovered = _hoveredIndex == lvi.Index;

        Color backColor;
        Color foreColor;

        if (isDropTarget)
        {
            backColor = p.Selection;
            foreColor = p.SelectionForeground;
        }
        else if (isSelected)
        {
            backColor = _vm.IsActive ? p.Selection : p.InactiveSelection;
            foreColor = p.SelectionForeground;
        }
        else if (isHovered)
        {
            backColor = p.RowHover;
            foreColor = GetItemForeColor(item);
        }
        else
        {
            backColor = (lvi.Index % 2 == 0) ? p.PanelBackground : p.AlternatingRow;
            foreColor = GetItemForeColor(item);
        }

        using (var bg = new SolidBrush(backColor))
            e.Graphics!.FillRectangle(bg, e.Bounds);

        var textRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);

        if (e.ColumnIndex == 0 && lvi.ImageList != null)
        {
            Image? img = null;
            if (!string.IsNullOrEmpty(lvi.ImageKey))
                img = lvi.ImageList.Images[lvi.ImageKey];
            else if (lvi.ImageIndex >= 0 && lvi.ImageIndex < lvi.ImageList.Images.Count)
                img = lvi.ImageList.Images[lvi.ImageIndex];

            if (img != null)
            {
                var imgSize = lvi.ImageList.ImageSize;
                var imgY = e.Bounds.Y + (e.Bounds.Height - imgSize.Height) / 2;
                e.Graphics.DrawImage(img, e.Bounds.X + 4, imgY);
                textRect.X += imgSize.Width + 4;
                textRect.Width -= imgSize.Width + 4;
            }
        }

        var format = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        if (e.Header?.TextAlign == HorizontalAlignment.Right)
            format |= TextFormatFlags.Right;

        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", p.GridFont, textRect,
            foreColor, format);

        // Left accent border for focused active item
        if (isSelected && _vm.IsActive && lvi.Focused)
        {
            using var focusPen = new Pen(p.Accent, 2f);
            e.Graphics.DrawLine(focusPen, e.Bounds.X, e.Bounds.Y, e.Bounds.X, e.Bounds.Bottom - 1);
        }
    }

    private static Color GetItemForeColor(FileSystemItem? item)
    {
        if (item == null) return ThemeService.Current.Foreground;
        if (item.IsParent) return ThemeService.Current.DimForeground;

        // Git status takes priority over the normal type-based coloring below (same convention
        // as VS Code's file explorer) - the per-row icon still shows file vs. folder, so this
        // doesn't lose that distinction, just repurposes the text color to surface what changed.
        var gitColor = GetGitStatusColor(item.GitStatus);
        if (gitColor is { } gc) return gc;

        if (item.IsDirectory) return ThemeService.Current.DirectoryColor;
        if (item.IsHidden) return ThemeService.Current.HiddenColor;

        var iconType = FileIcons.GetIconType(item.Extension);
        if (iconType is FileIconType.Executable) return ThemeService.Current.ExecutableColor;
        if (iconType is FileIconType.Archive or FileIconType.DiskImage) return ThemeService.Current.ArchiveColor;
        return ThemeService.Current.Foreground;
    }

    private static Color? GetGitStatusColor(GitFileStatus status) => status switch
    {
        GitFileStatus.Untracked or GitFileStatus.Added => ThemeService.Current.GitAddedColor,
        GitFileStatus.Modified or GitFileStatus.Renamed => ThemeService.Current.GitModifiedColor,
        GitFileStatus.Deleted or GitFileStatus.Conflicted => ThemeService.Current.Danger,
        _ => null
    };

    private void RefreshItemColors()
    {
        _fileList.Invalidate();
    }

    private void ApplyActiveState()
    {
        var p = ThemeService.Current;
        _borderPanel.BackColor = _vm.IsActive
            ? p.FocusBorder
            : p.PanelInactiveBorder;
        _fileList.BackColor = p.PanelBackground;
        RefreshItemColors();
        UpdateDriveBarDim();
        Invalidate();
    }

    // -- Theme --

    /// <summary>Re-reads the extension visibility setting and rebuilds the list to apply it.</summary>
    public void RefreshFromViewModel()
    {
        _showExtensionInName = SettingsService.Load().ShowExtensionInName;
        RebuildList();
    }

    /// <summary>Re-applies the current theme palette to all child controls and rebuilds icons.</summary>
    public void ApplyTheme()
    {
        var p = ThemeService.Current;
        BackColor = p.PanelBackground;
        _borderPanel.BackColor = _vm.IsActive ? p.FocusBorder : p.PanelInactiveBorder;
        _pathBar.BackColor = p.HeaderBackground;
        _pathBar.ForeColor = p.HeaderForeground;
        _pathBar.Font = p.GridFont;
        _breadcrumbBar.BackColor = p.HeaderBackground;
        RebuildBreadcrumb();
        _driveBar.BackColor = p.HeaderBackground;
        _driveBar.ForeColor = p.HeaderForeground;
        _filterBar.BackColor = p.HeaderBackground;
        _filterBox.BackColor = p.HeaderBackground;
        _filterBox.ForeColor = p.HeaderForeground;
        _filterBox.Font = p.GridFont;
        _filterLabel.Font = p.GridFont;
        _filterLabel.ForeColor = p.DimForeground;
        _fileList.BackColor = p.PanelBackground;
        _fileList.ForeColor = p.Foreground;
        _fileList.Font = p.GridFont;
        NativeControlThemer.ThemeListView(_fileList);
        ThemeService.StyleStatusStrip(_statusStrip);
        PopulateFileImageList();
        PopulateDriveBar();
        RebuildList();
        // No context-menu re-theme needed here anymore - BuildContextMenu() runs fresh on every
        // right-click and always reads ThemeService.Current at that point.
    }

    private float GetToolbarScale()
    {
        try
        {
            // DeviceDpi is per-monitor aware (unlike Graphics.FromHwnd(IntPtr.Zero).DpiX, which
            // reads the desktop/primary-monitor DPI and would give a stale scale after the
            // window moves to a differently-scaled monitor - same reasoning as MainForm's own
            // GetIconSize()).
            return DeviceDpi / 96f;
        }
        catch { return 1f; }
    }

    // -- Status bar --

    private void UpdateStatus()
    {
        if (!IsHandleCreated) return;
        var L = LocalizationService.Current;
        BeginInvoke(() =>
        {
            _lblCursor.Text = _vm.CursorInfo;
            _lblSelected.Text = L.GetString("Panel.Selected", _vm.SelectedCount);
            _lblFree.Text = _vm.FreeSpaceDisplay;
        });
    }

    // -- Breadcrumb path bar --

    private void UpdatePathDisplay()
    {
        _pathBar.Text = _vm.CurrentPath;
        RebuildBreadcrumb();
    }

    private void RebuildBreadcrumb()
    {
        _breadcrumbBar.SuspendLayout();
        foreach (Control c in _breadcrumbBar.Controls.Cast<Control>().ToList())
            c.Dispose();

        var currentPath = _vm.CurrentPath;
        if (string.IsNullOrEmpty(currentPath))
        {
            _breadcrumbBar.ResumeLayout();
            return;
        }

        var p = ThemeService.Current;
        var parts = new List<(string display, string fullPath)>();

        if (RemotePath.IsRemote(currentPath))
        {
            // Split on '/' below the root, which is the only separator a remote path has.
            // Path.GetPathRoot below would answer "" for "dav://host/x" - not null, so the
            // existing `?? trimmed` fallback would not catch it - and the bar would start with an
            // empty crumb pointing nowhere.
            var remoteRoot = RemotePath.GetRoot(currentPath);
            parts.Add((remoteRoot, remoteRoot));

            var acc = remoteRoot;
            foreach (var seg in RemotePath.PathOf(currentPath).Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                acc = RemotePath.Combine(acc, seg);
                parts.Add((seg, acc));
            }
        }
        else if (ArchivePath.IsArchivePath(currentPath))
        {
            // Archive paths mix a real path with an internal virtual one — too many
            // edge cases to safely split into clickable segments, so show as-is.
            parts.Add((currentPath.TrimEnd(Path.DirectorySeparatorChar), currentPath));
        }
        else
        {
            var trimmed = currentPath.TrimEnd(Path.DirectorySeparatorChar);
            var root = Path.GetPathRoot(trimmed) ?? trimmed;
            var rootDisplay = root.TrimEnd(Path.DirectorySeparatorChar);
            parts.Add((rootDisplay.Length > 0 ? rootDisplay : root, root));

            var rest = trimmed.Length > root.Length ? trimmed[root.Length..] : string.Empty;
            var acc = root.TrimEnd(Path.DirectorySeparatorChar);
            foreach (var seg in rest.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                acc += Path.DirectorySeparatorChar + seg;
                parts.Add((seg, acc));
            }
        }

        for (int i = 0; i < parts.Count; i++)
        {
            var (display, fullPath) = parts[i];
            var isLast = i == parts.Count - 1;

            var seg = new Label
            {
                Text = display,
                AutoSize = true,
                Margin = new Padding(0, 5, 2, 0),
                Padding = new Padding(4, 2, 4, 2),
                Cursor = Cursors.Hand
            };
            // A role, not a colour and font set here. ControlThemer resets every untagged control
            // to its generic default on each theme switch, so a hand-set colour survives only until
            // the next one - and these crumbs happen to be rebuilt often enough that the breakage
            // was invisible rather than absent. The last segment is where the panel actually is, so
            // it carries the emphasis.
            seg.SetRole(isLast ? ThemeRole.Emphasis : ThemeRole.Body);
            // Applied immediately, not only on the next theme switch. Tagging a freshly built
            // control and walking away leaves it with no colour or font of its own until something
            // re-themes the tree, so it inherits whatever the bar happens to have - which is how a
            // crumb ends up unreadable. ControlThemer is the one place that turns a role into
            // colours, so it is what gets called here too.
            ControlThemer.ThemeSingleControl(seg, p);
            var target = fullPath;
            var canNavigate = !isLast;
            seg.Click += (_, _) =>
            {
                ActivatePanel();
                if (canNavigate) _ = _vm.NavigateAsync(target);
            };
            seg.MouseEnter += (_, _) => seg.BackColor = p.ToolbarHover;
            seg.MouseLeave += (_, _) => seg.BackColor = Color.Transparent;
            _breadcrumbBar.Controls.Add(seg);

            if (!isLast)
            {
                var chevron = new Label
                {
                    Text = "\u203A",
                    AutoSize = true,
                    Margin = new Padding(0, 5, 2, 0),
                };
                // Separator, not Muted: the dimmed colour lands around 3:1 against the header
                // background these sit on, below the 4.5:1 a glyph rendered as text needs.
                chevron.SetRole(ThemeRole.Separator);
                ControlThemer.ThemeSingleControl(chevron, p);
                _breadcrumbBar.Controls.Add(chevron);
            }
        }

        _breadcrumbBar.ResumeLayout();
    }

    private void CommitOrCancelPathEdit(bool commit)
    {
        if (commit)
        {
            var typed = _pathBar.Text?.Trim() ?? string.Empty;
            if (typed.Length > 0 && !string.Equals(typed, _vm.CurrentPath, StringComparison.OrdinalIgnoreCase))
                _ = _vm.NavigateAsync(typed);
        }

        _pathBar.ReadOnly = true;
        _pathBar.Visible = false;
        _breadcrumbBar.Visible = true;
    }

    /// <summary>
    /// Switches the path bar into an editable text field for typing or pasting a path
    /// directly, as an alternative to clicking through the breadcrumb.
    /// </summary>
    public void BeginEditPath()
    {
        ActivatePanel();
        _breadcrumbBar.Visible = false;
        _pathBar.Visible = true;
        _pathBar.ReadOnly = false;
        _pathBar.Text = _vm.CurrentPath;
        _pathBar.Focus();
        _pathBar.SelectAll();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            LocalizationService.Current.LanguageChanged -= OnLanguageChanged;
            DriveCatalog.Instance.Changed -= OnDrivesChanged;
            ConnectionManager.Instance.Changed -= OnDrivesChanged;
            MtpDeviceCatalog.Instance.Changed -= OnDrivesChanged;
            _vm.ItemsChanged -= OnItemsChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _scrollOverlay?.Dispose();
            // ImageList is a Component, not a Control - it's never in the Controls collection, so
            // base.Dispose()'s recursive walk below never reaches it. The context menu built by
            // BuildContextMenu() is not tracked here at all - each shown instance disposes itself
            // via its own Closed handler (see OnFileListMouseDown), so there is nothing left to
            // dispose if the panel closes while a menu happens to be open; it just finishes
            // closing and self-disposing normally, independent of this panel's own teardown.
            _fileImageList?.Dispose();
            _borderPanel?.Dispose();
            _driveBar?.Dispose();
            _fileList?.Dispose();
            _pathBar?.Dispose();
            _breadcrumbBar?.Dispose();
            // _tabStrip has OwnsPageContent=false, so disposing it never touches
            // _tabStripDummyContent (its pages' shared, never-shown content) - dispose that
            // separately. _addTabButton is the strip's trailing control, not a page's content;
            // ThemedTabControl.Dispose() doesn't own it either, so it needs disposing here too.
            _tabStrip?.Dispose();
            _tabStripDummyContent?.Dispose();
            _addTabButton?.Dispose();
            _addTabTooltip.Dispose();
            _quickViewDebounce?.Stop();
            _quickViewDebounce?.Dispose();
            DisposeQuickViewHost();
            _filterBar?.Dispose();
            _filterBox?.Dispose();
            _filterLabel?.Dispose();
            _statusStrip?.Dispose();
            _lblCursor?.Dispose();
            _lblSelected?.Dispose();
            _lblFree?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>See <see cref="ListViewScrollbarOverlay.Reposition"/> - called by
    /// <see cref="MainForm.OnDpiChanged"/> for both panels after a DPI-monitor change.</summary>
    public void RefreshScrollbarOverlay() => _scrollOverlay?.Reposition();
}

/// <summary>
/// A drive bar button's identity (immutable) plus the two flags <see cref="FilePanelUserControl.UpdateDriveBarHighlight"/>
/// recomputes whenever the current path or the panel's active/inactive state changes. Stored in
/// <see cref="ToolStripItem.Tag"/> instead of a delimited string, which used to get overwritten
/// with "{root}|{isCurrent}|{dimmed}" and then re-parsed via Path.GetPathRoot() on that same
/// mangled string the next time around - fine for local drives, broken for UNC roots.
/// </summary>
internal sealed class DriveButtonState
{
    public DriveButtonState(string rootPath) => RootPath = rootPath;
    public string RootPath { get; }
    public bool IsCurrent { get; set; }
    public bool Dimmed { get; set; }
}

/// <summary>
/// Custom ToolStripRenderer that paints drive buttons with rounded corners and gradient,
/// matching the RoundedButton style from the original drive bar.
/// </summary>
internal sealed class DriveBarRenderer : ToolStripProfessionalRenderer
{
    private static ThemePalette P => ThemeService.Current;

    public DriveBarRenderer() : base(new DriveBarColorTable()) { }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var bg = new SolidBrush(P.HeaderBackground);
        e.Graphics.FillRectangle(bg, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
        {
            using var pen = new Pen(P.GridLine, 1f);
            e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

        var item = e.Item;
        var rect = new Rectangle(1, 1, item.Width - 3, item.Height - 3);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var isCurrent = false;
        var dimmed = false;
        if (item.Tag is DriveButtonState state)
        {
            isCurrent = state.IsCurrent;
            dimmed = state.Dimmed;
        }

        Color baseColor;
        Color foreColor;
        if (isCurrent)
        {
            baseColor = dimmed ? ThemeService.DimColor(P.Accent, 92) : P.Accent;
            foreColor = P.SelectionForeground;
        }
        else
        {
            baseColor = item.Selected
                ? (dimmed ? ThemeService.DimColor(P.ToolbarHover, 92) : P.ToolbarHover)
                : (dimmed ? ThemeService.DimColor(P.HeaderBackground, 92) : P.HeaderBackground);
            foreColor = dimmed ? ThemeService.DimColor(P.HeaderForeground, 92) : P.HeaderForeground;
        }

        var topColor = ControlPaint.Light(baseColor, 0.08f);
        var bottomColor = ControlPaint.Dark(baseColor, 0.04f);

        int radius = 5;
        using var path = GraphicsHelpers.GetRoundedRect(rect, radius);

        using (var gradBrush = new System.Drawing.Drawing2D.LinearGradientBrush(rect, topColor, bottomColor, 90f))
            g.FillPath(gradBrush, path);

        if (!isCurrent && item.Selected)
        {
            using var borderPen = new Pen(Color.FromArgb(120, P.Accent), 1f);
            g.DrawPath(borderPen, path);
        }

        if (!item.Pressed)
        {
            var hlRect = new Rectangle(rect.X + radius / 2, rect.Y + 1, rect.Width - radius, rect.Height / 2 - 1);
            using var hlBrush = new SolidBrush(P.GlossOverlay);
            using var hlPath = GraphicsHelpers.GetRoundedRect(hlRect, Math.Max(0, radius - 1));
            g.FillPath(hlBrush, hlPath);
        }

        var textRect = new Rectangle(
            item.Padding.Left + 2,
            item.Padding.Top,
            item.Width - item.Padding.Left - item.Padding.Right - 4,
            item.Height - item.Padding.Top - item.Padding.Bottom);

        var font = item.Font ?? P.StatusBarFont;

        if (item.Image != null && !string.IsNullOrEmpty(item.Text))
        {
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var textW = TextRenderer.MeasureText(g, item.Text, font, new Size(200, item.Height), flags).Width;
            var totalW = item.Image.Width + 4 + textW;

            if (totalW <= textRect.Width)
            {
                var startX = textRect.X + (textRect.Width - totalW) / 2;
                var imgY = textRect.Y + (textRect.Height - item.Image.Height) / 2;
                g.DrawImage(item.Image, startX, imgY, item.Image.Width, item.Image.Height);
                var tRect = new Rectangle(startX + item.Image.Width + 4, textRect.Y, textW + 2, textRect.Height);
                TextRenderer.DrawText(g, item.Text, font, tRect, foreColor, flags);
            }
            else
            {
                var imgY = textRect.Y + (textRect.Height - item.Image.Height) / 2;
                g.DrawImage(item.Image, textRect.X + 2, imgY, item.Image.Width, item.Image.Height);
                var tRect = new Rectangle(textRect.X + item.Image.Width + 6, textRect.Y, textRect.Width - item.Image.Width - 10, textRect.Height);
                TextRenderer.DrawText(g, item.Text, font, tRect, foreColor, flags);
            }
        }
        else if (item.Image != null)
        {
            var imgX = textRect.X + (textRect.Width - item.Image.Width) / 2;
            var imgY = textRect.Y + (textRect.Height - item.Image.Height) / 2;
            g.DrawImage(item.Image, imgX, imgY, item.Image.Width, item.Image.Height);
        }
        else
        {
            var centerFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, item.Text, font, textRect, foreColor, centerFlags);
        }
    }

}

internal sealed class DriveBarColorTable : ProfessionalColorTable
{
    private static ThemePalette P => ThemeService.Current;
    public override Color ToolStripDropDownBackground => P.HeaderBackground;
    public override Color MenuBorder => P.GridLine;
    public override Color MenuItemSelected => P.ToolbarHover;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color SeparatorDark => P.GridLine;
    public override Color SeparatorLight => P.GridLine;
}