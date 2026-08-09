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
    private readonly TerminalKeyBindings _keyBindings = TerminalKeyBindings.WindowsTerminalPreset();

    /// <summary>Loop-guard for the push (panel -&gt; shell) / report (shell -&gt; panel) cwd-sync
    /// cycle: a path just pushed via <see cref="SetWorkingDirectory"/>, so the shell's own
    /// bootstrap reporting that same path back a moment later doesn't bounce straight back into
    /// another panel navigation. Keyed by tab id; cleared once consumed or once it expires.</summary>
    private readonly Dictionary<Guid, (string NormalizedPath, DateTime UntilUtc)> _suppressCwdReport = new();
    private static readonly TimeSpan CwdReportSuppressWindow = TimeSpan.FromSeconds(3);

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
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

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
        _tabControl = new ThemedTabControl { Dock = DockStyle.Fill };
        _tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
        _tabControl.TabRightClicked += TabControl_TabRightClicked;
        Controls.Add(_tabControl);

        _newTabButton = new RoundedButton
        {
            Text = "+",
            Width = 32,
            Height = 32,
            Font = ThemeService.Current.ButtonGlyphFont,
            Cursor = Cursors.Hand,
            Margin = new Padding(6, 1, 0, 1),
            CornerRadius = 0,
            UseGradient = false,
            DrawShadow = false
        };
        _newTabButton.Click += (_, _) => ShowNewTabDialog();
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
            _newTabButton.BackColor = p.Background;
            _newTabButton.ForeColor = p.Foreground;
            _newTabButton.HoverColor = p.ToolbarHover;
            _newTabButton.BorderColor = p.GridLine;
            _newTabButton.BorderWidth = 1;
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
            var distro = session.Shell.Id.StartsWith(ShellIds.WslPrefix, StringComparison.Ordinal)
                ? session.Shell.Id[ShellIds.WslPrefix.Length..]
                : session.Shell.Id;
            if (!new WslPathMapper(distro).TryToWsl(path, out shellPath))
                return; // e.g. a UNC path with no automount-root equivalent in this distro
        }

        if (!ShellCwdQuoting.TryBuildCd(session.Shell.Family, shellPath, out var command))
            return;

        activeTab.CurrentPath = path;
        _suppressCwdReport[activeTab.Id] = (NormalizePath(path), DateTime.UtcNow + CwdReportSuppressWindow);
        session.SendInput(System.Text.Encoding.UTF8.GetBytes(command));
    }

    private static string NormalizePath(string path) =>
        path.TrimEnd('\\').ToUpperInvariant();

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
            session = TerminalSession.Start(shell, seedPath, cols: 80, rows: 24, scrollbackLines: 5000);
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to start shell \"{shell.Id}\"", ex);
            var L = LocalizationService.Current;
            StyledMessageBox.Show(ex.Message, L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, FindForm());
            return null;
        }

        var tab = _sessionManager.CreateTab(shell, session.Name, seedPath);
        if (tab == null)
        {
            _ = session.DisposeAsync().AsTask();
            return null;
        }

        _sessions[tab.Id] = session;
        session.Exited += _ => OnSessionExited(tab.Id);
        session.Screen.CwdReported += path => OnSessionCwdReported(tab.Id, path);

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
            _ = Task.Run(() => session.DisposeAsync().AsTask());
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

    private void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var page = _tabControl.SelectedPage;
        if (page == null)
            return;

        var tabId = _tabPagesByGuid.FirstOrDefault(kv => kv.Value == page).Key;
        if (tabId != Guid.Empty && tabId != _sessionManager?.ActiveTab?.Id)
            SwitchToTab(tabId);

        FocusTerminalContent(page.Content);
    }

    private void TabControl_TabRightClicked(object? sender, int index)
    {
        if (index < 0 || index >= _tabControl.Pages.Count)
            return;

        var page = _tabControl.Pages[index];
        var tabId = _tabPagesByGuid.FirstOrDefault(kv => kv.Value == page).Key;
        if (tabId == Guid.Empty || _sessionManager?.GetTab(tabId) is not TerminalTab tab)
            return;

        var L = LocalizationService.Current;
        using var dlg = new InputDialogForm(L.GetString("Input.Rename"), L.GetString("Input.RenamePrompt"), tab.Name);
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.Value) && dlg.Value != tab.Name)
            _sessionManager.RenameTab(tabId, dlg.Value);
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
        if (_sessionManager?.GetTab(tabId) is not TerminalTab tab || !_sessions.TryGetValue(tabId, out var session))
            return;

        var view = new TerminalTabView(session, _keyBindings);
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
        if (_tabPagesByGuid.TryGetValue(tabId, out var page))
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
        if (_tabPagesByGuid.TryGetValue(tabId, out var page))
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

            // Each session's teardown blocks up to a few seconds waiting for its process tree to
            // exit; doing that one tab at a time could stall closing the app for several seconds
            // with multiple terminal tabs open. Run them concurrently instead.
            var disposeTasks = _sessions.Values.Select(s => Task.Run(() => s.DisposeAsync().AsTask())).ToArray();
            Task.WaitAll(disposeTasks, TimeSpan.FromSeconds(5));
            _sessions.Clear();

            foreach (var page in _tabPagesByGuid.Values)
                page.Content.Dispose();
            _tabPagesByGuid.Clear();

            _sessionManager?.Dispose();
            _tabControl?.Dispose();
            _newTabButton?.Dispose();
        }
        base.Dispose(disposing);
    }
}
