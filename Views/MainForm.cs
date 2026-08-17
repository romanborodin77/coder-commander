using CoderCommander.Archives;
using CoderCommander.Commands;
using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Operations;
using CoderCommander.Services;
using CoderCommander.ViewModels;
using CoderCommander.WinForms;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CoderCommander.Views;

/// <summary>
/// Main application form: menu, toolbar, two file panels, command line, function buttons, status bar.
/// </summary>
public sealed class MainForm : Form
{
    private readonly MainViewModel _vm;

    private MenuStrip _menuStrip = null!;
    private ToolStrip _toolStrip = null!;
    private SplitContainer _mainSplit = null!;
    private FilePanelUserControl _leftPanel = null!;
    private FilePanelUserControl _rightPanel = null!;
    private ToolStrip _functionBar = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _lblStatus = null!;
    private ToolStripStatusLabel _lblQueue = null!;
    private Panel _splitterOverlay = null!;
    private bool _splitterDragging;
    /// <summary>Listens for volume arrival/removal so the panels' drive bars stay honest. Lives
    /// here because volume broadcasts go to top-level windows, and because one watcher shared by
    /// both panels means one probe pass per device change instead of two racing ones.</summary>
    private readonly DeviceChangeWatcher _deviceWatcher = new();
    /// <summary>Fraction of _mainSplit's width where the splitter sits, 0.5 (centered) until the
    /// user drags it. OnFormResize uses this to preserve the chosen proportion instead of
    /// recentering the panels on every window resize.</summary>
    private double _splitRatio = 0.5;
    private EmbeddedTerminalPanel _terminalPanel = null!;
    private Splitter _terminalSplitter = null!;
    private int _terminalHeight = 250;
    private bool _terminalVisible;
    /// <summary>Guards the "OnOpen" <c>TerminalFollowPanelCwd</c> setting - reset to false each
    /// time the terminal panel becomes visible; see <see cref="PushActivePathToTerminal"/>.</summary>
    private bool _terminalFollowedOnceSinceOpen;
    /// <summary>Cached value of <see cref="AppSettings.TerminalFollowPanelCwd"/> to avoid a
    /// <see cref="SettingsService.Load"/> call on every panel navigation.</summary>
    private string _cachedTerminalFollow = "OnOpen";

    // Menu items that need re-localization
    private readonly List<Action> _relocalizeActions = new();

