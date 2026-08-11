using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Terminal;
using CoderCommander.Terminal.Input;
using CoderCommander.Terminal.Shells;
using CoderCommander.Terminal.Ui;
using CoderCommander.Utils;

namespace CoderCommander.WinForms;

/// <summary>
/// Embedded terminal panel with tabbed multi-session support. Each tab owns its own
/// <see cref="TerminalSession"/> (a real ConPTY-backed shell) rendered by its own
/// <see cref="TerminalCanvas"/> - unlike the pre-rewrite panel, tabs no longer share a single
/// output/input control pair.
/// </summary>
public sealed class EmbeddedTerminalPanel : Panel
{
    private TerminalSessionManager? _sessionManager;
    private readonly Dictionary<Guid, TerminalSession> _sessions = new();
    private readonly Dictionary<Guid, ThemedTabPage> _tabPagesByGuid = new();
    private ThemedTabControl? _tabControl;
    private RoundedButton? _newTabButton;
    private readonly ToolTip _newTabTooltip = new();
    // Not a fixed field: re-read on every tab creation so a settings change (SettingsForm's
    // Terminal tab) takes effect for the next new/restored tab without needing to reload the
    // whole panel - an already-open tab's canvas keeps whatever table it was created with.
    private static TerminalKeyBindings LoadKeyBindings()
    {
        var s = SettingsService.Load();
        return TerminalKeyBindings.FromSettings(s.TerminalKeyBindingPreset, s.TerminalCustomKeyBindings);
    }

    /// <summary>Loop-guard for the push (panel -&gt; shell) / report (shell -&gt; panel) cwd-sync
    /// cycle: a path just pushed via <see cref="SetWorkingDirectory"/>, so the shell's own
    /// bootstrap reporting that same path back a moment later doesn't bounce straight back into
    /// another panel navigation. Keyed by tab id; cleared once consumed or once it expires.</summary>
    private readonly Dictionary<Guid, (string NormalizedPath, DateTime UntilUtc)> _suppressCwdReport = new();
    private static readonly TimeSpan CwdReportSuppressWindow = TimeSpan.FromSeconds(3);
    private readonly System.Windows.Forms.Timer _cwdSweepTimer;

    /// <summary>Gets the underlying <see cref="TerminalSessionManager"/> that owns tab lifecycle.</summary>
    public TerminalSessionManager? SessionManager => _sessionManager;

    /// <summary>Raised when a tab is created, closed, or the tab count changes.</summary>
    public event EventHandler? TabsChanged;

    /// <summary>Raised when the tracked shell working directory changes for the active tab.</summary>
    public event EventHandler<DirectoryChangedEventArgs>? DirectoryChanged;

    /// <summary>Raised from a tab's right-click "Show in panel" menu item with a detected
    /// filesystem path - <c>MainForm</c> navigates the active file panel there.</summary>
    public event EventHandler<string>? ShowPathInPanelRequested;

    public EmbeddedTerminalPanel()
    {
        InitializeComponents();
        ApplyTheme();
        ThemeService.ThemeChanged += OnThemeChanged;
        LocalizationService.Current.LanguageChanged += OnLanguageChanged;

        _cwdSweepTimer = new System.Windows.Forms.Timer { Interval = 10_000 };
        _cwdSweepTimer.Tick += (_, _) => SweepExpiredCwdGuards();
        _cwdSweepTimer.Start();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    /// <summary>Picks up the close button's tooltip text on a live language switch - every other
    /// localized string in this panel is looked up fresh at the moment it's shown (dialogs, error
    /// messages), but the tooltip is attached once when a tab button is built and would otherwise
    /// keep showing whatever language was active at that point.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_tabControl == null) return;
        _tabControl.CloseButtonTooltip = LocalizationService.Current.GetString("Terminal.CloseTab");
        _tabControl.RefreshTabStrip();
        if (_newTabButton != null)
        {
            var newTab = LocalizationService.Current.GetString("Terminal.NewTab");
            _newTabTooltip.SetToolTip(_newTabButton, newTab);
            _newTabButton.AccessibleName = newTab;
        }
    }

