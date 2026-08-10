using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.ViewModels;
using CoderCommander.WinForms;
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
    private readonly PanelViewModel _vm;

    private Panel _borderPanel = null!;
    private TextBox _pathBar = null!;
    private FlowLayoutPanel _breadcrumbBar = null!;
    private ListView _fileList = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _lblCursor = null!;
    private ToolStripStatusLabel _lblSelected = null!;
    private ToolStripStatusLabel _lblFree = null!;
    private ContextMenuStrip _ctxMenu = null!;
    private ImageList _fileImageList = null!;
    private ToolStrip _driveBar = null!;
    private ListViewScrollbarOverlay? _scrollOverlay;

    private readonly List<ToolStripButton> _driveButtons = new();

    private bool _suppressSelectionEvent;
    private bool _updatingItems;
    private ListViewItem? _hoveredItem;
    private bool _showExtensionInName = true;

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

    /// <summary>Raised when files are dropped onto this panel via drag &amp; drop.</summary>
    public event EventHandler<PanelDropEventArgs>? ItemsDropped;

    /// <summary>Raised when the user tries to enter a recognized archive file (double-click or Enter).</summary>
    public event EventHandler<FileSystemItem>? ArchiveEntered;

    /// <summary>Raised when a connection button in the places bar is clicked. Carries the
    /// profile id; MainForm decides whether that means connect, enter, or retry, because only
    /// it can swap the panel's file system and show a dialog.</summary>
    public event EventHandler<Guid>? ConnectionActivated;

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

    /// <summary>Creates the panel UI and wires up the ViewModel, context menu and drag-and-drop.</summary>
    /// <param name="vm">ViewModel providing data for this panel.</param>
    public FilePanelUserControl(PanelViewModel vm)
    {
        _vm = vm;
        _showExtensionInName = SettingsService.Load().ShowExtensionInName;
        Dock = DockStyle.Fill;
        BackColor = ThemeService.Current.PanelBackground;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        DoubleBuffered = true;

        BuildControls();
        BuildContextMenu();
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
            HeaderStyle = ColumnHeaderStyle.Clickable
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

        content.Controls.Add(_fileList);
        content.Controls.Add(_driveBar);
        content.Controls.Add(_pathBar);
        content.Controls.Add(_breadcrumbBar);
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

    private void OnItemsChanged(object? sender, EventArgs e)
    {
        if (!IsHandleCreated) return;
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

    private void RebuildList()
    {
        _updatingItems = true;
        _suppressSelectionEvent = true;

        _fileList.BeginUpdate();
        _fileList.Items.Clear();

        var selName = _vm.SelectedItem?.Name;

        foreach (var item in _vm.Items)
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

            // Restore selection state from model
            if (item.IsSelected)
                lvi.Selected = true;

            _fileList.Items.Add(lvi);

            // Restore focus
            if (selName != null && item.Name == selName)
            {
                lvi.Selected = true;
                lvi.Focused = true;
            }
        }

        _fileList.EndUpdate();
        _suppressSelectionEvent = false;
        _updatingItems = false;
        UpdateStatus();
    }

    private void SyncSelectionFromVm()
    {
        if (_suppressSelectionEvent || _updatingItems) return;
        var target = _vm.SelectedItem;
        if (target == null) return;

        foreach (ListViewItem lvi in _fileList.Items)
        {
            if (lvi.Tag is FileSystemItem item && item == target)
            {
                _suppressSelectionEvent = true;
                lvi.Selected = true;
                lvi.Focused = true;
                lvi.EnsureVisible();
                _suppressSelectionEvent = false;
                break;
            }
        }
    }

    /// <summary>
    /// Re-syncs every row's visual .Selected state from item.IsSelected. Needed after bulk
    /// selection changes (SelectAll/DeselectAll/InvertSelection/pattern select) which mutate
    /// the model directly without going through the ListView's own selection events.
    /// </summary>
    private void SyncAllSelectionFromModel()
    {
        if (_updatingItems) return;
        _suppressSelectionEvent = true;
        foreach (ListViewItem lvi in _fileList.Items)
        {
            if (lvi.Tag is FileSystemItem item)
                lvi.Selected = item.IsSelected;
        }
        _suppressSelectionEvent = false;
        UpdateStatus();
    }

    // -- Event handlers --

    private void OnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressSelectionEvent || _updatingItems) return;

        // Sync ListView.SelectedItems back to model IsSelected
        foreach (ListViewItem lvi in _fileList.Items)
        {
            if (lvi.Tag is FileSystemItem item)
            {
                item.IsSelected = lvi.Selected;
            }
        }
        _vm.NotifySelectionChanged();

        // Update cursor item
        if (_fileList.SelectedItems.Count > 0 && _fileList.FocusedItem != null)
        {
            if (_fileList.FocusedItem.Tag is FileSystemItem item)
            {
                _vm.SelectedItem = item;
            }
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
            else if (HasNativePaths && ArchiveFormatRegistry.FromExtension(item.FullPath) != null)
            {
                ArchiveEntered?.Invoke(this, item);
            }
            else
            {
                ItemActivated?.Invoke(this, item);
            }
        }
    }

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
            else if (HasNativePaths && ArchiveFormatRegistry.FromExtension(enterItem.FullPath) != null)
                ArchiveEntered?.Invoke(this, enterItem);
            else
                ItemActivated?.Invoke(this, enterItem);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void OnFileListMouseDown(object? sender, MouseEventArgs e)
    {
        // Right-click selects the item under cursor and shows context menu
        if (e.Button == MouseButtons.Right)
        {
            var info = _fileList.HitTest(e.Location);
            if (info.Item != null)
            {
                _suppressSelectionEvent = true;
                _fileList.SelectedItems.Clear();
                info.Item.Selected = true;
                info.Item.Focused = true;
                _suppressSelectionEvent = false;
                if (info.Item.Tag is FileSystemItem fsItem)
                    _vm.SelectedItem = fsItem;
            }
            _ctxMenu.Show(_fileList, e.Location);
        }
    }

    private void OnFileListMouseMove(object? sender, MouseEventArgs e)
    {
        var info = _fileList.HitTest(e.Location);
        var newHovered = info.Item;
        if (!ReferenceEquals(_hoveredItem, newHovered))
        {
            var oldHovered = _hoveredItem;
            _hoveredItem = newHovered;
            if (oldHovered != null && oldHovered.Index >= 0)
                _fileList.Invalidate(new Rectangle(0, oldHovered.Bounds.Top, _fileList.ClientSize.Width, oldHovered.Bounds.Height));
            if (_hoveredItem != null && _hoveredItem.Index >= 0)
                _fileList.Invalidate(new Rectangle(0, _hoveredItem.Bounds.Top, _fileList.ClientSize.Width, _hoveredItem.Bounds.Height));
        }
    }

    private void OnFileListMouseLeave(object? sender, EventArgs e)
    {
        if (_hoveredItem != null)
        {
            var oldHovered = _hoveredItem;
            _hoveredItem = null;
            if (oldHovered.Index >= 0)
                _fileList.Invalidate(new Rectangle(0, oldHovered.Bounds.Top, _fileList.ClientSize.Width, oldHovered.Bounds.Height));
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

    // -- Drag & Drop --

    private void OnFileListItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        var items = new List<FileSystemItem>();
        foreach (ListViewItem lvi in _fileList.SelectedItems)
        {
            if (lvi.Tag is FileSystemItem item && !item.IsParent)
                items.Add(item);
        }

        if (items.Count == 0) return;

        // The internal payload survives virtual paths, which the shell FileDrop format cannot
        // carry - not archive ones and not remote ones. Handing Explorer a "dav://host/f.txt"
        // through FileDrop would announce a file that does not exist at that path.
        var data = new DataObject();
        data.SetData(PanelDragPayload.Format, new PanelDragPayload(this, items));

        var shellPaths = HasNativePaths
            ? items.Where(i => !VfsPath.IsArchive(i.FullPath) && !RemotePath.IsRemote(i.FullPath))
                   .Select(i => i.FullPath)
                   .ToArray()
            : [];
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

    private ListViewItem? _dropHighlight;

    private void HighlightDropTarget(FileSystemItem? folder)
    {
        ListViewItem? target = null;
        if (folder != null)
        {
            foreach (ListViewItem lvi in _fileList.Items)
            {
                if (ReferenceEquals(lvi.Tag, folder)) { target = lvi; break; }
            }
        }

        if (ReferenceEquals(_dropHighlight, target)) return;

        _dropHighlight = target;
        RefreshItemColors();

        if (target != null)
        {
            target.BackColor = ThemeService.Current.Selection;
            target.ForeColor = ThemeService.Current.SelectionForeground;
        }
    }

    // -- Context menu --

    private void BuildContextMenu()
    {
        var L = LocalizationService.Current;
        _ctxMenu = new ContextMenuStrip
        {
            BackColor = ThemeService.Current.HeaderBackground,
            ForeColor = ThemeService.Current.Foreground,
            Font = ThemeService.Current.GridFont,
            ImageScalingSize = new Size(16, 16),
            Renderer = new ThemeRenderer()
        };

        CtxItem("Ctx.View", "view", () => { if (_vm.SelectedItem is { IsParent: false } item) ItemActivated?.Invoke(this, item); });
        CtxItem("Ctx.Edit", "edit", () => { if (_vm.SelectedItem is { IsParent: false } item) EditRequested?.Invoke(this, item); });
        _ctxMenu.Items.Add(new ToolStripSeparator());
        CtxItem("Ctx.Copy", "copy", () => CopyRequested?.Invoke(this, EventArgs.Empty));
        CtxItem("Ctx.Move", "move", () => MoveRequested?.Invoke(this, EventArgs.Empty));
        CtxItem("Ctx.Rename", "rename", () => RenameRequested?.Invoke(this, EventArgs.Empty));
        CtxItem("Ctx.Delete", "delete", () => DeleteRequested?.Invoke(this, EventArgs.Empty));
        _ctxMenu.Items.Add(new ToolStripSeparator());
        CtxItem("Ctx.Properties", "properties", () => PropertiesRequested?.Invoke(this, EventArgs.Empty));

        // Copy path submenu
        var copyPathMenu = new ToolStripMenuItem(L.GetString("Ctx.CopyPath"), ToolbarIcons.Get("copy"));
        var cpFull = new ToolStripMenuItem(L.GetString("Ctx.CopyPath.Full"), null, (_, _) => CopyToClipboard(_vm.SelectedItem?.FullPath ?? ""));
        var cpName = new ToolStripMenuItem(L.GetString("Ctx.CopyPath.Name"), null, (_, _) => CopyToClipboard(_vm.SelectedItem?.Name ?? ""));
        copyPathMenu.DropDownItems.AddRange([cpFull, cpName]);
        _ctxMenu.Items.Add(copyPathMenu);

        _ctxMenu.Items.Add(new ToolStripSeparator());
        CtxItem("Ctx.SelectAll", "selectall", () => _vm.SelectAll());
        CtxItem("Ctx.InvertSelection", "invert", () => _vm.InvertSelection());
    }

    private void CtxItem(string key, string iconKey, Action action)
    {
        var L = LocalizationService.Current;
        var item = new ToolStripMenuItem(L.GetString(key), ToolbarIcons.Get(iconKey));
        item.Click += (_, _) => action();
        _ctxMenu.Items.Add(item);
    }

    private static void CopyToClipboard(string text)
    {
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
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

        // Rebuild context menu
        _ctxMenu.Items.Clear();
        BuildContextMenu();
    }

    // -- Drive bar --

    private void PopulateDriveBar()
    {
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

        AddConnectionButtons(toolbarScale, btnHeight);
        UpdateDriveBarDim();
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
                Tag = status.ProfileId,
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
        }
    }

    private void UpdateDriveBarHighlight()
    {
        var currentRoot = Path.GetPathRoot(_vm.CurrentPath);
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
        var isDropTarget = ReferenceEquals(_dropHighlight, lvi);
        var isHovered = _hoveredItem == lvi;

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
        _fileList.BackColor = p.PanelBackground;
        _fileList.ForeColor = p.Foreground;
        _fileList.Font = p.GridFont;
        NativeControlThemer.ThemeListView(_fileList);
        ThemeService.StyleStatusStrip(_statusStrip);
        PopulateFileImageList();
        PopulateDriveBar();
        RebuildList();
        ApplyContextMenuTheme();
    }

    /// <summary>
    /// _ctxMenu's colors used to be set once, at BuildContextMenu() time, and never touched
    /// again by ApplyTheme() - only Relocalize() (which rebuilds the menu from scratch for
    /// language changes) happened to also pick up the current theme. A theme switch with no
    /// language change left its item text at the old theme's color.
    /// </summary>
    private void ApplyContextMenuTheme()
    {
        var p = ThemeService.Current;
        _ctxMenu.BackColor = p.HeaderBackground;
        _ctxMenu.ForeColor = p.Foreground;
        _ctxMenu.Font = p.GridFont;
        ControlThemer.ThemeToolStripItems(_ctxMenu.Items, p);
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
                ForeColor = isLast ? p.Foreground : p.HeaderForeground,
                Font = p.GridFont,
                Cursor = Cursors.Hand
            };
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
                    ForeColor = p.DimForeground,
                    Font = p.GridFont
                };
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
            _vm.ItemsChanged -= OnItemsChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _scrollOverlay?.Dispose();
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
        var path = GraphicsHelpers.GetRoundedRect(rect, radius);

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

        path.Dispose();

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