    /// <summary>Creates the main application window with all menus, toolbars, panels and terminal.</summary>
    /// <param name="vm">Application ViewModel that owns both panels, commands and operations.</param>
    public MainForm(MainViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));

        var settings = SettingsService.Load();
        Text = LocalizationService.Current.GetString("About.AppName");
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Font = ThemeService.Current.GridFont;
        DoubleBuffered = true;
        _terminalHeight = settings.TerminalHeight; // read before BuildTerminalPanel() sizes the panel

        // Build all controls (order of Controls.Add determines z-order:
        // first added = lowest z-index = docks last = innermost)
        BuildMenu();
        BuildToolbar();
        BuildMainArea();
        BuildFunctionButtons();
        BuildStatusBar();
        BuildTerminalPanel();
        WireEvents();

        // WinForms docks from HIGHEST index down to 0.
        // Fill must be at index 0 (docks LAST = gets remaining space).
        // Among Top: higher index = outermost. Among Bottom: higher index = outermost.
        Controls.SetChildIndex(_mainSplit, 0);
        Controls.SetChildIndex(_toolStrip, 1);
        Controls.SetChildIndex(_terminalSplitter, 2);
        Controls.SetChildIndex(_terminalPanel, 3);
        Controls.SetChildIndex(_functionBar, 4);
        Controls.SetChildIndex(_statusStrip, 5);

        ApplyTheme();
        ApplyVisibility();

        // Restored once here in the constructor, not via ApplyVisibility() - that method
        // also re-runs after the Settings dialog closes against a possibly-stale cached
        // AppSettings, which would silently undo a mid-session F9 toggle.
        _terminalVisible = settings.TerminalVisible;
        _terminalPanel.Visible = _terminalVisible;
        _terminalSplitter.Visible = _terminalVisible;
        _cachedTerminalFollow = settings.TerminalFollowPanelCwd;

        if (settings.WindowMaximized)
            WindowState = FormWindowState.Maximized;

        LocalizationService.Current.LanguageChanged += (_, _) => Relocalize();

        // MainForm isn't a ThemedForm, so unlike every dialog in the app it doesn't pick up
        // ThemeService.ThemeChanged automatically - it currently only re-themes via the
        // hand-wired _vm.ThemeChanged and SettingsForm's SettingsSaved paths (see WireEvents /
        // OnSettingsSaved). Subscribing directly here means any future caller of
        // ThemeService.ApplyTheme also reaches the main window, not just those two call sites.
        ThemeService.ThemeChanged += OnGlobalThemeChanged;
    }

    private void OnGlobalThemeChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated)
            BeginInvoke(ApplyTheme);
    }

    /// <summary>Unsubscribes from global theme-changed events.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ThemeService.ThemeChanged -= OnGlobalThemeChanged;
        base.Dispose(disposing);
    }

    /// <summary>Applies the dark title bar theme after the native window handle is created.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeControlThemer.ApplyDarkTitleBar(Handle);
    }

    // ═══════════════════════════════════════════
    // MENU (fully localized, built first)
    // ═══════════════════════════════════════════

    private void BuildMenu()
    {
        _menuStrip = new MenuStrip
        {
            Dock = DockStyle.Top,
            ImageScalingSize = new Size(16, 16)
        };

        BuildFileMenu();
        BuildSelectionMenu();
        BuildCommandsMenu();
        BuildViewMenu();
        BuildConfigMenu();
        BuildHelpMenu();

        Controls.Add(_menuStrip);
        MainMenuStrip = _menuStrip;
    }

    private void BuildFileMenu()
    {
        var L = LocalizationService.Current;
        var m = new ToolStripMenuItem(L.GetString("Menu.File"));

        m.DropDownItems.Add(Mi("Menu.File.View", "view", "F3", CommandIds.View));
        m.DropDownItems.Add(Mi("Menu.File.Edit", "edit", "F4", CommandIds.Edit));
        m.DropDownItems.Add(Mi("Menu.File.EditNew", "editnew", "Shift+F4", null, () => OpenEditorNew()));
        m.DropDownItems.Add(new ToolStripSeparator());
        m.DropDownItems.Add(Mi("Menu.File.Copy", "copy", "F5", CommandIds.Copy));
        m.DropDownItems.Add(Mi("Menu.File.Move", "move", "F6", CommandIds.Move));
        m.DropDownItems.Add(Mi("Menu.File.Rename", "rename", "F2", CommandIds.Rename));
        m.DropDownItems.Add(Mi("Menu.File.CreateFolder", "newdir", "F7", CommandIds.MakeDir));
        m.DropDownItems.Add(Mi("Menu.File.Delete", "delete", "F8", CommandIds.Delete));
        m.DropDownItems.Add(Mi("Menu.File.Wipe", "wipe", "Shift+F8", CommandIds.Wipe));
        m.DropDownItems.Add(new ToolStripSeparator());
        m.DropDownItems.Add(Mi("Menu.File.Pack", "pack", "Alt+F5", CommandIds.PackFiles));
        m.DropDownItems.Add(Mi("Menu.File.Extract", "extract", "Alt+F9", CommandIds.UnpackFiles));
        m.DropDownItems.Add(new ToolStripSeparator());
        m.DropDownItems.Add(Mi("Menu.File.Split", "split", "", CommandIds.SplitFile));
        m.DropDownItems.Add(Mi("Menu.File.Combine", "combine", "", CommandIds.CombineFiles));
        m.DropDownItems.Add(new ToolStripSeparator());
        m.DropDownItems.Add(Mi("Menu.File.Properties", "properties", "Alt+Enter", CommandIds.ShowProperties));
        m.DropDownItems.Add(new ToolStripSeparator());
        m.DropDownItems.Add(Mi("Menu.File.Exit", "exit", "Alt+X", CommandIds.Exit));

        _menuStrip.Items.Add(m);
    }

    private void BuildSelectionMenu()
    {
        var L = LocalizationService.Current;
        var m = new ToolStripMenuItem(L.GetString("Menu.Selection"));

        m.DropDownItems.Add(Mi("Menu.Selection.All", "selectall", "Ctrl+A", CommandIds.SelectAll));
        m.DropDownItems.Add(Mi("Menu.Selection.None", "deselectall", "Ctrl+D", CommandIds.DeselectAll));
        m.DropDownItems.Add(Mi("Menu.Selection.Invert", "invert", "Num+", CommandIds.InvertSelection));

        _menuStrip.Items.Add(m);
    }

    private void BuildCommandsMenu()
    {
        var L = LocalizationService.Current;
        var m = new ToolStripMenuItem(L.GetString("Menu.Commands"));

        m.DropDownItems.Add(Mi("Menu.Commands.Search", "search", "Alt+F7", CommandIds.FindFiles));
        m.DropDownItems.Add(Mi("Menu.Commands.MultiRename", "multirename", "Ctrl+M", CommandIds.MultiRename));
        m.DropDownItems.Add(new ToolStripSeparator());
        m.DropDownItems.Add(Mi("Menu.Commands.SyncDirs", "syncdirs", "", null, () => OnSyncDirs(this, (_vm.LeftPanel.CurrentPath, _vm.RightPanel.CurrentPath))));
        m.DropDownItems.Add(Mi("Menu.Commands.SwapPanels", "syncdirs", "Ctrl+U", CommandIds.SwapPanels));
        m.DropDownItems.Add(Mi("Menu.Commands.SyncPanels", "syncdirs", "", CommandIds.TargetEqualSource));
        m.DropDownItems.Add(new ToolStripSeparator());
        m.DropDownItems.Add(Mi("Menu.Commands.DirTree", "view", "", null, () => OpenDirectoryTree()));
        m.DropDownItems.Add(Mi("Menu.Commands.Terminal", "terminal", "F9", CommandIds.ToggleTerminal));
        m.DropDownItems.Add(new ToolStripSeparator());
        m.DropDownItems.Add(Mi("Menu.Commands.Checksum", "properties", "", CommandIds.Checksum));
        m.DropDownItems.Add(Mi("Menu.Commands.CalculateFolderSize", "properties", "Ctrl+Alt+Space", CommandIds.CalculateFolderSize));
        m.DropDownItems.Add(Mi("Menu.Commands.Differ", "view", "", null, () => OpenDiffer()));
        m.DropDownItems.Add(new ToolStripSeparator());
        m.DropDownItems.Add(Mi("Menu.Commands.OpQueue", "settings", "", null, () => OpenOperationQueue()));

        _menuStrip.Items.Add(m);
    }

    private void BuildViewMenu()
    {
        var L = LocalizationService.Current;
        var m = new ToolStripMenuItem(L.GetString("Menu.View"));

        var themeMenu = new ToolStripMenuItem(L.GetString("Menu.View.Theme"));
        themeMenu.DropDownItems.Add(Mi("Menu.View.Theme.Dark", "settings", "", CommandIds.SetTheme, null, "Dark"));
        themeMenu.DropDownItems.Add(Mi("Menu.View.Theme.Light", "settings", "", CommandIds.SetTheme, null, "Light"));
        m.DropDownItems.Add(themeMenu);
        _relocalizeActions.Add(() => themeMenu.Text = L.GetString("Menu.View.Theme"));

        var langMenu = new ToolStripMenuItem(L.GetString("Menu.View.Language"));
        langMenu.DropDownItems.Add(L.GetString("Language.English"), null, (_, _) => LocalizationService.Current.LoadLanguage("en"));
        langMenu.DropDownItems.Add(L.GetString("Language.Russian"), null, (_, _) => LocalizationService.Current.LoadLanguage("ru"));
        m.DropDownItems.Add(langMenu);
        _relocalizeActions.Add(() => langMenu.Text = L.GetString("Menu.View.Language"));

        var sortMenu = new ToolStripMenuItem(L.GetString("Menu.View.Sort"));
        sortMenu.DropDownItems.Add(Mi("Menu.View.Sort.Name", "settings", "", CommandIds.SetSortColumn, null, "Name"));
        sortMenu.DropDownItems.Add(Mi("Menu.View.Sort.Extension", "settings", "", CommandIds.SetSortColumn, null, "Extension"));
        sortMenu.DropDownItems.Add(Mi("Menu.View.Sort.Size", "settings", "", CommandIds.SetSortColumn, null, "Size"));
        sortMenu.DropDownItems.Add(Mi("Menu.View.Sort.Modified", "settings", "", CommandIds.SetSortColumn, null, "Modified"));
        sortMenu.DropDownItems.Add(new ToolStripSeparator());
        sortMenu.DropDownItems.Add(Mi("Menu.View.Sort.DirsFirst", "settings", "", CommandIds.SetDirectoriesFirst));
        sortMenu.DropDownItems.Add(Mi("Menu.View.Sort.Descending", "settings", "", CommandIds.SetSortDescending));
        m.DropDownItems.Add(sortMenu);
        _relocalizeActions.Add(() => sortMenu.Text = L.GetString("Menu.View.Sort"));

        m.DropDownItems.Add(new ToolStripSeparator());
        m.DropDownItems.Add(Mi("Menu.View.Hidden", "view", "Ctrl+.", CommandIds.ToggleHidden));
        m.DropDownItems.Add(Mi("Menu.View.ShowExtInName", "view", "", CommandIds.ToggleShowExtensionInName));
        m.DropDownItems.Add(Mi("Menu.View.Refresh", "refresh", "Ctrl+R", CommandIds.Refresh));
        m.DropDownItems.Add(Mi("Menu.View.RefreshDrives", "drive", "Ctrl+Shift+R", CommandIds.RefreshDrives));

        _menuStrip.Items.Add(m);
    }

    private void BuildConfigMenu()
    {
        var L = LocalizationService.Current;
        var m = new ToolStripMenuItem(L.GetString("Menu.Config"));

        m.DropDownItems.Add(Mi("Menu.Config.Settings", "settings", "", null, () => OpenSettings()));
        m.DropDownItems.Add(new ToolStripSeparator());
        m.DropDownItems.Add(Mi("Menu.Config.Bookmarks", "bookmarks", "", null, () => OpenBookmarks()));
        m.DropDownItems.Add(Mi("Conn.Title", "connection", "", null, () => OpenConnections()));

        _menuStrip.Items.Add(m);
    }

    private void BuildHelpMenu()
    {
        var L = LocalizationService.Current;
        var m = new ToolStripMenuItem(L.GetString("Menu.Help"));
        m.DropDownItems.Add(Mi("Menu.Help.About", "view", "", CommandIds.About));
        _menuStrip.Items.Add(m);
    }

    private ToolStripMenuItem Mi(string textKey, string iconKey, string shortcut,
        string? commandId, Action? customAction = null, string? param = null)
    {
        var L = LocalizationService.Current;
        var item = new ToolStripMenuItem(L.GetString(textKey), ToolbarIcons.Get(iconKey))
        {
            Tag = iconKey  // Store icon key for theme refresh
        };

        if (!string.IsNullOrEmpty(shortcut))
            item.ShortcutKeyDisplayString = shortcut;

        if (commandId != null)
            item.Click += (_, _) => _vm.Commands.Execute(commandId, param);
        else if (customAction != null)
            item.Click += (_, _) => customAction();

        // Register for re-localization
        _relocalizeActions.Add(() => item.Text = L.GetString(textKey));

        return item;
    }

    // ═══════════════════════════════════════════
    // TOOLBAR (icons + localized tooltips)
    // ═══════════════════════════════════════════

    private void BuildToolbar()
    {
        // Вычисляем размер иконок с учётом DPI
        var iconSize = GetIconSize();

        var toolbarScale = DeviceDpi / 96f;
        _toolStrip = new ToolStrip
        {
            Dock = DockStyle.Top,
            ImageScalingSize = new Size(iconSize, iconSize),
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(8, 3, 8, 3),
            AutoSize = false,
            Height = (int)Math.Round(38 * toolbarScale),
            Renderer = new ThemeRenderer()
        };

        _toolStrip.Items.Add(TbBtn("Toolbar.Back", "back", () => _ = _vm.ActivePanel.GoBackAsync()));
        _toolStrip.Items.Add(TbBtn("Toolbar.Forward", "forward", () => _ = _vm.ActivePanel.GoForwardAsync()));
        _toolStrip.Items.Add(TbBtn("Toolbar.Up", "up", () => _ = _vm.ActivePanel.GoToParentAsync()));
        _toolStrip.Items.Add(new ToolStripSeparator { Margin = new Padding(6, 4, 6, 4) });
        _toolStrip.Items.Add(TbBtn("Toolbar.Copy", "copy", () => _vm.Commands.Execute(CommandIds.Copy)));
        _toolStrip.Items.Add(TbBtn("Toolbar.Move", "move", () => _vm.Commands.Execute(CommandIds.Move)));
        _toolStrip.Items.Add(TbBtn("Toolbar.Delete", "delete", () => _vm.Commands.Execute(CommandIds.Delete)));
        _toolStrip.Items.Add(TbBtn("Toolbar.NewDir", "newdir", () => _vm.Commands.Execute(CommandIds.MakeDir)));
        _toolStrip.Items.Add(new ToolStripSeparator { Margin = new Padding(6, 4, 6, 4) });
        _toolStrip.Items.Add(TbBtn("Toolbar.Search", "search", () => _vm.Commands.Execute(CommandIds.FindFiles)));
        _toolStrip.Items.Add(TbBtn("Toolbar.Refresh", "refresh", () => _vm.Commands.Execute(CommandIds.Refresh)));
        _toolStrip.Items.Add(new ToolStripSeparator { Margin = new Padding(6, 4, 6, 4) });
        _toolStrip.Items.Add(TbBtn("Toolbar.Settings", "settings", () => OpenSettings()));

        Controls.Add(_toolStrip);
    }

    private int GetIconSize()
    {
        // DeviceDpi is per-monitor aware (csproj sets ApplicationHighDpiMode=PerMonitorV2) and
        // updates on DpiChanged — unlike the old Graphics.FromImage(new Bitmap(1,1))
        // trick, which always read the bitmap's own fixed 96 DPI metadata.
        var scale = DeviceDpi / 96f;
        return (int)Math.Round(16 * scale);
    }

    /// <summary>Regenerates toolbar icons at the new DPI scale and repositions scrollbar overlays.</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // Toolbar/menu icons are rasterized at a fixed pixel size and cached (ToolbarIcons),
        // so moving the window to a monitor with a different DPI would otherwise leave them
        // blurry (WinForms just stretches the old bitmaps) until the next unrelated cache
        // invalidation (a theme switch). Re-fetching after clearing the cache regenerates them
        // at the new DeviceDpi-derived size.
        WinForms.ToolbarIcons.ClearCache();
        WinForms.FileIcons.ClearCache();
        RefreshToolbarIconsForDpi();

        // WinForms' automatic DPI rescale (Control.ScaleControl) resizes every control's Bounds
        // but doesn't reliably fire Resize the way a normal size change does, so the two panels'
        // ListViewScrollbarOverlay bars - positioned imperatively as siblings of the file list,
        // outside the designer-driven scaling metadata - can be left stranded at their pre-scale
        // position/size after moving the window to a different-DPI monitor. Force both back into
        // place from the (already-rescaled) file list bounds.
        _leftPanel.RefreshScrollbarOverlay();
        _rightPanel.RefreshScrollbarOverlay();
    }

    private void RefreshToolbarIconsForDpi()
    {
        var iconSize = GetIconSize();
        _toolStrip.ImageScalingSize = new Size(iconSize, iconSize);
        _functionBar.ImageScalingSize = new Size(iconSize, iconSize);
        var p = ThemeService.Current;
        ControlThemer.ThemeToolStripItems(_menuStrip.Items, p);
        ControlThemer.ThemeToolStripItems(_toolStrip.Items, p);
        ControlThemer.ThemeToolStripItems(_functionBar.Items, p);
    }

    private ToolStripButton TbBtn(string tooltipKey, string iconKey, Action onClick)
    {
        var L = LocalizationService.Current;
        var scale = DeviceDpi / 96f;
        var btn = new ToolStripButton
        {
            Image = ToolbarIcons.Get(iconKey),
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = L.GetString(tooltipKey),
            // Image-only buttons have no caption to fall back on, so without an explicit
            // accessible name they show up nameless in the UIA tree (and to a screen reader).
            AccessibleName = L.GetString(tooltipKey),
            Padding = new Padding(6, 3, 6, 3),
            Margin = new Padding(2, 0, 2, 0),
            AutoSize = false,
            Width = (int)Math.Round(34 * scale),
            Height = (int)Math.Round(32 * scale),
            Tag = iconKey
        };
        btn.Click += (_, _) => onClick();
        _relocalizeActions.Add(() =>
        {
            btn.ToolTipText = L.GetString(tooltipKey);
            btn.AccessibleName = L.GetString(tooltipKey);
        });
        return btn;
    }

    // ═══════════════════════════════════════════
    // MAIN AREA (two panels)
    // ═══════════════════════════════════════════

    private void BuildMainArea()
    {
        var p = ThemeService.Current;
        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 5,
            FixedPanel = FixedPanel.None,
            BackColor = p.SplitterNormal,
            Panel1MinSize = 100,
            Panel2MinSize = 100
        };
        _mainSplit.Panel1.BackColor = p.Background;
        _mainSplit.Panel2.BackColor = p.Background;

        _leftPanel = new FilePanelUserControl(_vm.LeftPanel, "LeftPanel");
        _rightPanel = new FilePanelUserControl(_vm.RightPanel, "RightPanel");

        _mainSplit.Panel1.Controls.Add(_leftPanel);
        _mainSplit.Panel2.Controls.Add(_rightPanel);

        Controls.Add(_mainSplit);

        // Splitter hover overlay for visual feedback
        _splitterOverlay = new Panel
        {
            Visible = false,
            BackColor = p.SplitterHover,
            Cursor = Cursors.VSplit
        };
        Controls.Add(_splitterOverlay);
        _splitterOverlay.BringToFront();

        _mainSplit.MouseMove += (_, e) =>
            UpdateSplitterOverlay(_mainSplit.SplitterRectangle.Contains(e.Location) || _splitterDragging);
        _mainSplit.MouseLeave += (_, _) =>
        {
            if (!_splitterDragging) UpdateSplitterOverlay(false);
        };
        _mainSplit.SplitterMoving += (_, _) =>
        {
            _splitterDragging = true;
            UpdateSplitterOverlay(true);
        };
        _mainSplit.SplitterMoved += (_, _) =>
        {
            _splitterDragging = false;
            // Remember the user's chosen proportion so a later window resize preserves it
            // instead of snapping back to center - see ApplySplitRatio/OnFormResize.
            _splitRatio = ComputeSplitRatio();
            var stillOver = _mainSplit.SplitterRectangle.Contains(_mainSplit.PointToClient(Cursor.Position));
            UpdateSplitterOverlay(stillOver);
        };

        _leftPanel.PanelActivated += (_, _) => _vm.SetActivePanel(_vm.LeftPanel);
        _rightPanel.PanelActivated += (_, _) => _vm.SetActivePanel(_vm.RightPanel);
        _leftPanel.ItemActivated += OnItemActivated;
        _rightPanel.ItemActivated += OnItemActivated;
        _leftPanel.ArchiveEntered += OnArchiveEntered;
        _rightPanel.ArchiveEntered += OnArchiveEntered;
        _leftPanel.ConnectionActivated += OnConnectionActivated;
        _rightPanel.ConnectionActivated += OnConnectionActivated;

        // Wire context menu events from panels to commands
        WirePanelContextMenu(_leftPanel);
        WirePanelContextMenu(_rightPanel);

        // Wire drag & drop between panels
        _leftPanel.ItemsDropped += OnItemsDropped;
        _rightPanel.ItemsDropped += OnItemsDropped;
    }

    private void UpdateSplitterOverlay(bool show)
    {
        if (!show)
        {
            _splitterOverlay.Visible = false;
            return;
        }

        var r = _mainSplit.SplitterRectangle;
        _splitterOverlay.Bounds = new Rectangle(_mainSplit.Left + r.X, _mainSplit.Top + r.Y, r.Width, r.Height);
        _splitterOverlay.BringToFront();
        _splitterOverlay.Visible = true;
    }

    private void WirePanelContextMenu(FilePanelUserControl panel)
    {
        panel.EditRequested += (_, item) => OnEdit(this, item!);
        panel.ViewRequested += (_, item) => OnView(this, item!);
        panel.CopyRequested += (_, _) => _vm.Commands.Execute(CommandIds.Copy);
        panel.MoveRequested += (_, _) => _vm.Commands.Execute(CommandIds.Move);
        panel.RenameRequested += (_, _) => _vm.Commands.Execute(CommandIds.Rename);
        panel.DeleteRequested += (_, _) => _vm.Commands.Execute(CommandIds.Delete);
        panel.PropertiesRequested += (_, _) => _vm.Commands.Execute(CommandIds.ShowProperties);
    }

    private void OnItemsDropped(object? sender, PanelDropEventArgs e)
    {
        var destination = e.Destination;
        if (string.IsNullOrEmpty(destination)) return;

        var settings = SettingsService.Load();
        var options = new TransferOptions
        {
            CopyAttributes = settings.CopyAttributes,
            CopyTimestamps = settings.CopyTimestamps,
            Compression = ResolveCompressionForDestination(destination, settings),
            SkipCompressionForCompressedFiles = settings.SkipCompressionForCompressedFiles,
            AlreadyCompressedExtensions = settings.AlreadyCompressedExtensions.Count > 0 ? settings.AlreadyCompressedExtensions : null,
            OverwriteResolver = settings.ConfirmOverwrite ? CreateOverwriteResolver() : null,
            Overwrite = !settings.ConfirmOverwrite
        };

        if (e.SourcePanel != null)
        {
            var source = e.SourcePanel.ViewModel;
            var entries = e.Items.Where(i => !i.IsParent).Select(i => i.Entry).ToList();
            if (entries.Count == 0) return;

            _vm.ExecuteTransfer(source.CurrentFileSystem, source.CurrentPath, entries,
                destination, options, move: !e.IsCopy);
            return;
        }

        // External drop: group by owning folder so relative paths stay intact.
        var external = new List<FileEntry>();
        foreach (var path in e.ExternalPaths)
        {
            try
            {
                var fsi = Directory.Exists(path)
                    ? new DirectoryInfo(path)
                    : (FileSystemInfo)new FileInfo(path);
                if (!fsi.Exists) continue;
                external.Add(FileEntry.FromFileSystemInfo(path, fsi));
            }
            catch (Exception ex)
            {
                LogService.Error($"Drag-drop: cannot access {path}: {ex.Message}", ex);
            }
        }

        foreach (var group in external.GroupBy(en => VfsPath.GetParent(en.FullPath), StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(group.Key)) continue;
            _vm.ExecuteTransfer(_vm.FileSystem, group.Key, group.ToList(),
                destination, options, move: !e.IsCopy);
        }
    }

    /// <summary>
    /// Extensions that Windows will run as code the instant <see cref="Process.Start(ProcessStartInfo)"/>
    /// (with <c>UseShellExecute = true</c>) hands them to the shell - the same vector as double-
    /// clicking the file in Explorer. Deliberately broader than <see cref="FileIcons.GetIconType"/>'s
    /// "Executable" icon classification, which exists to pick a display glyph, not to gate a
    /// dangerous action - it neither needs nor wants script hosts (<c>.vbs</c>/<c>.js</c>/<c>.wsf</c>),
    /// <c>.scr</c>/<c>.hta</c>/<c>.lnk</c>/<c>.pif</c>/<c>.cpl</c>, or <c>.reg</c> for that purpose.
    /// <c>.jar</c> is deliberately absent: it never reaches here, since <see cref="ArchiveFormatRegistry.FromExtension"/>
    /// already claims it as a plain ZIP container earlier in the activation path (see
    /// <c>FilePanelUserControl.OnItemDoubleClick</c>).
    /// </summary>
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".scr", ".hta", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".ps1", ".msi", ".msc", ".reg", ".lnk", ".pif", ".cpl",
    };

    private async void OnItemActivated(object? sender, FileSystemItem item)
    {
        if (item.IsDirectory || item.IsParent) return;

        var L = LocalizationService.Current;
        var originFs = _vm.ActivePanel.CurrentFileSystem;
        // Guarding on the active panel's own NativePaths capability (rather than just
        // RemotePath.IsRemote) is what catches a file INSIDE an archive too:
        // VfsPath.IsArchive(item.FullPath) alone would miss a remote panel, and RemotePath.IsRemote
        // alone would miss an archive panel; the capability flag is the one check that is correct
        // for both, matching FileSystemCapabilities.NativePaths's own doc comment.
        var isNative = originFs.Capabilities.HasFlag(FileSystem.FileSystemCapabilities.NativePaths);
        // TrimEnd('.', ' ') before extracting the extension: Win32 silently strips a trailing dot
        // or space when it materializes a file on disk (the same fact RemotePath.IsSafeEntryName
        // rejects names for), so an entry named "invoice.exe." or "invoice.exe " reports extension
        // ".exe." / ".exe " here - neither matches ExecutableExtensions - yet becomes a real
        // "invoice.exe" the moment MaterializedFile.AcquireAsync creates the local temp copy below,
        // running unconfirmed. Trimming first closes that gap without changing FileEntry.GetExtension
        // itself, which many unrelated call sites rely on to report the exact on-disk extension.
        var isExecutable = ExecutableExtensions.Contains(FileEntry.GetExtension(item.Name.TrimEnd('.', ' ')));

        // Security decision, not a missing feature: an executable reached via a connection or an
        // archive is refused outright, never confirmed - downloading an unknown .exe from wherever
        // this panel happens to be and running it on one Enter keystroke is categorically different
        // from running a local file the user already had on disk. A local executable keeps the
        // existing confirm-then-run path below.
        if (!isNative && isExecutable)
        {
            StyledMessageBox.Show(L.GetString("Panel.RemoteExecutableUnsupported", item.Name),
                L.GetString("Common.Info"), MsgBoxButtons.OK, MsgBoxIcon.Information, this);
            return;
        }

        // Enter/double-click on a document must never be a silent code-execution vector - a
        // disguised executable (invoice.pdf.exe, a .scr posing as an image, a malicious .lnk from
        // a downloaded archive) would otherwise run with one keystroke, indistinguishable from
        // opening a document. Always confirmed, no setting to skip it - the same "irreversible
        // enough that skipping confirmation isn't offered" call MainViewModel.Wipe makes.
        if (isExecutable)
        {
            var confirmed = StyledMessageBox.Show(
                L.GetString("Panel.ConfirmOpenExecutable", item.Name),
                L.GetString("Common.Confirm"),
                MsgBoxButtons.YesNo, MsgBoxIcon.Warning, this) == MsgBoxResult.Yes;
            if (!confirmed) return;
        }

        string localPath;
        if (isNative)
        {
            localPath = item.FullPath;
        }
        else
        {
            // Materialized into the panel's own session (not disposed here): the launched external
            // program needs the file to still exist for as long as it keeps it open, and there is
            // no reliable, universal way to detect "the external app is done with this file" across
            // every possible ShellExecute delegation path (a brand-new process, activation of an
            // already-running instance via COM/DDE, ...). The temp copy is therefore intentionally
            // left in place for the rest of the session - cleaned up with the panel/app, same as any
            // other materialized file - rather than deleted the instant Process.Start returns, which
            // would race the very application it was just handed to. Write-back is not offered for
            // the same reason: there is no trustworthy "the user is done editing" signal to hang it
            // on, so the dialog below tells the user plainly instead of silently discarding an edit.
            try
            {
                var materialized = await _vm.ActivePanel.MaterializeAsync(
                    originFs, item.FullPath, FileSystem.Materialization.MaterializeOptions.ForArchiveRead, CancellationToken.None);
                localPath = materialized.LocalPath;
            }
            catch (IOException ex)
            {
                LogService.Error($"Failed to materialize {item.FullPath}", ex);
                StyledMessageBox.Show(ex.Message, L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
                return;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true });
            if (!isNative)
                StyledMessageBox.Show(L.GetString("Panel.OpenedTemporaryCopy", item.Name),
                    L.GetString("Common.Info"), MsgBoxButtons.OK, MsgBoxIcon.Information, this);
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to open {localPath}: {ex.Message}", ex);
            // Previously logged only - a failure here (no registered handler, the file locked, a
            // permission error) produced no visible sign anything went wrong; Enter/double-click
            // would just silently do nothing, indistinguishable from a slow-to-open program.
            StyledMessageBox.Show(L.GetString("Panel.OpenFailed", item.Name, ex.Message),
                L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
        }
    }

    // ═══════════════════════════════════════════
    // FUNCTION BUTTONS (F3-F10, localized)
    // ═══════════════════════════════════════════

    private void BuildFunctionButtons()
    {
        var p = ThemeService.Current;
        var iconSize = GetIconSize();
        var scale = DeviceDpi / 96f;
        _functionBar = new ToolStrip
        {
            Dock = DockStyle.Bottom,
            GripStyle = ToolStripGripStyle.Hidden,
            ImageScalingSize = new Size(iconSize, iconSize),
            Padding = new Padding(6, 3, 6, 3),
            AutoSize = false,
            Height = (int)Math.Round(36 * scale),
            Renderer = new ThemeRenderer()
        };

        var specs = new (string key, string iconKey, string? cmd, Action? custom)[]
        {
            ("Fn.View", "view", CommandIds.View, null),
            ("Fn.Edit", "edit", CommandIds.Edit, null),
            ("Fn.Copy", "copy", CommandIds.Copy, null),
            ("Fn.Move", "move", CommandIds.Move, null),
            ("Fn.MkDir", "newdir", CommandIds.MakeDir, null),
            ("Fn.Delete", "delete", CommandIds.Delete, null),
            ("Fn.Terminal", "terminal", CommandIds.ToggleTerminal, null),
            ("Fn.Exit", "exit", CommandIds.Exit, null),
        };

        foreach (var (key, iconKey, cmd, custom) in specs)
        {
            var btn = new ToolStripButton
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                Image = ToolbarIcons.Get(iconKey),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = LocalizationService.Current.GetString(key),
                AutoSize = false,
                Width = (int)Math.Round(118 * scale),
                Height = (int)Math.Round(30 * scale),
                Padding = new Padding(4, 2, 4, 2),
                Margin = new Padding(2, 0, 2, 0),
                ForeColor = p.HeaderForeground,
                Font = p.GridFont,
                Tag = iconKey
            };
            if (cmd != null)
                btn.Click += (_, _) => _vm.Commands.Execute(cmd);
            else if (custom != null)
                btn.Click += (_, _) => custom();

            var capturedKey = key;
            _relocalizeActions.Add(() => btn.Text = LocalizationService.Current.GetString(capturedKey));
            _functionBar.Items.Add(btn);
        }

        Controls.Add(_functionBar);
    }

    // ═══════════════════════════════════════════
    // EMBEDDED TERMINAL PANEL
    // ═══════════════════════════════════════════

    private void BuildTerminalPanel()
    {
        var p = ThemeService.Current;
        
        // Splitter between main area and terminal
        _terminalSplitter = new Splitter
        {
            Dock = DockStyle.Bottom,
            Height = 4,
            BackColor = p.GridLine,
            Visible = false,
            Cursor = Cursors.SizeNS
        };
        Controls.Add(_terminalSplitter);

        // Terminal panel itself
        _terminalPanel = new EmbeddedTerminalPanel
        {
            Dock = DockStyle.Bottom,
            Height = _terminalHeight,
            Visible = false
        };
        Controls.Add(_terminalPanel);
    }

    private void ToggleTerminal()
    {
        _terminalVisible = !_terminalVisible;
        _terminalPanel.Visible = _terminalVisible;
        _terminalSplitter.Visible = _terminalVisible;

        if (_terminalVisible)
        {
            // "OnOpen" (TerminalFollowPanelCwd) means "push once per time the terminal becomes
            // visible" - reset so PushActivePathToTerminal's one-shot guard fires again.
            _terminalFollowedOnceSinceOpen = false;
            PushActivePathToTerminal();
        }
    }

    private void CreateTerminalTabWithDefaults()
    {
        if (_terminalPanel?.Visible != true)
            ToggleTerminal();

        _terminalPanel?.ShowNewTabDialog();
    }

    private void CloseTerminalTab()
    {
        if (_terminalPanel?.Visible == true)
        {
            // Close active tab
            var activeTab = _terminalPanel.SessionManager?.ActiveTab;
            if (activeTab != null)
                _terminalPanel.CloseTerminalTab(activeTab.Id);
        }
    }

    private void NextTerminalTab()
    {
        if (_terminalPanel?.Visible == true)
            _terminalPanel?.NextTab();
    }

    private void PreviousTerminalTab()
    {
        if (_terminalPanel?.Visible == true)
            _terminalPanel?.PreviousTab();
    }

    // ═══════════════════════════════════════════
    // STATUS BAR
    // ═══════════════════════════════════════════

    private void BuildStatusBar()
    {
        _statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            SizingGrip = false,
            Padding = new Padding(10, 3, 10, 4)
        };

        _lblStatus = new ToolStripStatusLabel
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ThemeService.Current.DimForeground,
            Font = ThemeService.Current.StatusBarFont
        };

        // Separator between status and queue
        var sep = new ToolStripStatusLabel
        {
            Text = "\u2502",
            ForeColor = ThemeService.Current.GridLine,
            Margin = new Padding(12, 0, 12, 0),
            Font = ThemeService.Current.StatusBarFont
        };

        _lblQueue = new ToolStripStatusLabel
        {
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = ThemeService.Current.Accent,
            Margin = new Padding(0, 0, 0, 0),
            Font = ThemeService.Current.StatusBarFont
        };

        _statusStrip.Items.Add(_lblStatus);
        _statusStrip.Items.Add(sep);
        _statusStrip.Items.Add(_lblQueue);

        Controls.Add(_statusStrip);
    }

    // ═══════════════════════════════════════════
    // DIALOG OPENERS
    // ═══════════════════════════════════════════

    private void OpenSettings()
    {
        using var dlg = new SettingsForm();
        dlg.SettingsSaved += (_, _) =>
        {
            ApplyTheme();
            ApplyVisibility();
            var s = SettingsService.Load();
            _vm.LeftPanel.ShowHidden = s.ShowHidden;
            _vm.RightPanel.ShowHidden = s.ShowHidden;
            _vm.LeftPanel.ShowSystem = s.ShowSystem;
            _vm.RightPanel.ShowSystem = s.ShowSystem;
            _vm.LeftPanel.IsFlatView = s.FlatView;
            _vm.RightPanel.IsFlatView = s.FlatView;
            _leftPanel.RefreshFromViewModel();
            _rightPanel.RefreshFromViewModel();
            _cachedTerminalFollow = s.TerminalFollowPanelCwd;
            _vm.Hotkeys.Reload(s.CustomHotkeys);
        };
        dlg.ShowDialog(this);
    }

    private void OpenBookmarks()
    {
        using var dlg = new BookmarksForm(_vm.PathExistsAsync);
        dlg.BookmarkActivated += (_, path) => _ = _vm.ActivePanel.NavigateAsync(path);
        dlg.ShowDialog(this);
    }

    private void OpenConnections()
    {
        using var dlg = new ConnectionsForm();
        // The places bar shows connections alongside drives, so it has to rebuild after any edit.
        dlg.ConnectionsChanged += (_, _) =>
        {
            // A deleted profile must actually close its connection rather than leave an
            // orphan holding a socket; the places bar then rebuilds from the manager's event.
            ConnectionManager.Instance.SyncWithProfiles();
            EvictPanelsFromClosedConnections();
            _ = DriveCatalog.Instance.RefreshAsync();
        };
        dlg.ShowDialog(this);
    }

    /// <summary>
    /// Sends any panel back to a local folder when the connection it was showing has just been
    /// closed.
    ///
    /// <para>Deleting a profile disposes its filesystem, and a panel still holding that instance is
    /// not merely stale: its next refresh throws <see cref="ObjectDisposedException"/>, which
    /// <c>RefreshAsync</c> catches and logs - leaving the panel frozen on the contents of a
    /// connection that no longer exists, with no indication why. The panel cannot notice this on its
    /// own, so the code that closed the connection is what has to move it.</para>
    /// </summary>
    private void EvictPanelsFromClosedConnections()
    {
        foreach (var panel in new[] { _vm.LeftPanel, _vm.RightPanel })
        {
            if (!FileSystem.RemotePath.IsRemote(panel.CurrentPath)) continue;
            if (ConnectionManager.Instance.GetConnectedForPath(panel.CurrentPath) is not null) continue;

            panel.CurrentFileSystem = new FileSystem.LocalFileSystem();
            _ = panel.NavigateAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
    }

    // The six non-modal dialogs below (as opposed to OpenAbout()'s using+ShowDialog, which is
    // modal) all follow the same self-disposal idiom: FormClosed disposes the form once the user
    // closes it. CA2000's escape analysis can't trace disposal through an event subscription, so
    // it flags every one of them as leaked - they aren't; each is disposed exactly once, from the
    // FormClosed handler registered two lines below its construction.
    private void OpenDirectoryTree()
    {
#pragma warning disable CA2000
        var dlg = new DirectoryTreeForm(_vm.ActivePanel.CurrentPath, _vm.ActivePanel.CurrentFileSystem);
#pragma warning restore CA2000
        dlg.NavigateRequested += (_, path) => _ = _vm.ActivePanel.NavigateAsync(path);
        dlg.FormClosed += (_, _) => dlg.Dispose();
        dlg.Show(this);
    }

    private void OpenAbout()
    {
        using var dlg = new AboutForm();
        dlg.ShowDialog(this);
    }

    private void OnOperationStarted(object? sender, (IFileOperation operation, string displayName) e)
    {
        if (!IsHandleCreated) return;

#pragma warning disable CA2000 // see the comment on OpenDirectoryTree() above
        var dlg = new OperationDialogForm(e.operation, e.displayName);
#pragma warning restore CA2000
        EventHandler<OperationProgress> progressHandler = (_, p) => dlg.UpdateProgress(p);
        e.operation.ProgressChanged += progressHandler;
        dlg.FormClosed += (_, _) =>
        {
            e.operation.ProgressChanged -= progressHandler;
            dlg.Dispose();
        };
        dlg.Show(this);
    }

    private void OpenOperationQueue()
    {
#pragma warning disable CA2000 // see the comment on OpenDirectoryTree() above
        var dlg = new OperationQueueForm(_vm.Operations);
#pragma warning restore CA2000
        dlg.FormClosed += (_, _) => dlg.Dispose();
        dlg.Show(this);
    }

    // ═══════════════════════════════════════════
    // EVENT WIRING
    // ═══════════════════════════════════════════

    private void WireEvents()
    {
        foreach (var panel in new[] { _vm.LeftPanel, _vm.RightPanel })
        {
            panel.ConfirmArchiveWriteBack = ConfirmArchiveWriteBack;
            panel.ArchiveWriteBackFailed = OnArchiveWriteBackFailed;
        }

        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.DeleteConfirmRequested += OnDeleteConfirm;
        _vm.WipeConfirmRequested += OnWipeConfirm;
        _vm.ConfirmPermanentDeleteRequested += OnConfirmPermanentDelete;
        _vm.CopyConfirmRequested += OnCopyConfirm;
        _vm.MoveConfirmRequested += OnMoveConfirm;
        _vm.MakeDirRequested += OnMakeDir;
        _vm.RenameRequested += OnRename;
        _vm.ViewRequested += OnView;
        _vm.EditRequested += OnEdit;
        _vm.PropertiesRequested += OnProperties;
        _vm.MultiRenameRequested += OnMultiRename;
        _vm.ChangeDirRequested += OnChangeDir;
        _vm.SelectGroupRequested += OnSelectGroup;
        _vm.DeselectGroupRequested += OnDeselectGroup;
        _vm.SyncDirsRequested += OnSyncDirs;
        _vm.PackRequested += OnPackRequested;
        _vm.UnpackRequested += OnUnpackRequested;
        _vm.SplitRequested += OnSplitRequested;
        _vm.CombineRequested += OnCombineRequested;
        _vm.OperationRejected += OnOperationRejected;
        _vm.EditNewRequested += (_, _) => OpenEditorNew();
        _vm.ChecksumRequested += (_, _) => OpenChecksum();
        _vm.FindFilesRequested += (_, _) => OpenFindFiles();
        _vm.ToggleTerminalRequested += (_, _) => ToggleTerminal();
        _vm.CreateTerminalTabRequested += (_, _) => CreateTerminalTabWithDefaults();
        _vm.CloseTerminalTabRequested += (_, _) => CloseTerminalTab();
        _vm.NextTerminalTabRequested += (_, _) => NextTerminalTab();
        _vm.PreviousTerminalTabRequested += (_, _) => PreviousTerminalTab();
        _vm.ExitRequested += (_, _) => Close();
        _vm.AboutRequested += (_, _) => OpenAbout();
        _vm.ThemeChanged += (_, _) => ApplyTheme();
        _vm.ShowExtensionInNameChanged += (_, _) => OnShowExtensionInNameChanged();
        _vm.OperationStarted += OnOperationStarted;

        _terminalPanel.DirectoryChanged += OnTerminalDirectoryChanged;
        _terminalPanel.ShowPathInPanelRequested += OnShowPathInPanelRequested;
        _vm.LeftPanel.PropertyChanged += OnFilePanelPropertyChanged;
        _vm.RightPanel.PropertyChanged += OnFilePanelPropertyChanged;

        _deviceWatcher.DevicesChanged += OnDevicesChanged;

        Load += OnFormLoad;
        Resize += OnFormResize;
        KeyDown += OnFormKeyDown;
        FormClosing += OnFormClosing;
        FormClosed += (_, _) =>
        {
            _deviceWatcher.Dispose();
            ConnectionManager.Instance.Dispose();
        };
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!IsHandleCreated) return;

        if (e.PropertyName == nameof(MainViewModel.StatusText))
            BeginInvoke(() => _lblStatus.Text = _vm.StatusText);
        else if (e.PropertyName == nameof(MainViewModel.OperationQueueText))
            BeginInvoke(() => _lblQueue.Text = _vm.OperationQueueText);
        else if (e.PropertyName == nameof(MainViewModel.ActivePanel))
            BeginInvoke(() =>
            {
                _leftPanel.ViewModel.IsActive = _vm.ActivePanel == _vm.LeftPanel;
                _rightPanel.ViewModel.IsActive = _vm.ActivePanel == _vm.RightPanel;
                PushActivePathToTerminal();
            });
    }

    private void OnFilePanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PanelViewModel.CurrentPath))
            return;

        // PanelViewModel.NavigateAsync sets CurrentPath after an await with ConfigureAwait(false),
        // so this event can arrive on a non-UI thread.
        if (InvokeRequired)
        {
            BeginInvoke(() => OnFilePanelPropertyChanged(sender, e));
            return;
        }
        if (!IsHandleCreated) return;

        if (sender == _vm.ActivePanel)
            PushActivePathToTerminal();
    }

    private async void OnTerminalDirectoryChanged(object? sender, EmbeddedTerminalPanel.DirectoryChangedEventArgs e)
    {
        if (e.TabId != _terminalPanel.SessionManager?.ActiveTab?.Id)
            return; // only sync the visible/active terminal tab

        try
        {
            await _vm.ActivePanel.NavigateAsync(e.NewPath);
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to sync file panel to terminal cwd", ex);
        }
    }

    /// <summary>Handles a terminal tab's "Show in panel" context menu item - navigates the active
    /// file panel to a path detected in the terminal's own text (see
    /// <c>Terminal.Ui.PathDetector</c>). A file path navigates to its containing folder (there's
    /// nothing to "browse into" for a file); a directory navigates directly.</summary>
    private async void OnShowPathInPanelRequested(object? sender, string path)
    {
        try
        {
            var target = Directory.Exists(path) ? path
                : File.Exists(path) ? Path.GetDirectoryName(path)
                : null;
            if (string.IsNullOrEmpty(target))
                return;

            await _vm.ActivePanel.NavigateAsync(target);
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to show terminal path in panel", ex);
        }
    }

    /// <summary>Push the active file panel's path into the terminal (default path for new tabs,
    /// and - gated by the <c>TerminalFollowPanelCwd</c> setting - the live working directory of
    /// the active tab when the terminal is visible).</summary>
    private void PushActivePathToTerminal()
    {
        var path = _vm.ActivePanel.CurrentPath;
        _terminalPanel.DefaultPath = path;
        if (!_terminalVisible) return;

        if (_cachedTerminalFollow == "Never") return;
        if (_cachedTerminalFollow == "OnOpen" && _terminalFollowedOnceSinceOpen) return;

        _terminalPanel.SetWorkingDirectory(path);
        _terminalFollowedOnceSinceOpen = true;
    }

    private async void OnFormLoad(object? sender, EventArgs e)
    {
        // async void: an exception here doesn't go through Program.cs's crash handling
        // (AppDomain.UnhandledException/TaskScheduler.UnobservedTaskException don't see it -
        // WinForms routes exceptions raised while processing queued continuations through
        // Application.ThreadException instead, which isn't hooked either) - without this
        // try/catch, a bad settings.json or a failed initial NavigateAsync would surface as
        // WinForms' own raw unhandled-exception dialog instead of the app's own error UX.
        try
        {
            await InitializeAsync();

            // Deliberately not awaited: auto-connect talks to servers that may be unreachable,
            // and startup must not wait for any of them. Failures land in the places bar as a
            // retryable state, never as a dialog in front of a just-launched app.
            //
            // The continuation is the difference between "not awaited" and "not observed": an
            // exception the manager did not absorb would otherwise vanish into an unobserved task
            // and leave no trace anywhere.
            // TaskScheduler.Default, not the implicit TaskScheduler.Current: this runs inside
            // InitializeAsync, itself a continuation of the startup path - TaskScheduler.Current
            // is not guaranteed to be Default there, and this continuation only logs, with no
            // reason to run on whatever scheduler happened to be ambient at the call site.
            _ = ConnectionManager.Instance.AutoConnectAllAsync()
                .ContinueWith(t => LogService.Error("Auto-connect failed", t.Exception),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            LogService.Error("MainForm initialization failed", ex);
            StyledMessageBox.Show(ex.Message, LocalizationService.Current.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
        }

        CenterSplitter();
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        // Don't intercept hotkeys when typing in a text input control.
        // ActiveControl of a Form stops at the first nested ContainerControl (e.g. a
        // UserControl like ThemedTabControl hosting the terminal's input box), so walk
        // down the chain to the actual focused leaf control.
        var focused = ActiveControl;
        while (focused is ContainerControl container && container.ActiveControl != null)
            focused = container.ActiveControl;
        if (focused is TextBox or ComboBox or NumericUpDown or DomainUpDown)
        {
            // Only allow Tab (panel switch) and F-keys through
            if (e.KeyCode is not (Keys.Tab or Keys.F1 or Keys.F2 or Keys.F3 or Keys.F4
                or Keys.F5 or Keys.F6 or Keys.F7 or Keys.F8 or Keys.F9 or Keys.F10))
                return;
        }
        else if (focused is IKeyboardGreedyControl greedy && !greedy.AllowsAppHotkey(e.KeyCode))
        {
            // e.g. TerminalCanvas: wants almost every key for itself (typing Ctrl+A/Ctrl+D/etc.
            // must reach the shell, not this app's SelectAll/other hotkeys). Its own ProcessCmdKey
            // override is the primary gate; this is a defense-in-depth backstop.
            return;
        }

        if (_vm.Hotkeys.HandleKey(e))
            return;
    }

    /// <summary>
    /// Switches the active panel on a bare Tab.
    ///
    /// <para><b>Why this cannot live in <see cref="OnFormKeyDown"/>, where it used to be.</b>
    /// Confirmed empirically while investigating a test failure this same audit pass surfaced
    /// (see <c>DEBUG.md §0</c>, "Tab did not reliably switch panels"): WinForms treats a bare
    /// Tab as a dialog navigation key and resolves it via <c>Control.ProcessDialogKey</c> /
    /// <c>SelectNextControl</c> - a stage that runs <i>before</i> <c>KeyDown</c> is ever raised.
    /// <see cref="Form.KeyPreview"/> reorders <c>KeyDown</c> among the controls that do receive it;
    /// it does not pull Tab into that path at all. The old <c>KeyDown</c>-based branch therefore
    /// never fired for a real keypress. What made panel-switching look like it worked in ordinary
    /// use was native tab-order focus cycling coincidentally landing on the other panel's file
    /// list and triggering its own <c>GotFocus</c> handler - which depends on the exact tab order
    /// of every control currently on the form (how many toolbar buttons, whether the terminal panel
    /// is visible, ...), not on anything deliberate. <c>ProcessCmdKey</c> runs earlier than dialog-key
    /// processing, which is why <see cref="Terminal.Ui.TerminalCanvas"/> already uses it to claim
    /// Tab for the shell - the same mechanism, used here for the same reason.</para>
    ///
    /// <para>Runs before <see cref="OnFormKeyDown"/>'s text-field guard even exists, so the
    /// equivalent check is inline here: everything the old code let Tab through for
    /// (TextBox/ComboBox/etc.) still lets it through, and only <see cref="IKeyboardGreedyControl"/>
    /// (the terminal) can refuse it - matching the original carve-out exactly. In practice the
    /// terminal's own <c>ProcessCmdKey</c> already claims Tab for the shell before this override is
    /// even reached, since WinForms calls the focused control's <c>ProcessCmdKey</c> before walking
    /// up to the form's; the check here is the same defense-in-depth backstop
    /// <see cref="OnFormKeyDown"/> already keeps for the analogous case.</para>
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Tab && CanTabSwitchPanels())
        {
            var target = _vm.InactivePanel;
            _vm.SetActivePanel(target);
            (target == _vm.LeftPanel ? _leftPanel : _rightPanel).FocusFileList();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool CanTabSwitchPanels()
    {
        var focused = ActiveControl;
        while (focused is ContainerControl container && container.ActiveControl != null)
            focused = container.ActiveControl;

        return focused is not IKeyboardGreedyControl greedy || greedy.AllowsAppHotkey(Keys.Tab);
    }

    // ═══════════════════════════════════════════
    // DIALOG HANDLERS (use themed forms)
    // ═══════════════════════════════════════════

    private void OnDeleteConfirm(object? sender, IReadOnlyList<FileSystemItem> files)
    {
        var L = LocalizationService.Current;
        var names = string.Join("\n", files.Take(10).Select(f => f.Name));
        if (files.Count > 10) names += $"\n... {files.Count - 10}";

        var result = StyledMessageBox.Show(
            L.GetString("Confirm.DeleteItems", files.Count, names),
            L.GetString("Confirm.Delete", files.Count),
            MsgBoxButtons.YesNo, MsgBoxIcon.Question, this);

        if (result == MsgBoxResult.Yes)
            _vm.ExecuteDelete(files);
    }

    private void OnWipeConfirm(object? sender, IReadOnlyList<FileSystemItem> files)
    {
        var L = LocalizationService.Current;
        var names = string.Join("\n", files.Take(10).Select(f => f.Name));
        if (files.Count > 10) names += $"\n... {files.Count - 10}";

        var result = StyledMessageBox.Show(
            L.GetString("Confirm.WipeItems", files.Count, names),
            L.GetString("Confirm.Wipe", files.Count),
            MsgBoxButtons.YesNo, MsgBoxIcon.Warning, this);

        if (result == MsgBoxResult.Yes)
            _vm.ExecuteWipe(files);
    }

    /// <summary>
    /// Called from a background thread when the Recycle Bin failed partway through a delete and
    /// permanently removing the remaining files is the only option left. Marshals to the UI thread,
    /// same pattern as <see cref="CreateOverwriteResolver"/>.
    /// </summary>
    /// <summary>
    /// <see cref="PanelViewModel.ConfirmArchiveWriteBack"/> - asks whether to upload a materialized
    /// archive's edits back to <paramref name="originPath"/> before its temp copy is deleted.
    /// Unlike <see cref="OnConfirmPermanentDelete"/>, no thread marshaling is needed here:
    /// <c>PanelViewModel.ReleaseArchiveLease</c> is only ever reached from UI-thread code
    /// (navigating out of an archive, entering a different one, or closing the app).
    /// </summary>
    private bool ConfirmArchiveWriteBack(string originPath)
    {
        var L = LocalizationService.Current;
        return StyledMessageBox.Show(
            L.GetString("Archive.ConfirmWriteBack", VfsPath.GetName(originPath)),
            L.GetString("Archive.Title"), MsgBoxButtons.YesNo, MsgBoxIcon.Question, this) == MsgBoxResult.Yes;
    }

    /// <summary><see cref="PanelViewModel.ArchiveWriteBackFailed"/> - the lease is already gone by
    /// the time this fires (see that property's own doc comment), so this is purely informational:
    /// the user's edits didn't make it back to <paramref name="originPath"/>.</summary>
    private void OnArchiveWriteBackFailed(string originPath, Exception ex)
    {
        LogService.Error($"Archive write-back failed: {originPath}", ex);
        var L = LocalizationService.Current;
        StyledMessageBox.Show(L.GetString("Archive.WriteBackFailed", VfsPath.GetName(originPath), ex.Message),
            L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
    }

    private void OnConfirmPermanentDelete(object? sender, ConfirmPermanentDeleteEventArgs e)
    {
        if (!IsHandleCreated)
        {
            e.Proceed = false;
            return;
        }

        var L = LocalizationService.Current;
        var names = string.Join("\n", e.Paths.Take(10));
        if (e.Paths.Count > 10) names += $"\n... {e.Paths.Count - 10}";

        e.Proceed = (bool)Invoke(new Func<bool>(() =>
        {
            var result = StyledMessageBox.Show(
                L.GetString("Confirm.RecycleBinFailedPermanent", e.Paths.Count, names),
                L.GetString("Confirm.Delete", e.Paths.Count),
                MsgBoxButtons.YesNo, MsgBoxIcon.Warning, this);
            return result == MsgBoxResult.Yes;
        }))!;
    }

    private void OnCopyConfirm(object? sender, (IReadOnlyList<FileSystemItem> files, string sourcePath, string destPath) e)
    {
        using var dlg = new CopyMoveDialogForm(e.files, e.destPath, isMove: false);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var options = BuildTransferOptions(dlg.OverwritePolicyIndex, dlg.CopyAttributes, dlg.CopyTimestamps, dlg.DestinationPath);
        _vm.ExecuteCopy(e.files, dlg.DestinationPath, options);
    }

    private void OnMoveConfirm(object? sender, (IReadOnlyList<FileSystemItem> files, string sourcePath, string destPath) e)
    {
        LogService.Info($"OnMoveConfirm: {e.files.Count} files, dest={e.destPath}");
        using var dlg = new CopyMoveDialogForm(e.files, e.destPath, isMove: true);
        var result = dlg.ShowDialog(this);
        LogService.Info($"OnMoveConfirm: dialog result={result}, destination={dlg.DestinationPath}");
        if (result != DialogResult.OK) return;

        var options = BuildTransferOptions(dlg.OverwritePolicyIndex, dlg.CopyAttributes, dlg.CopyTimestamps, dlg.DestinationPath);
        LogService.Info($"OnMoveConfirm: calling ExecuteMove with dest={dlg.DestinationPath}");
        _vm.ExecuteMove(e.files, dlg.DestinationPath, options);
    }

    private TransferOptions BuildTransferOptions(int policyIndex, bool copyAttrs, bool copyTs, string destinationPath)
    {
        var action = (OverwriteAction)policyIndex;
        var settings = SettingsService.Load();
        var options = new TransferOptions
        {
            CopyAttributes = copyAttrs,
            CopyTimestamps = copyTs,
            Compression = ResolveCompressionForDestination(destinationPath, settings),
            SkipCompressionForCompressedFiles = settings.SkipCompressionForCompressedFiles,
            AlreadyCompressedExtensions = settings.AlreadyCompressedExtensions.Count > 0 ? settings.AlreadyCompressedExtensions : null
        };

        switch (action)
        {
            case OverwriteAction.Overwrite:
            case OverwriteAction.OverwriteAll:
                options.Overwrite = true;
                break;
            case OverwriteAction.Ask:
                options.OverwriteResolver = CreateOverwriteResolver();
                break;
            case OverwriteAction.Skip:
            case OverwriteAction.SkipAll:
                options.OverwriteResolver = (string s, string d, FileEntry si, FileEntry? di, out string? nn) => { nn = null; return OverwriteAction.Skip; };
                break;
            case OverwriteAction.OverwriteOlder:
                options.OverwriteResolver = (string s, string d, FileEntry si, FileEntry? di, out string? nn) =>
                {
                    nn = null;
                    return di != null && si.LastWriteTimeUtc > di.LastWriteTimeUtc
                        ? OverwriteAction.Overwrite
                        : OverwriteAction.Skip;
                };
                break;
        }

        return options;
    }

    private OverwriteResolveHandler CreateOverwriteResolver()
    {
        OverwriteAction? cachedAction = null;

        OverwriteAction Resolve(string source, string destination, FileEntry sourceInfo, FileEntry? destInfo, out string? newName)
        {
            newName = null;

            if (cachedAction is OverwriteAction.OverwriteAll)
                return OverwriteAction.Overwrite;
            if (cachedAction is OverwriteAction.SkipAll)
                return OverwriteAction.Skip;

            var srcText = $"{UiHelpers.FormatSize(sourceInfo.Size)}  {sourceInfo.LastWriteTime:G}";
            var dstText = destInfo != null
                ? $"{UiHelpers.FormatSize(destInfo.Size)}  {destInfo.LastWriteTime:G}"
                : "";

            if (!IsHandleCreated) return OverwriteAction.Skip;

            var result = (int?)Invoke(new Func<int?>(() =>
            {
                using var dlg = new OverwriteDialogForm(sourceInfo.Name, srcText, dstText);
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.Result : (int?)2;
            }))!;

            var chosen = (OverwriteAction)result;
            if (chosen is OverwriteAction.OverwriteAll or OverwriteAction.SkipAll)
                cachedAction = chosen;

            if (chosen == OverwriteAction.Rename)
            {
                newName = GenerateUniqueName(destination);
                return OverwriteAction.Rename;
            }

            return chosen is OverwriteAction.OverwriteAll ? OverwriteAction.Overwrite : chosen;
        }

        return Resolve;
    }

    private static string GenerateUniqueName(string destPath)
    {
        var dir = Path.GetDirectoryName(destPath) ?? "";
        var ext = FileSystem.FileEntry.GetExtension(destPath);
        var fileName = Path.GetFileName(destPath);
        var name = ext.Length > 0 ? fileName[..^ext.Length] : fileName;
        int counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{name} ({counter.ToString(CultureInfo.InvariantCulture)}){ext}");
            counter++;
        }
        while (File.Exists(candidate) || Directory.Exists(candidate));
        return Path.GetFileName(candidate);
    }

    private async void OnMakeDir(object? sender, string path)
    {
        var L = LocalizationService.Current;
        using var dlg = new InputDialogForm(L.GetString("Input.CreateDir"), L.GetString("Input.CreateDirPrompt"));
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.Value))
        {
            try
            {
                // Through the active panel's own IFileSystem + VfsPath.Combine, not
                // Directory.CreateDirectory(Path.Combine(...)) - the previous System.IO-only
                // implementation was the one command MainViewModel.MakeDir's own Writable capability
                // gate could enable (a writable archive or connection) that then still failed here:
                // System.IO.Path.Combine on a "archive.zip|inner/dir" or "sftp://host/dir" path
                // either throws on the illegal '|'/':' or silently resolves against the process's
                // own working directory, and Directory.CreateDirectory would create a REAL local
                // folder with that garbled name instead of the intended virtual one.
                var fs = _vm.ActivePanel.CurrentFileSystem;
                await fs.CreateDirectoryAsync(VfsPath.Combine(path, dlg.Value));
                _ = _vm.ActivePanel.RefreshAsync();
            }
            catch (Exception ex)
            {
                StyledMessageBox.Show(ex.Message, L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
            }
        }
    }

    private async void OnRename(object? sender, FileSystemItem item)
    {
        var L = LocalizationService.Current;
        using var dlg = new InputDialogForm(L.GetString("Input.Rename"), L.GetString("Input.RenamePrompt"), item.Name);
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.Value) && dlg.Value != item.Name)
        {
            try
            {
                // VfsPath.ChangeName (not Path.GetDirectoryName + File.Move/Directory.Move) is the
                // same fix as MakeDir above, plus it's the one choke point that validates the typed
                // name against RemotePath.IsSafeEntryName - blocking a path separator, an ADS colon,
                // a reserved DOS device name, or a display-spoofing character before it ever reaches
                // the provider, the same protection Copy/Move/Pack/Unpack's overwrite-rename flow
                // already gets (see F007/F020 in the audit history) but this command never did.
                var fs = _vm.ActivePanel.CurrentFileSystem;
                var newPath = VfsPath.ChangeName(item.FullPath, dlg.Value);
                await fs.MoveAsync(item.FullPath, newPath, overwrite: false);
                _ = _vm.ActivePanel.RefreshAsync();
            }
            catch (Exception ex)
            {
                StyledMessageBox.Show(ex.Message, L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
            }
        }
    }

    private void OnView(object? sender, FileSystemItem item)
    {
        try
        {
            var panel = _vm.ActivePanel;

            // External viewer only makes sense for a real local path - an archive entry or a
            // remote file has no path an outside process could open, so those always fall through
            // to the built-in viewer below regardless of the setting.
            var settings = SettingsService.Load();
            if (settings.ExternalViewerEnabled &&
                panel.CurrentFileSystem.Capabilities.HasFlag(FileSystemCapabilities.NativePaths) &&
                ExternalToolLauncher.TryLaunch(settings.ExternalViewerPath, settings.ExternalViewerArgs, item.FullPath))
            {
                return;
            }

            var files = panel.Items
                .Where(f => !f.IsDirectory && !f.IsParent)
                .Select(f => f.FullPath)
                .ToList();
            var currentIndex = files.IndexOf(item.FullPath);

#pragma warning disable CA2000 // see the comment on OpenDirectoryTree() above
            var dlg = new ViewerForm(panel.CurrentFileSystem, item.FullPath, files, currentIndex);
#pragma warning restore CA2000
            dlg.FormClosed += (_, _) => dlg.Dispose();
            dlg.Show(this);
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to view file: {item.FullPath}", ex);
            StyledMessageBox.Show(ex.Message, LocalizationService.Current.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
        }
    }

    private void OnEdit(object? sender, FileSystemItem item)
    {
        try
        {
            // Same "native paths only, silent fallback otherwise" contract as OnView above.
            var settings = SettingsService.Load();
            if (settings.ExternalEditorEnabled &&
                _vm.ActivePanel.CurrentFileSystem.Capabilities.HasFlag(FileSystemCapabilities.NativePaths) &&
                ExternalToolLauncher.TryLaunch(settings.ExternalEditorPath, settings.ExternalEditorArgs, item.FullPath))
            {
                return;
            }

#pragma warning disable CA2000 // see the comment on OpenDirectoryTree() above
            var dlg = new EditorForm(_vm.ActivePanel.CurrentFileSystem, item.FullPath);
#pragma warning restore CA2000
            dlg.FormClosed += (_, _) => dlg.Dispose();
            dlg.Show(this);
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to edit file: {item.FullPath}", ex);
            StyledMessageBox.Show(ex.Message, LocalizationService.Current.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
        }
    }

    private void OnProperties(object? sender, IReadOnlyList<FileSystemItem> items)
    {
        using var dlg = new PropertiesForm(items);
        dlg.ShowDialog(this);

        // Attribute/timestamp changes may alter listing ordering & visibility.
        _ = _vm.LeftPanel.RefreshAsync();
        _ = _vm.RightPanel.RefreshAsync();
    }

    private void OnMultiRename(object? sender, (IReadOnlyList<FileSystemItem> files, string sourcePath) e)
    {
        using var dlg = new MultiRenameForm(e.files, e.sourcePath);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var failures = new List<string>();

        foreach (var (oldPath, newPath) in dlg.Results)
        {
            try
            {
                if (Directory.Exists(oldPath))
                    Directory.Move(oldPath, newPath);
                else
                    File.Move(oldPath, newPath);
            }
            catch (Exception ex)
            {
                LogService.Error($"Multi-rename failed: {oldPath} -> {newPath}: {ex.Message}", ex);
                failures.Add($"{Path.GetFileName(oldPath)}: {ex.Message}");
            }
        }

        _ = _vm.ActivePanel.RefreshAsync();

        // Unlike single Rename (OnRename below), a batch has multiple independent outcomes - a
        // silent per-file catch here would leave the user with no indication that some renames
        // never applied (e.g. a pattern collision with a file outside the selection).
        if (failures.Count > 0)
        {
            var L = LocalizationService.Current;
            StyledMessageBox.Show(string.Join("\n", failures), L.GetString("Common.Error"),
                MsgBoxButtons.OK, MsgBoxIcon.Error, this);
        }
    }

    /// <summary>
    /// Strips a menu-style "&amp;" mnemonic marker (single "&amp;" -&gt; removed, "&amp;&amp;" -&gt;
    /// literal "&amp;") from a localized string reused as a dialog title - <see cref="Form.Text"/>
    /// doesn't interpret "&amp;" as a mnemonic the way <c>ToolStripItem.Text</c> does, so
    /// <c>ChangeDir</c>/<c>SelectGroup</c>/<c>DeselectGroup</c> (none of which have a real menu item
    /// of their own - see the doc comments below) were showing the raw "&amp;" literally in their
    /// title bar, caught by visual inspection of a live build.
    /// </summary>
    private static string StripMnemonic(string text) => text
        .Replace("&&", "\0", StringComparison.Ordinal)
        .Replace("&", "", StringComparison.Ordinal)
        .Replace("\0", "&", StringComparison.Ordinal);

    // ChangeDir has no menu item of its own (Ctrl+G only) - Menu.Commands.ChangeDir exists purely
    // for this dialog's title, formatted with a menu mnemonic that was never meant to survive here.
    private void OnChangeDir(object? sender, string currentPath)
    {
        var L = LocalizationService.Current;
        using var dlg = new InputDialogForm(StripMnemonic(L.GetString("Menu.Commands.ChangeDir") ?? "Change Directory"),
            L.GetString("Input.ChangeDirPrompt") ?? "Path:", currentPath);
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.Value))
        {
            _ = _vm.ActivePanel.NavigateAsync(dlg.Value);
        }
    }

    // Same mnemonic-leak fix as OnChangeDir - SelectGroup/DeselectGroup are hotkey/command-only,
    // no menu item of their own, so Menu.Selection.Group/.DeselectGroup's "&" was pure noise here.
    private void OnSelectGroup(object? sender, EventArgs e)
    {
        var L = LocalizationService.Current;
        using var dlg = new InputDialogForm(StripMnemonic(L.GetString("Menu.Selection.Group")),
            L.GetString("Input.SelectPattern") ?? "Pattern (e.g. *.txt):", "*.*");
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.Value))
        {
            _vm.ActivePanel.SelectByPattern(dlg.Value);
        }
    }

    private void OnDeselectGroup(object? sender, EventArgs e)
    {
        var L = LocalizationService.Current;
        using var dlg = new InputDialogForm(StripMnemonic(L.GetString("Menu.Selection.DeselectGroup")),
            L.GetString("Input.SelectPattern") ?? "Pattern (e.g. *.txt):", "*.*");
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.Value))
        {
            _vm.ActivePanel.DeselectByPattern(dlg.Value);
        }
    }

    private void OnSyncDirs(object? sender, (string leftPath, string rightPath) e)
    {
        using var dlg = new SyncDirsForm(e.leftPath, e.rightPath, _vm.LeftPanel.CurrentFileSystem, _vm.RightPanel.CurrentFileSystem);
        dlg.CopyRequested += (_, req) => IssueSyncCopy(req);
        if (dlg.ShowDialog(this) == DialogResult.OK) { /* copy requests handled via CopyRequested event */ }
    }

    /// <summary>Routes a Sync Dirs copy through the same <see cref="CopyOperation"/> every other
    /// copy in the app uses (queued, progress-tracked, VFS-aware) instead of a hand-rolled
    /// <c>File.Copy</c>/<c>Directory</c> walk - what makes syncing into/out of an archive or a
    /// remote connection actually work, and what makes a copy into a materialized archive lease
    /// correctly mark that lease dirty (via <see cref="Operations.CopyOperation"/> writing through
    /// the panel's own <c>DirtyTrackingFileSystem</c>-wrapped <see cref="IFileSystem"/>, not a
    /// private bypass). <see cref="TransferOptions.Overwrite"/> is forced on, matching the old
    /// unconditional <c>overwrite: true</c> - the sync list's checkboxes are the user's conflict
    /// resolution, a second per-file prompt would be redundant.</summary>
    private void IssueSyncCopy(SyncCopyRequest req)
    {
        if (req.Items.Count == 0) return;

        // CA2000: ownership transfers to Operations.RunAsync, which disposes it on completion -
        // same pattern as every other operation constructed directly by this window/MainViewModel.
#pragma warning disable CA2000
        var op = new CopyOperation(req.SourceFs, req.DestFs, req.Items, req.SourceRoot, req.DestRoot,
            new TransferOptions { Overwrite = true });
#pragma warning restore CA2000
        _ = _vm.Operations.RunAsync(op, LocalizationService.Current.GetString("Op.DisplaySyncDirs", req.Items.Count));
    }

    /// <summary>
    /// A connection button in the places bar was clicked.
    ///
    /// One button, three meanings depending on state: an established connection is entered, an
    /// idle or failed one is (re)connected and then entered. Retrying from the same button is
    /// deliberate - a failed connection that offers no way to try again is a dead end, and the
    /// usual cause is a server that was simply not up yet.
    /// </summary>
    private async void OnConnectionActivated(object? sender, Guid profileId)
    {
        // async void, so the same rule as OnFormLoad applies: an exception escaping here does not
        // reach Program.cs's crash handling and would surface as WinForms' own raw dialog. Nothing
        // below is expected to throw - the connection manager absorbs its own failures - but "not
        // expected to" is not a guarantee when the other side is a network.
        try
        {
            await ActivateConnectionAsync(sender, profileId);
        }
        catch (Exception ex)
        {
            LogService.Error("Opening a connection failed", ex);
            var L = LocalizationService.Current;
            StyledMessageBox.Show(ex.Message, L.GetString("Conn.Title"),
                MsgBoxButtons.OK, MsgBoxIcon.Error, this);
        }
    }

    private async Task ActivateConnectionAsync(object? sender, Guid profileId)
    {
        if (sender is not FilePanelUserControl panel) return;

        var manager = ConnectionManager.Instance;
        var fs = manager.GetConnected(profileId);

        if (fs is null)
        {
            // Connecting talks to a server, so it must not run on the UI thread; the manager
            // enforces that and reports progress through its own event, which rebuilds the bar.
            fs = await manager.ConnectAsync(profileId);
            if (fs is null)
            {
                var status = manager.Current.FirstOrDefault(c => c.ProfileId == profileId);

                // A null result also means "someone else is already connecting to this" - startup
                // auto-connect, or the other panel. That is not a failure and must not be reported
                // as one; the button will come alive on its own when the attempt in flight settles.
                if (status?.State == ConnectionState.Connecting) return;

                var L = LocalizationService.Current;
                StyledMessageBox.Show(
                    L.GetString("Conn.ConnectFailed", status?.Name ?? "", status?.Error ?? ""),
                    L.GetString("Conn.Title"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
                return;
            }
        }

        var root = manager.Current.FirstOrDefault(c => c.ProfileId == profileId)?.RootPath;
        if (string.IsNullOrEmpty(root)) return;

        // Deliberately not assigning CurrentFileSystem here. NavigateAsync resolves the filesystem
        // from the path itself, and assigning it first opened a window between the two statements:
        // if the navigation was then superseded or refused, the panel was left holding a
        // connection while its path was still the local one - so the next local navigation listed
        // the server under a drive letter.
        await panel.ViewModel.NavigateAsync(root);
    }

    private async void OnArchiveEntered(object? sender, FileSystemItem item)
    {
        if (sender is not FilePanelUserControl panel) return;
        await EnterArchiveAsync(panel, item);
    }

    /// <summary>
    /// Browses into an archive as a virtual folder in <paramref name="panel"/> - the one way
    /// archives are opened now that <c>ArchiveForm</c> (a separate view+extract-only dialog that
    /// duplicated this, minus add/delete/queueing/overwrite-confirmation) has been retired.
    ///
    /// <para>When the item's own container isn't on this machine (a connection - a nested archive
    /// stays refused below, see <see cref="FilePanelUserControl.CanEnterAsArchive"/>'s own doc
    /// comment for why that needs no extra check here), it is materialized to a real local temp
    /// copy first (<see cref="FileSystem.Materialization.MaterializedFile"/>), owned by
    /// <paramref name="panel"/>'s own <c>ViewModel</c> for as long as it keeps browsing that
    /// archive, and wrapped <see cref="DirtyTrackingFileSystem"/> - fully writable against the temp
    /// copy, with a write-back offered (via <c>PanelViewModel.ConfirmArchiveWriteBack</c>) when the
    /// panel leaves the archive, rather than refusing every write outright.</para>
    /// </summary>
    private async Task EnterArchiveAsync(FilePanelUserControl panel, FileSystemItem item)
    {
        var L = LocalizationService.Current;

        if (VfsPath.IsArchive(item.FullPath))
        {
            StyledMessageBox.Show(L.GetString("Archive.NestedUnsupported", item.Name),
                L.GetString("Archive.Title"), MsgBoxButtons.OK, MsgBoxIcon.Information, this);
            return;
        }

        var originFs = panel.ViewModel.CurrentFileSystem;
        FileSystem.Materialization.MaterializedFile materialized;

        try
        {
            if (!originFs.Capabilities.HasFlag(FileSystemCapabilities.NativePaths))
            {
                var info = await originFs.GetFileInfoAsync(item.FullPath);
                if (info != null && info.Size > FileSystem.Materialization.MaterializationLimits.ArchiveBrowseWarnBytes)
                {
                    var confirmed = StyledMessageBox.Show(
                        L.GetString("Archive.ConfirmDownload", item.Name, Utils.FormatUtils.FormatSize(info.Size)),
                        L.GetString("Archive.Title"), MsgBoxButtons.YesNo, MsgBoxIcon.Warning, this) == MsgBoxResult.Yes;
                    if (!confirmed) return;
                }
            }

            materialized = await panel.ViewModel.MaterializeAsync(
                originFs, item.FullPath, FileSystem.Materialization.MaterializeOptions.ForArchiveRead, CancellationToken.None);
        }
        // Was `catch (IOException ex)` only - the caller (OnArchiveEntered) is async void, so
        // anything this doesn't catch becomes an unhandled-exception crash instead of an error
        // dialog. WebDAV surfaces HttpRequestException, SSH.NET surfaces SshException/
        // SftpPermissionDeniedException, neither derives from IOException - a permission error or a
        // dropped connection while entering a remote archive used to crash the whole app.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Error($"Failed to materialize archive: {item.FullPath}", ex);
            StyledMessageBox.Show(ex.Message, L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
            return;
        }

        var format = ArchiveFormatRegistry.Detect(materialized.LocalPath);
        IFileSystem? archiveFs = format?.CreateFileSystem(materialized.LocalPath);
        if (archiveFs == null)
        {
            materialized.Dispose();
            StyledMessageBox.Show(L.GetString("Archive.UnsupportedFormat", item.Name),
                L.GetString("Archive.Title"), MsgBoxButtons.OK, MsgBoxIcon.Information, this);
            return;
        }

        // Passthrough (a local item) needs no wrapping at all - nothing was copied, mutations
        // already land on the real archive. A genuinely downloaded copy is wrapped so any mutation
        // marks the lease dirty, which is what lets ReleaseArchiveLease know later whether there is
        // anything worth offering to write back.
        if (!materialized.IsPassthrough)
            archiveFs = new DirtyTrackingFileSystem(archiveFs, materialized.MarkDirty);

        try
        {
            await panel.ViewModel.AttachArchiveLeaseAsync(materialized);
            panel.ViewModel.CurrentFileSystem = archiveFs;
            var archivePath = ArchivePath.MakePath(materialized.LocalPath, "");
            await panel.ViewModel.NavigateAsync(archivePath);
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to enter archive: {item.FullPath}", ex);
            StyledMessageBox.Show(ex.Message, L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
        }
    }

    private void OnPackRequested(object? sender, (IReadOnlyList<FileSystemItem> files, string sourcePath, string destPath) e)
    {
        var L = LocalizationService.Current;
        var settings = SettingsService.Load();
        var suggestedBaseName = SuggestArchiveBaseName(e.files, e.sourcePath);

        using var dlg = new PackDialogForm(suggestedBaseName, e.destPath, settings.DefaultArchiveFormat, settings.DeleteOriginalsAfterPack);
        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.ArchivePath)) return;

        var archivePath = dlg.ArchivePath;

        if (File.Exists(archivePath))
        {
            var result = StyledMessageBox.Show(
                L.GetString("Archive.PackExists", Path.GetFileName(archivePath)),
                L.GetString("Archive.PackTitle"),
                MsgBoxButtons.YesNo, MsgBoxIcon.Question, this);
            if (result != MsgBoxResult.Yes) return;
        }

        try
        {
            var options = new TransferOptions
            {
                CopyTimestamps = settings.CopyTimestamps,
                Compression = dlg.SelectedCompression,
                SkipCompressionForCompressedFiles = settings.SkipCompressionForCompressedFiles,
                AlreadyCompressedExtensions = settings.AlreadyCompressedExtensions.Count > 0 ? settings.AlreadyCompressedExtensions : null,
                Overwrite = true
            };
            _vm.ExecutePack(e.files, archivePath, options, move: dlg.MoveOriginals);
        }
        catch (Exception ex)
        {
            LogService.Error($"Pack failed: {archivePath}", ex);
            StyledMessageBox.Show(L.GetString("Archive.PackFailed", ex.Message),
                L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
        }
    }

    /// <summary>Resolves the per-format compression preference from settings for whichever
    /// archive format <paramref name="destinationPath"/> points into, or null when it isn't an
    /// archive path at all (plain file-to-file transfers ignore <see cref="TransferOptions.Compression"/>
    /// entirely, so there's nothing meaningful to resolve).</summary>
    private static ArchiveCompressionSpec? ResolveCompressionForDestination(string destinationPath, AppSettings settings)
    {
        if (!VfsPath.IsArchive(destinationPath))
            return null;

        var format = ArchiveFormatRegistry.Detect(VfsPath.GetArchiveFile(destinationPath));
        return format == null ? null : ResolveCompressionForFormat(format, settings);
    }

    /// <summary>Looks up the saved preset for <paramref name="format"/>, falling back to Balanced
    /// when nothing is saved yet or the saved value no longer applies to this format.</summary>
    private static ArchiveCompressionSpec ResolveCompressionForFormat(IArchiveFormat format, AppSettings settings)
    {
        if (settings.ArchiveCompression.TryGetValue(format.Id, out var presetName) &&
            Enum.TryParse<CompressionPreset>(presetName, out var preset) &&
            format.SupportedPresets.Contains(preset))
        {
            return new ArchiveCompressionSpec(preset);
        }

        return ArchiveCompressionSpec.Balanced;
    }

    /// <summary>Regex for a split-part file name: <c>&lt;base&gt;.NNN</c> (3+ digits) - matches
    /// <see cref="Operations.CombineOperation"/>'s own pattern. Kept separate (not shared code)
    /// because this one is only ever used for the dialog's informational preview list, never for
    /// the authoritative missing-part check, which stays solely inside <c>CombineOperation</c>.</summary>
    private static readonly Regex SplitPartNameRegex = new(@"^(?<base>.+)\.(?<num>\d{3,})$", RegexOptions.CultureInvariant);

    private void OnSplitRequested(object? sender, (IReadOnlyList<FileSystemItem> files, string destDir) e)
    {
        var L = LocalizationService.Current;
        using var dlg = new SplitDialogForm(e.destDir);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var partSize = dlg.PartSizeBytes;
        if (partSize <= 0)
        {
            StyledMessageBox.Show(L.GetString("Split.InvalidSize"), L.GetString("Common.Error"),
                MsgBoxButtons.OK, MsgBoxIcon.Error, this);
            return;
        }

        _vm.ExecuteSplit(e.files, dlg.DestDir, partSize, dlg.WriteCrc, dlg.DeleteSource);
    }

    private async void OnCombineRequested(object? sender, (FileSystemItem firstPart, string destDir) e)
    {
        // async void: this is a top-level UI event handler (not awaited by anything), same
        // contract as OnFormLoad - exceptions are caught below rather than left to the
        // unhandled-exception path.
        var L = LocalizationService.Current;
        var match = SplitPartNameRegex.Match(e.firstPart.Name);
        if (!match.Success)
        {
            StyledMessageBox.Show(L.GetString("Combine.NotAPart", e.firstPart.Name), L.GetString("Common.Error"),
                MsgBoxButtons.OK, MsgBoxIcon.Error, this);
            return;
        }

        var suggestedName = match.Groups["base"].Value;
        List<string> partNames;
        try
        {
            var fs = _vm.ActivePanel.CurrentFileSystem;
            var siblings = await fs.EnumerateAsync(e.destDir, includeHidden: true).ConfigureAwait(true);
            partNames = siblings
                .Where(entry => !entry.IsDirectory)
                .Select(entry => (entry.Name, Match: SplitPartNameRegex.Match(entry.Name)))
                .Where(t => t.Match.Success && string.Equals(t.Match.Groups["base"].Value, suggestedName, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Error("Combine: failed to list part files", ex);
            StyledMessageBox.Show(ex.Message, L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
            return;
        }

        using var dlg = new CombineDialogForm(suggestedName, e.destDir, partNames);
        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.DestPath)) return;

        // CA2000: ownership transfers to Operations.RunAsync inside ExecuteCombine, which disposes
        // it on completion - see MainViewModel.ExecuteTransfer's own suppression for the same
        // pattern. This method only holds a reference to subscribe StateChanged, never owns it.
#pragma warning disable CA2000
        var op = _vm.ExecuteCombine(e.firstPart.FullPath, dlg.DestPath, dlg.VerifyCrc, dlg.DeleteSource);
#pragma warning restore CA2000
        if (op != null && dlg.VerifyCrc)
            op.StateChanged += OnCombineStateChanged;
    }

    /// <summary>Reports a CRC mismatch after a successful combine - not a failure (the file is
    /// already written either way), just a heads-up. Silent on a verified match or when there was
    /// nothing to verify against (no <c>.crc</c> sidecar - <see cref="CombineOperation.CrcVerified"/>
    /// is null in that case, distinct from a confirmed false).</summary>
    private void OnCombineStateChanged(object? sender, OperationState state)
    {
        if (state is not (OperationState.Completed or OperationState.Failed or OperationState.Canceled))
            return;
        if (sender is CombineOperation op)
            op.StateChanged -= OnCombineStateChanged;
        if (state != OperationState.Completed || sender is not CombineOperation combine || combine.CrcVerified != false)
            return;

        if (InvokeRequired) { BeginInvoke(() => ShowCrcMismatchWarning()); return; }
        ShowCrcMismatchWarning();
    }

    private void ShowCrcMismatchWarning()
    {
        if (!IsHandleCreated) return;
        var L = LocalizationService.Current;
        StyledMessageBox.Show(L.GetString("Combine.CrcMismatch"), L.GetString("Combine.Title"),
            MsgBoxButtons.OK, MsgBoxIcon.Warning, this);
    }

    /// <summary>A single folder names the archive after itself; anything else after its parent.
    /// No extension - <see cref="PackDialogForm"/> appends the selected format's own.</summary>
    private static string SuggestArchiveBaseName(IReadOnlyList<FileSystemItem> files, string sourcePath)
    {
        if (files.Count == 1)
        {
            var name = files[0].Name;
            var ext = FileSystem.FileEntry.GetExtension(name);
            var baseName = ext.Length > 0 ? name[..^ext.Length] : name;
            return baseName.Length > 0 ? baseName : "archive";
        }

        var folder = VfsPath.GetName(sourcePath);
        return string.IsNullOrWhiteSpace(folder) ? "archive" : folder;
    }

    private void OnUnpackRequested(object? sender, (IReadOnlyList<FileSystemItem> archives, string destPath) e)
    {
        var L = LocalizationService.Current;

        var nested = e.archives.Where(a => VfsPath.IsArchive(a.FullPath)).ToList();
        if (nested.Count > 0)
        {
            StyledMessageBox.Show(L.GetString("Archive.NestedUnsupported", nested[0].Name),
                L.GetString("Archive.Title"), MsgBoxButtons.OK, MsgBoxIcon.Information, this);
            return;
        }

        var valid = e.archives.Where(a => ArchiveFormatRegistry.IsSupportedArchiveFile(a.FullPath)).ToList();
        if (valid.Count == 0)
        {
            StyledMessageBox.Show(L.GetString("Archive.UnsupportedFormat", e.archives.FirstOrDefault()?.Name ?? ""),
                L.GetString("Archive.Title"), MsgBoxButtons.OK, MsgBoxIcon.Information, this);
            return;
        }

        using var dlg = new InputDialogForm(L.GetString("Archive.UnpackTitle"), L.GetString("Archive.ChooseTarget"), e.destPath);
        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.Value)) return;

        var destPath = dlg.Value.Trim();

        try
        {
            var settings = SettingsService.Load();
            var options = new TransferOptions
            {
                CopyTimestamps = settings.CopyTimestamps,
                // Compression is irrelevant here - UnpackOperation only ever reads, never writes.
                OverwriteResolver = settings.ConfirmOverwrite ? CreateOverwriteResolver() : null,
                Overwrite = !settings.ConfirmOverwrite
            };
            _vm.ExecuteUnpack(valid, destPath, options);
        }
        catch (Exception ex)
        {
            LogService.Error($"Unpack failed: {destPath}", ex);
            StyledMessageBox.Show(L.GetString("Archive.UnpackFailed", ex.Message),
                L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, this);
        }
    }

    private void OnOperationRejected(object? sender, string reasonKey)
    {
        var L = LocalizationService.Current;
        StyledMessageBox.Show(L.GetString(reasonKey), L.GetString("Archive.Title"),
            MsgBoxButtons.OK, MsgBoxIcon.Information, this);
    }

    private void OpenEditorNew()
    {
#pragma warning disable CA2000 // see the comment on OpenDirectoryTree() above
        var dlg = new EditorForm(null);
#pragma warning restore CA2000
        dlg.FormClosed += (_, _) => dlg.Dispose();
        dlg.Show(this);
    }

    /// <summary>
    /// Opens the search dialog against the active panel's own file system, which is what makes the
    /// same dialog search a local folder, the inside of an archive and a connection with no code
    /// aware of the difference. Choosing a result navigates the panel to the file's folder and puts
    /// the cursor on it.
    /// </summary>
    private void OpenFindFiles()
    {
        var panel = _vm.ActivePanel;
        using var dlg = new FindFilesForm(panel.CurrentFileSystem, panel.CurrentPath);

        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(dlg.SelectedPath)) return;

        var folder = FileSystem.VfsPath.GetParent(dlg.SelectedPath);
        if (string.IsNullOrEmpty(folder)) return;

        var target = dlg.SelectedPath;
        _ = SafeNavigateAndSelectAsync(panel, folder, target);
    }

    private async Task SafeNavigateAndSelectAsync(ViewModels.PanelViewModel panel, string folder, string target)
    {
        try
        {
            await panel.NavigateAsync(folder);
            panel.SelectedItem = panel.Items.FirstOrDefault(
                i => string.Equals(i.FullPath, target, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            LogService.Error($"Could not open the found file's folder: {folder}", ex);
        }
    }

    private void OpenChecksum()
    {
        var files = _vm.ActivePanel.GetSelectedOrActive()
            .Where(f => !f.IsDirectory && !f.IsParent)
            .Select(f => f.FullPath)
            .ToList();
        if (files.Count == 0)
        {
            StyledMessageBox.Show(
                LocalizationService.Current.GetString("Checksum.SelectFiles"),
                LocalizationService.Current.GetString("Checksum.Title"),
                MsgBoxButtons.OK, MsgBoxIcon.Information, this);
            return;
        }
        using var dlg = new ChecksumForm(files);
        dlg.ShowDialog(this);
    }

    private void OpenDiffer()
    {
        var selected = _vm.ActivePanel.GetSelectedOrActive()
            .Where(f => !f.IsDirectory && !f.IsParent)
            .ToList();
        var left = selected.Count > 0 ? selected[0].FullPath : null;
        var right = selected.Count > 1 ? selected[1].FullPath : null;
        using var dlg = new DifferForm(left, right, _vm.ActivePanel.CurrentFileSystem);
        dlg.ShowDialog(this);
    }

    // ═══════════════════════════════════════════
    // RE-LOCALIZATION
    // ═══════════════════════════════════════════

    private void Relocalize()
    {
        // Rebuild menu texts
        var L = LocalizationService.Current;
        LogService.Info($"Relocalize called. Current language: {L.CurrentLanguage}");

        if (_menuStrip.Items.Count >= 6)
        {
            _menuStrip.Items[0].Text = L.GetString("Menu.File");
            _menuStrip.Items[1].Text = L.GetString("Menu.Selection");
            _menuStrip.Items[2].Text = L.GetString("Menu.Commands");
            _menuStrip.Items[3].Text = L.GetString("Menu.View");
            _menuStrip.Items[4].Text = L.GetString("Menu.Config");
            _menuStrip.Items[5].Text = L.GetString("Menu.Help");
        }

        LogService.Info($"Applying {_relocalizeActions.Count} relocalize actions");
        foreach (var action in _relocalizeActions)
            action();

        _leftPanel.ApplyTheme();
        _rightPanel.ApplyTheme();

        _vm.UpdateStatus();

        LogService.Info("Relocalize completed");
    }

    // ═══════════════════════════════════════════
    // THEME
    // ═══════════════════════════════════════════

    private void ApplyTheme()
    {
        var p = ThemeService.Current;
        BackColor = p.Background;
        ForeColor = p.Foreground;
        Font = p.GridFont;

        if (IsHandleCreated)
        {
            NativeControlThemer.ApplyDarkTitleBar(Handle);
            NativeControlThemer.ApplyDarkScrollbars(this);
        }

        ThemeService.StyleMenu(_menuStrip);
        ThemeService.StyleToolStrip(_toolStrip);
        ThemeService.StyleToolStrip(_functionBar);
        ThemeService.StyleStatusStrip(_statusStrip);

        // These were previously only ever set once, at construction, and never refreshed here -
        // so both kept whatever theme was active on first build after a live theme switch.
        _terminalSplitter.BackColor = p.GridLine;
        _splitterOverlay.BackColor = p.SplitterHover;

        _mainSplit.BackColor = p.SplitterNormal;
        _mainSplit.Panel1.BackColor = p.Background;
        _mainSplit.Panel2.BackColor = p.Background;
        _mainSplit.SplitterWidth = 5;
        _mainSplit.Panel1MinSize = 100;
        _mainSplit.Panel2MinSize = 100;
        _mainSplit.BorderStyle = BorderStyle.None;

        _leftPanel.ApplyTheme();
        _rightPanel.ApplyTheme();

        _lblStatus.ForeColor = p.DimForeground;
        _lblStatus.Font = p.StatusBarFont;
        _lblQueue.ForeColor = p.Accent;
        _lblQueue.Font = p.StatusBarFont;
        _lblStatus.Text = _vm.StatusText;
        _lblQueue.Text = _vm.OperationQueueText;

        // Recolor items and regenerate icons (ToolbarIcons' cache was just cleared by
        // ThemeService.ApplyTheme) across the menu bar and both toolbars - one shared helper
        // instead of three near-identical loops.
        ControlThemer.ThemeToolStripItems(_menuStrip.Items, p);
        ControlThemer.ThemeToolStripItems(_toolStrip.Items, p);
        ControlThemer.ThemeToolStripItems(_functionBar.Items, p);

        Invalidate();
        Update();
    }

    private void OnShowExtensionInNameChanged()
    {
        _leftPanel.RefreshFromViewModel();
        _rightPanel.RefreshFromViewModel();
    }

    private void ApplyVisibility()
    {
        var s = SettingsService.Load();
        _toolStrip.Visible = s.ShowToolbar;
        _statusStrip.Visible = s.ShowStatusBar;
        _functionBar.Visible = s.ShowFunctionButtons;
    }

    // ═══════════════════════════════════════════
    // FORM CLOSING
    // ═══════════════════════════════════════════

    /// <summary>
    /// Volume arrival/removal reaches a top-level window as <c>WM_DEVICECHANGE</c> without any
    /// <c>RegisterDeviceNotification</c> call - that is only needed for device-interface classes.
    /// The message is always passed on to the base implementation; the watcher only observes.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        _deviceWatcher.HandleMessage(m.Msg, m.WParam, m.LParam);
        base.WndProc(ref m);
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        // Fires on a thread-pool thread (DeviceChangeWatcher's contract), and the refresh itself
        // must not run on the UI thread anyway - DriveCatalog publishes its results back through
        // its own event, which the panels marshal.
        _ = DriveCatalog.Instance.RefreshAsync();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        var s = SettingsService.Load();
        if (WindowState == FormWindowState.Normal)
        {
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }
        s.WindowMaximized = WindowState == FormWindowState.Maximized;
        s.LeftPath = _vm.LeftPanel.CurrentPath;
        s.RightPath = _vm.RightPanel.CurrentPath;
        s.ShowHidden = _vm.LeftPanel.ShowHidden;
        s.ShowSystem = _vm.LeftPanel.ShowSystem;
        s.FlatView = _vm.LeftPanel.IsFlatView;

        s.TerminalVisible = _terminalVisible;
        // Read the live control height, not the _terminalHeight field - it's never updated
        // when the user drags _terminalSplitter (a standard Splitter resizes the docked
        // control directly without notifying app code).
        s.TerminalHeight = _terminalPanel.Height > 0 ? _terminalPanel.Height : s.TerminalHeight;
        s.OpenTerminalTabs.Clear();
        if (_terminalPanel.SessionManager != null)
            s.OpenTerminalTabs.AddRange(_terminalPanel.SessionManager.Tabs
                .Select(t => $"{t.Shell.Id}|{t.CurrentPath}"));
        s.LastTerminalPath = _terminalPanel.SessionManager?.ActiveTab?.CurrentPath;

        SettingsService.Save(s);
    }

    /// <summary>Centers the split - used only once, on first load. Resizing afterward goes
    /// through ApplySplitRatio so it doesn't undo a user-dragged proportion.</summary>
    private void CenterSplitter()
    {
        _splitRatio = 0.5;
        ApplySplitRatio();
    }

    private double ComputeSplitRatio()
    {
        var denom = _mainSplit.Width - _mainSplit.SplitterWidth;
        return denom > 0 ? (double)_mainSplit.SplitterDistance / denom : 0.5;
    }

    private void ApplySplitRatio()
    {
        if (_mainSplit.Width <= 0) return;
        var target = (int)((_mainSplit.Width - _mainSplit.SplitterWidth) * _splitRatio);
        target = Math.Max(target, _mainSplit.Panel1MinSize + 1);
        target = Math.Min(target, _mainSplit.Width - _mainSplit.Panel2MinSize - _mainSplit.SplitterWidth - 1);
        _mainSplit.SplitterDistance = target;
    }

    private void OnFormResize(object? sender, EventArgs e)
    {
        // Preserves whatever proportion the user last dragged to, instead of recentering the
        // panels on every resize (which used to discard it).
        ApplySplitRatio();

        // Belt-and-braces: ApplySplitRatio's SplitterDistance change cascades a Resize down to
        // each panel's file list, which already repositions its own scrollbar overlay - this
        // covers the (normally redundant) case where that cascade lags behind this event.
        _leftPanel.RefreshScrollbarOverlay();
        _rightPanel.RefreshScrollbarOverlay();
    }

    /// <summary>
    /// Initialize panels with saved or default paths.
    /// </summary>
    /// <summary>Loads saved paths and restores terminal tabs after the window is shown.</summary>
    public async Task InitializeAsync()
    {
        // Fire-and-forget: memoized internally, so by the time anything actually needs the shell
        // list (the "+" new-tab dialog, or Settings' default-shell combo) it's already warm rather
        // than blocking on WSL distro enumeration/registry lookups at that point instead.
        _ = Terminal.Shells.ShellCatalog.DiscoverAsync();

        var s = SettingsService.Load();
        var leftPath = !string.IsNullOrEmpty(s.LeftPath) && Directory.Exists(s.LeftPath)
            ? s.LeftPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var rightPath = !string.IsNullOrEmpty(s.RightPath) && Directory.Exists(s.RightPath)
            ? s.RightPath
            : Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

        await _vm.LeftPanel.NavigateAsync(leftPath);
        await _vm.RightPanel.NavigateAsync(rightPath);
        _vm.SetActivePanel(_vm.LeftPanel);

        _terminalPanel.DefaultPath = s.LastTerminalPath;
        if (s.TerminalVisible && s.OpenTerminalTabs.Count > 0)
        {
            var tabs = s.OpenTerminalTabs
                .Select(entry => entry.Split('|', 2))
                .Where(parts => parts.Length == 2 && Directory.Exists(parts[1]))
                .Select(parts => (ShellId: parts[0], Path: parts[1]));
            await _terminalPanel.RestoreTabsAsync(tabs);
        }
    }
}