    private void InitializeComponents()
    {
        BackColor = ThemeService.Current.Background;

        if (!OsVersion.IsConPtySupported)
        {
            // No ConPTY API at all on this build - per the approved rewrite plan there is no
            // fallback to the old pipe-based implementation, just this message. AddTerminalTab/
            // ShowNewTabDialog/RestoreTabsAsync all no-op when _sessionManager is null.
            Controls.Add(new UnsupportedOsPanel());
            return;
        }

        _sessionManager = new TerminalSessionManager();
        _sessionManager.TabCreated += OnTabCreated;
        _sessionManager.TabClosed += OnTabClosed;
        _sessionManager.TabActivated += OnTabActivated;
        _sessionManager.TabRenamed += OnTabRenamed;

        // Fully owner-drawn, theme-aware tab strip (native TabControl chrome ignores
        // BackColor/ForeColor and stays light in dark mode).
        _tabControl = new ThemedTabControl
        {
            Dock = DockStyle.Fill,
            ShowCloseButtons = true,
            CloseButtonTooltip = LocalizationService.Current.GetString("Terminal.CloseTab")
        };
        _tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
        _tabControl.TabRightClicked += TabControl_TabRightClicked;
        _tabControl.TabCloseClicked += TabControl_TabCloseClicked;
        Controls.Add(_tabControl);

        // A drawn "+" rather than the "+" character: the glyph's weight and optical centring
        // depend on the UI font, so it never quite matched the tab strip's close glyphs.
        // ToolbarIcons draws both, so they are now the same stroke on the same grid.
        _newTabButton = new RoundedButton
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
            // Icon-only, so the caption can no longer serve as its accessible name - without
            // this the button is nameless to a screen reader and unreachable by UIA.
            AccessibleName = LocalizationService.Current.GetString("Terminal.NewTab"),
            AccessibleRole = AccessibleRole.PushButton,
            // WinForms surfaces Control.Name as the UIA AutomationId, which is the only stable
            // handle on this button: its caption is gone (it draws an icon now) and its accessible
            // name is localized. TerminalCanvas is identified the same way, for the same reason.
            Name = "TerminalNewTabButton",
        };
        // The role carries the whole borderless icon-button look, so ControlThemer reapplies it on
        // every theme switch. Previously this was styled by hand below, which left the button one
        // untagged control away from being reset to the bordered, padded dialog-button shape.
        _newTabButton.Role = ThemeRole.ToolbarButton;
        _newTabButton.Click += (_, _) => ShowNewTabDialog();
        _newTabTooltip.SetToolTip(_newTabButton, LocalizationService.Current.GetString("Terminal.NewTab"));
        _tabControl.SetTrailingControl(_newTabButton);
    }

    private void ApplyTheme()
    {
        if (IsDisposed)
            return;

        var p = ThemeService.Current;
        BackColor = p.Background;
        if (_newTabButton != null)
        {
            // Colours come from ThemeRole.ToolbarButton via ControlThemer. Only the icon is
            // repainted here: it is drawn against the new palette and has no role to carry it.
            ControlThemer.ThemeSingleControl(_newTabButton, p);
            _newTabButton.Image = ToolbarIcons.Get("plus");
            _newTabButton.Invalidate();
        }
        // ThemedTabControl and each tab's TerminalCanvas self-theme via their own
        // ThemeService.ThemeChanged subscriptions.
    }

    /// <summary>Fallback working directory for new tabs when there's no active tab to inherit from.</summary>
    public string? DefaultPath { get; set; }

    /// <summary>Change the working directory of the active terminal tab (programmatic push, not
    /// something the user typed) - injects a <c>cd</c>-equivalent as if typed. Silently does
    /// nothing if: the path isn't accessible, it's already where the tab is tracked as being
    /// (normalized, case-insensitive - also what breaks the push/report loop with
    /// <see cref="OnSessionCwdReported"/>), the alt-screen is active (never type into a running
    /// full-screen TUI like vim/htop on the user's behalf), or - a heuristic, not a precise
    /// shell-idle check like the OSC 133 prompt marks a later phase could add - the cursor isn't
    /// at the start of a line (probably mid-command, not sitting at an empty prompt).</summary>
    public void SetWorkingDirectory(string path)
    {
        if (!ShellValidator.IsPathAccessible(path))
            return;

        var activeTab = _sessionManager?.ActiveTab;
        if (activeTab == null || !_sessions.TryGetValue(activeTab.Id, out var session))
            return;

        if (NormalizePath(path) == NormalizePath(activeTab.CurrentPath))
            return;
        if (session.Screen.IsAltScreenActive || session.Screen.CursorCol != 0)
            return;

        var shellPath = path;
        if (session.Shell.Family is ShellFamily.Wsl)
        {
            var distro = ShellIds.DistroNameFromShellId(session.Shell.Id);
            if (!new WslPathMapper(distro).TryToWsl(path, out shellPath))
                return; // e.g. a UNC path with no automount-root equivalent in this distro
        }

        if (!ShellCwdQuoting.TryBuildCd(session.Shell.Family, shellPath, out var command))
            return;

        activeTab.CurrentPath = path;
        SweepExpiredCwdGuards();
        _suppressCwdReport[activeTab.Id] = (NormalizePath(path), DateTime.UtcNow + CwdReportSuppressWindow);
        session.SendInput(System.Text.Encoding.UTF8.GetBytes(command));
    }

    private static string NormalizePath(string path) =>
        path.TrimEnd('\\').ToUpperInvariant();

    /// <summary>Removes expired entries from the cwd-report loop-guard dictionary. Called before
    /// each new insertion to prevent unbounded growth when the shell doesn't report back.</summary>
    private void SweepExpiredCwdGuards()
    {
        var now = DateTime.UtcNow;
        var expired = new List<Guid>();
        foreach (var (id, entry) in _suppressCwdReport)
        {
            if (now > entry.UntilUtc)
                expired.Add(id);
        }
        foreach (var id in expired)
            _suppressCwdReport.Remove(id);
    }

    /// <summary>Restore previously saved tabs (shell id + working directory) on startup. Unknown
    /// shell ids (e.g. a WSL distro that's since been uninstalled) are silently skipped.</summary>
    public async Task RestoreTabsAsync(IEnumerable<(string ShellId, string Path)> tabs)
    {
        if (_sessionManager == null) return;
        var available = await ShellCatalog.DiscoverAsync().ConfigureAwait(true);
        foreach (var (shellId, path) in tabs)
        {
            var shell = available.FirstOrDefault(s => s.Id == shellId);
            if (shell != null)
                AddTerminalTab(shell, path);
        }
    }

    /// <summary>Create a new terminal tab, inheriting the working directory from the active tab (or DefaultPath).</summary>
    public TerminalTab? AddTerminalTab(ShellDescriptor shell) => AddTerminalTab(shell, null);

    /// <summary>Create a new terminal tab with an explicit working directory.</summary>
    public TerminalTab? AddTerminalTab(ShellDescriptor shell, string? workingDirectory)
    {
        if (_sessionManager == null)
            return null;

        if (_sessionManager.Tabs.Count >= _sessionManager.MaxConcurrentSessions)
        {
            var L = LocalizationService.Current;
            StyledMessageBox.Show(
                L.GetString("Terminal.MaxTabsReached", _sessionManager.MaxConcurrentSessions),
                L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Warning, FindForm());
            return null;
        }

        // A brand-new tab should reflect where the user currently is in the file manager
        // (DefaultPath, kept live by MainForm.PushActivePathToTerminal), not wherever a
        // previously-opened tab happened to navigate to.
        var seedPath = ShellValidator.ValidateOrDefaultPath(
            workingDirectory ?? DefaultPath ?? _sessionManager.ActiveTab?.CurrentPath);

        TerminalSession session;
        try
        {
            // Ownership transfers to _sessions[tabId] inside CreateTab's beforeNotify callback;
            // if CreateTab fails, the session is disposed in the null-check below.
#pragma warning disable CA2000 // Dispose owned by _sessions dictionary or explicitly disposed below
            session = TerminalSession.Create(shell, seedPath, scrollbackLines: 5000);
#pragma warning restore CA2000
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to start shell \"{shell.Id}\"", ex);
            var L = LocalizationService.Current;
            StyledMessageBox.Show(ex.Message, L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, FindForm());
            return null;
        }

        // _sessions must be populated - and the Exited/CwdReported subscriptions attached - before
        // TabCreated fires: CreateTab raises it synchronously, and OnTabCreated looks the session up
        // by tab ID immediately. Doing this after CreateTab returns is too late (the ID isn't known
        // that early anyway) and previously made OnTabCreated silently no-op, leaving a live session
        // with no visible tab.
        var tab = _sessionManager.CreateTab(shell, session.Name, seedPath, beforeNotify: tabId =>
        {
            _sessions[tabId] = session;
            session.Exited += _ => OnSessionExited(tabId);
            session.Screen.CwdReported += path => OnSessionCwdReported(tabId, path);
        });
        if (tab == null)
        {
            _ = session.DisposeAsync().AsTask();
            return null;
        }

        return tab;
    }

    /// <summary>Close a terminal tab.</summary>
    public void CloseTerminalTab(Guid tabId)
    {
        if (_sessions.TryGetValue(tabId, out var session))
        {
            _sessions.Remove(tabId);
            // Teardown blocks up to a few seconds waiting on the process tree; run it in the
            // background so closing a single tab doesn't freeze the window.
            _ = Task.Run(async () =>
            {
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Services.LogService.Error($"Terminal session {tabId} teardown failed", ex);
                }
            });
        }
        _suppressCwdReport.Remove(tabId);

        _sessionManager?.CloseTab(tabId);
        TabsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Switch to a tab.</summary>
    public void SwitchToTab(Guid tabId)
    {
        if (!_sessionManager?.SwitchTab(tabId) ?? false)
            return;
    }

    /// <summary>Switch to next tab.</summary>
    public void NextTab()
    {
        if (_sessionManager == null || _sessionManager.Tabs.Count == 0)
            return;

        var currentIndex = _sessionManager.ActiveTab != null
            ? _sessionManager.GetTabIndex(_sessionManager.ActiveTab.Id)
            : -1;

        var nextIndex = (currentIndex + 1) % _sessionManager.Tabs.Count;
        if (nextIndex >= 0 && nextIndex < _sessionManager.Tabs.Count)
            SwitchToTab(_sessionManager.Tabs[nextIndex].Id);
    }

    /// <summary>Switch to previous tab.</summary>
    public void PreviousTab()
    {
        if (_sessionManager == null || _sessionManager.Tabs.Count == 0)
            return;

        var currentIndex = _sessionManager.ActiveTab != null
            ? _sessionManager.GetTabIndex(_sessionManager.ActiveTab.Id)
            : -1;

        var prevIndex = (currentIndex - 1 + _sessionManager.Tabs.Count) % _sessionManager.Tabs.Count;
        if (prevIndex >= 0 && prevIndex < _sessionManager.Tabs.Count)
            SwitchToTab(_sessionManager.Tabs[prevIndex].Id);
    }

    // The _tabControl null checks in this handler and the five below are unreachable in practice:
    // every one of them is only ever invoked through an event subscribed inside
    // InitializeComponents() *after* _tabControl is assigned, so a null _tabControl (the
    // ConPTY-unsupported early return) means the handler was never wired at all. They are here
    // because that is a non-local invariant the compiler cannot see - and because no-op-when-the-
    // terminal-never-initialized is already this panel's documented behaviour (see
    // OnLanguageChanged, which needs its guard for real: it is subscribed unconditionally).
    private void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_tabControl?.SelectedPage is not { } page)
            return;

        var tabId = _tabPagesByGuid.FirstOrDefault(kv => kv.Value == page).Key;
        if (tabId != Guid.Empty && tabId != _sessionManager?.ActiveTab?.Id)
            SwitchToTab(tabId);

        FocusTerminalContent(page.Content);
    }

    private void TabControl_TabRightClicked(object? sender, int index)
    {
        if (_tabControl == null || index < 0 || index >= _tabControl.Pages.Count)
            return;

        var page = _tabControl.Pages[index];
        var tabId = _tabPagesByGuid.FirstOrDefault(kv => kv.Value == page).Key;
        if (tabId == Guid.Empty || _sessionManager?.GetTab(tabId) is not TerminalTab tab)
            return;

        if (!_sessions.TryGetValue(tabId, out var session))
            return;

        var L = LocalizationService.Current;
#pragma warning disable CA2000 // Ownership transferred to Closed event handler
        var menu = new ContextMenuStrip();
#pragma warning restore CA2000
        menu.Closed += (_, _) => menu.Dispose();
        menu.Items.Add(new ToolStripMenuItem(L.GetString("Input.Rename"), null, (_, _) =>
        {
            using var dlg = new InputDialogForm(L.GetString("Input.Rename"), L.GetString("Input.RenamePrompt"), tab.Name);
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.Value) && dlg.Value != tab.Name)
                _sessionManager.RenameTab(tabId, dlg.Value);
        }));
        menu.Items.Add(new ToolStripMenuItem(L.GetString("Terminal.CopyPath"), null, (_, _) =>
        {
            if (!string.IsNullOrEmpty(session.CurrentPath))
                ClipboardHelper.TrySetClipboard(session.CurrentPath);
        }));
        menu.Show(_tabControl, _tabControl.PointToClient(Cursor.Position));
    }

    /// <summary>Handles a click on a tab's own close ("x") button - resolves the button strip
    /// index back to the tab's <see cref="Guid"/> (the index is only stable for the duration of
    /// this callback; tabs can be reordered/removed by the time async work would resume) and tears
    /// the tab down through the same path as the hotkey/menu Close Tab command.</summary>
    private void TabControl_TabCloseClicked(object? sender, int index)
    {
        if (_tabControl == null || index < 0 || index >= _tabControl.Pages.Count)
            return;

        var page = _tabControl.Pages[index];
        var tabId = _tabPagesByGuid.FirstOrDefault(kv => kv.Value == page).Key;
        if (tabId == Guid.Empty)
            return;

        CloseTerminalTab(tabId);
    }

    private void OnTabRenamed(object? sender, (Guid TabId, string NewName) e)
    {
        if (_tabPagesByGuid.TryGetValue(e.TabId, out var page))
        {
            page.Text = e.NewName;
            page.RefreshTab();
        }
        if (_sessions.TryGetValue(e.TabId, out var session))
            session.Name = e.NewName;
    }

    private void OnTabCreated(object? sender, Guid tabId)
    {
        if (_tabControl == null || _sessionManager?.GetTab(tabId) is not TerminalTab tab
            || !_sessions.TryGetValue(tabId, out var session))
            return;

        var view = new TerminalTabView(session, LoadKeyBindings());
        view.Canvas.ActionRequested += (_, action) => OnCanvasActionRequested(tabId, action);
        view.Canvas.ShowPathInPanelRequested += (_, path) => ShowPathInPanelRequested?.Invoke(this, path);
        session.Screen.TitleChanged += () => OnScreenTitleChanged(tabId);

        var page = new ThemedTabPage(tab.GetDisplayName(), view);
        _tabPagesByGuid[tabId] = page;
        _tabControl.AddPage(page);
        _tabControl.SelectedIndex = _tabControl.Pages.Count - 1;

        // For the very first page AddPage selects index 0 internally without raising
        // SelectedIndexChanged - sync the session explicitly instead of relying on the event alone.
        if (_sessionManager.ActiveTab?.Id != tabId)
            _sessionManager.SwitchTab(tabId);

        view.Canvas.Focus();

        // Spawn the ConPTY and start reading AFTER the canvas has its final size. BeginInvoke
        // defers execution until all pending layout messages are processed — without this, a late
        // layout pass can resize the canvas AFTER the PTY was created at the wrong dimensions,
        // causing the shell's initial output (version string, copyright) to be parsed into a
        // stale-sized buffer and pushed to scrollback on the first resize.
        view.Canvas.BeginInvoke(() =>
        {
            if (IsDisposed || session.IsExited) return;
            var (cols, rows) = view.Canvas.GetTerminalSize();
            session.StartPty(cols, rows);
        });

        TabsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Focuses the actual interactive control of a tab page's content - a
    /// <see cref="TerminalTabView"/>'s <see cref="TerminalTabView.Canvas"/>, not the wrapper panel
    /// itself (which also hosts the find bar).</summary>
    private static void FocusTerminalContent(Control content)
    {
        if (content is TerminalTabView view) view.Canvas.Focus();
        else content.Focus();
    }

    private void OnTabClosed(object? sender, Guid tabId)
    {
        if (_tabControl != null && _tabPagesByGuid.TryGetValue(tabId, out var page))
        {
            // RemovePage first, so ThemedTabControl un-parents (and stops referencing) this page's
            // Content before it's disposed - disposing a still-parented control out from under a
            // pending re-parent in UpdateTabs() would risk an ObjectDisposedException there.
            _tabControl.RemovePage(page);
            _tabPagesByGuid.Remove(tabId);
            page.Content.Dispose();
        }
        TabsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTabActivated(object? sender, Guid tabId)
    {
        if (_tabControl != null && _tabPagesByGuid.TryGetValue(tabId, out var page))
        {
            for (var i = 0; i < _tabControl.Pages.Count; i++)
            {
                if (_tabControl.Pages[i] == page)
                {
                    _tabControl.SelectedIndex = i;
                    break;
                }
            }
            FocusTerminalContent(page.Content);
        }
    }

    private void OnCanvasActionRequested(Guid tabId, TerminalAction action)
    {
        switch (action)
        {
            case TerminalAction.NewTab: ShowNewTabDialog(); break;
            case TerminalAction.CloseTab: CloseTerminalTab(tabId); break;
            case TerminalAction.NextTab: NextTab(); break;
            case TerminalAction.PrevTab: PreviousTab(); break;
            // Find/ClearBuffer/ResetTerminal/scroll navigation land in later phases.
        }
    }

    private void OnScreenTitleChanged(Guid tabId)
    {
        if (InvokeRequired) { BeginInvoke(() => OnScreenTitleChanged(tabId)); return; }
        if (IsDisposed) return;
        if (!_sessions.TryGetValue(tabId, out var session)) return;

        // Title changes only ever rename the tab - never MainForm.Text (see VtResponder/OscSanitizer
        // doc comments for why an attacker-controlled title must stay confined to the tab strip).
        _sessionManager?.RenameTab(tabId, session.Screen.Title.Length > 0 ? session.Screen.Title : session.Name);
    }

    private void OnSessionCwdReported(Guid tabId, string path)
    {
        if (InvokeRequired) { BeginInvoke(() => OnSessionCwdReported(tabId, path)); return; }
        if (IsDisposed) return;
        if (_sessionManager?.GetTab(tabId) is not TerminalTab tab) return;

        tab.CurrentPath = path;

        // Loop-guard: if this report just confirms a path we pushed ourselves a moment ago (see
        // SetWorkingDirectory), don't bounce it back into another panel navigation - the panel is
        // already there, that's WHY we pushed it.
        if (_suppressCwdReport.TryGetValue(tabId, out var suppress))
        {
            _suppressCwdReport.Remove(tabId);
            if (DateTime.UtcNow <= suppress.UntilUtc && suppress.NormalizedPath == NormalizePath(path))
                return;
        }

        DirectoryChanged?.Invoke(this, new DirectoryChangedEventArgs { TabId = tabId, NewPath = path });
    }

    private void OnSessionExited(Guid tabId)
    {
        if (InvokeRequired) { BeginInvoke(() => OnSessionExited(tabId)); return; }
        if (IsDisposed) return;
        if (_sessionManager?.GetTab(tabId) is not TerminalTab tab) return;

        LogService.Info($"Terminal: shell process exited for tab {tab.Name}");
        CloseTerminalTab(tabId);
    }

    /// <summary>Show dialog to create new terminal tab.</summary>
    public async void ShowNewTabDialog()
    {
        if (_sessionManager == null) return; // unsupported OS - see InitializeComponents

        try
        {
            var available = await ShellCatalog.DiscoverAsync().ConfigureAwait(true);
            if (available.Count == 0)
            {
                var L = LocalizationService.Current;
                StyledMessageBox.Show(
                    L.GetString("Terminal.NoShellAvailable"),
                    L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, FindForm());
                return;
            }

            var preferredShellId = SettingsService.Load().DefaultShellType;
            using var dlg = new SelectShellDialog(available, preferredShellId);
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                AddTerminalTab(dlg.SelectedShell);
        }
        catch (Exception ex)
        {
            // async void: this is the top of the call stack, so an unhandled exception here would
            // surface as WinForms' raw crash dialog instead of the app's own error handling.
            LogService.Error("ShowNewTabDialog failed", ex);
        }
    }

    // Intercept tab-management hotkeys locally so they still work if focus is ever on this panel
    // itself rather than the active tab's TerminalCanvas (which owns its own, richer chord table).
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.Shift | Keys.T:
                ShowNewTabDialog();
                return true;
            case Keys.Control | Keys.Shift | Keys.W:
                if (_sessionManager?.ActiveTab is TerminalTab activeTab)
                    CloseTerminalTab(activeTab.Id);
                return true;
            case Keys.Control | Keys.Tab:
                NextTab();
                return true;
            case Keys.Control | Keys.Shift | Keys.Tab:
                PreviousTab();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    /// <summary>Event arguments for <see cref="DirectoryChanged"/>.</summary>
    public class DirectoryChangedEventArgs : EventArgs
    {
        /// <summary>Id of the tab whose directory changed.</summary>
        public Guid TabId { get; set; }

        /// <summary>New working directory path.</summary>
        public string NewPath { get; set; } = "";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeService.ThemeChanged -= OnThemeChanged;
            LocalizationService.Current.LanguageChanged -= OnLanguageChanged;

            _cwdSweepTimer.Stop();
            _cwdSweepTimer.Dispose();

            // Each session's teardown blocks up to a few seconds waiting for its process tree to
            // exit; doing that one tab at a time could stall closing the app for several seconds
            // with multiple terminal tabs open. Run them concurrently instead.
            try
            {
                var disposeTasks = _sessions.Values.Select(s => Task.Run(async () =>
                {
                    try
                    {
                        await s.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Services.LogService.Error("Terminal session disposal failed", ex);
                    }
                })).ToArray();
                Task.WaitAll(disposeTasks, TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Services.LogService.Error("Terminal panel bulk disposal failed", ex);
            }
            _sessions.Clear();

            foreach (var page in _tabPagesByGuid.Values)
                page.Content.Dispose();
            _tabPagesByGuid.Clear();

            _sessionManager?.Dispose();
            _tabControl?.Dispose();
            _newTabButton?.Dispose();
            _newTabTooltip.Dispose();
        }
        base.Dispose(disposing);
    }
}
