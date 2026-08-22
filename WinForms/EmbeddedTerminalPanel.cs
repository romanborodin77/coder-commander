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
    /// <summary>Event handler delegates per tab, for proper unsubscription when closing.</summary>
    private readonly Dictionary<Guid, (TerminalSession Session, Action<int> Exited, Action<string> CwdReported, Action TitleChanged, Action BecameIdle, Action BecameBusy)> _tabEventHandlers = new();
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
    private bool _newTabDialogOpen;

    /// <summary>Gets the underlying <see cref="TerminalSessionManager"/> that owns tab lifecycle.</summary>
    public TerminalSessionManager? SessionManager => _sessionManager;

    /// <summary>Raised when the tracked shell working directory changes for the active tab.</summary>
    public event EventHandler<DirectoryChangedEventArgs>? DirectoryChanged;

    /// <summary>Raised from a tab's right-click "Show in panel" menu item with a detected
    /// filesystem path - <c>MainForm</c> navigates the active file panel there.</summary>
    public event EventHandler<string>? ShowPathInPanelRequested;

    /// <summary>Raised for one of the six <c>TerminalAction.App*</c> chords (Copy/Move/MakeDir/
    /// Delete/Refresh/ChangeDir - F5/F6/F7/F8/Ctrl+R/Ctrl+L by default) - <c>MainForm</c> maps it
    /// to the matching file-panel <c>CommandIds</c> entry, since this class deliberately has no
    /// reference to <c>CommandEngine</c> itself. Internal, not public, like
    /// <see cref="TerminalAction"/> itself - both stay same-assembly-only on purpose (the doc
    /// comment on <see cref="TerminalAction"/> explains why terminal actions aren't routed through
    /// the public CommandEngine surface).</summary>
    internal event EventHandler<TerminalAction>? AppCommandRequested;

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
    /// nothing if the path isn't accessible or is already where the tab is tracked as being
    /// (normalized, case-insensitive - also what breaks the push/report loop with
    /// <see cref="OnSessionCwdReported"/>). Otherwise, if the shell isn't at a safe moment to type
    /// into right now (alt-screen active - never type into a running full-screen TUI like vim/htop
    /// on the user's behalf - or mid-command), the push is held as <see cref="TerminalTab.PendingCwd"/>
    /// and retried automatically once <see cref="Terminal.Screen.TerminalScreen.BecameIdlePrompt"/>
    /// fires (see <see cref="OnScreenBecameIdle"/>) - never silently dropped the way it used to be.
    /// <para>
    /// "Safe to type into" itself prefers the shell's own word on it
    /// (<see cref="Terminal.Screen.TerminalScreen.IsAtIdlePrompt"/>, driven by OSC 133 prompt marks
    /// injected by <see cref="Terminal.Shells.ShellBootstrap"/>) and only falls back to the old
    /// cursor-at-column-0 heuristic for a session where no OSC 133 mark has ever arrived
    /// (<see cref="Terminal.Screen.TerminalScreen.HasShellIntegration"/> false) - a shell with no
    /// prompt-mark support gets the same (imprecise, but not regressed) behavior as before.
    /// </para></summary>
    public void SetWorkingDirectory(string path)
    {
        if (!ShellValidator.IsPathAccessible(path))
            return;

        var activeTab = _sessionManager?.ActiveTab;
        if (activeTab == null || !_sessions.TryGetValue(activeTab.Id, out var session))
            return;

        if (NormalizePath(path) == NormalizePath(activeTab.CurrentPath))
        {
            activeTab.PendingCwd = null; // already there - drop any earlier still-pending push too
            return;
        }

        // Screen state is mutated on the pty reader thread; read it as one consistent snapshot
        // under SyncRoot rather than as two separate unguarded reads (see TerminalScreen's own
        // threading contract).
        bool canSendNow;
        lock (session.Screen.SyncRoot)
        {
            canSendNow = !session.Screen.IsAltScreenActive && (session.Screen.HasShellIntegration
                ? session.Screen.IsAtIdlePrompt
                : session.Screen.CursorCol == 0);
        }

        if (!canSendNow)
        {
            activeTab.PendingCwd = path;
            return;
        }

        activeTab.PendingCwd = null;
        TrySendCd(activeTab, session, path);
    }

    /// <summary>Retries a <see cref="TerminalTab.PendingCwd"/> the moment its session's shell
    /// reports becoming idle - see <see cref="SetWorkingDirectory"/>. Raised from
    /// <see cref="Terminal.Screen.TerminalScreen.BecameIdlePrompt"/>, which fires on the pty
    /// reader thread, so this marshals to the UI thread before touching any tab/session state.</summary>
    private void OnScreenBecameIdle(Guid tabId)
    {
        if (InvokeRequired) { BeginInvoke(() => OnScreenBecameIdle(tabId)); return; }
        if (IsDisposed) return;

        // Update the busy/idle indicator — the shell just became idle, so the busy dot goes away.
        if (_tabControl != null && _tabPagesByGuid.TryGetValue(tabId, out var idlePage)
            && _sessions.TryGetValue(tabId, out var idleSession))
        {
            idlePage.HasShellIntegration = idleSession.Screen.HasShellIntegration;
            idlePage.Busy = false;
            _tabControl.UpdateTabIndicator(idlePage, false);
        }

        if (_sessionManager?.GetTab(tabId) is not TerminalTab tab) return;
        if (string.IsNullOrEmpty(tab.PendingCwd)) return;
        if (!_sessions.TryGetValue(tabId, out var session)) return;

        var pending = tab.PendingCwd;
        tab.PendingCwd = null;
        TrySendCd(tab, session, pending);
    }

    /// <summary>Updates the busy/idle indicator on the tab button when the shell transitions to
    /// busy (command started) or idle (prompt ready). Raised from
    /// <see cref="Terminal.Screen.TerminalScreen.BecameBusy"/>/<see cref="BecameIdlePrompt"/>,
    /// which fire on the pty reader thread, so this marshals to the UI thread first.</summary>
    private void OnScreenBecameBusy(Guid tabId)
    {
        if (InvokeRequired) { BeginInvoke(() => OnScreenBecameBusy(tabId)); return; }
        if (IsDisposed) return;
        if (_tabControl == null) return;
        if (!_tabPagesByGuid.TryGetValue(tabId, out var page)) return;
        if (!_sessions.TryGetValue(tabId, out var session)) return;

        page.HasShellIntegration = session.Screen.HasShellIntegration;
        bool isAtIdle;
        lock (session.Screen.SyncRoot) { isAtIdle = session.Screen.IsAtIdlePrompt; }
        page.Busy = !isAtIdle;
        _tabControl.UpdateTabIndicator(page, page.Busy);
    }

    /// <summary>Builds and sends the actual <c>cd</c>-equivalent for <paramref name="path"/> on
    /// <paramref name="session"/> - the caller is responsible for having already confirmed this is
    /// a safe moment to type into it (see <see cref="SetWorkingDirectory"/>'s idle check). Shared
    /// by the immediate-send path there and the deferred retry in <see cref="OnScreenBecameIdle"/>.</summary>
    private bool TrySendCd(TerminalTab tab, TerminalSession session, string path)
    {
        if (NormalizePath(path) == NormalizePath(tab.CurrentPath))
            return false;

        var shellPath = path;
        if (session.Shell.Family is ShellFamily.Wsl)
        {
            var distro = ShellIds.DistroNameFromShellId(session.Shell.Id);
            if (!new WslPathMapper(distro).TryToWsl(path, out shellPath))
                return false; // e.g. a UNC path with no automount-root equivalent in this distro
        }
        else if (session.Shell.Family is ShellFamily.Bash)
        {
            if (!new BashPathMapper().TryToPosix(path, out shellPath))
                return false; // e.g. a UNC path with no Git-for-Windows mount equivalent
        }

        if (!ShellCwdQuoting.TryBuildCd(session.Shell.Family, shellPath, out var command))
            return false;

        tab.CurrentPath = path;
        SweepExpiredCwdGuards();
        _suppressCwdReport[tab.Id] = (NormalizePath(path), DateTime.UtcNow + CwdReportSuppressWindow);
        // command always ends with a trailing \r (TryBuildCd's own documented contract) - strip it
        // before arming echo suppression, so only the literal typed-looking text ("cd /d ...")
        // disappears and the shell's own newline-to-new-prompt transition renders normally.
        session.SuppressNextEcho(command[..^1]);
        session.SendInput(System.Text.Encoding.UTF8.GetBytes(command));
        return true;
    }

    private static string NormalizePath(string path) =>
        path.Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();

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

            // Store handlers for unsubscription on tab close
            Action<int> exitedHandler = code => OnSessionExited(tabId, code);
            Action<string> cwdHandler = path => OnSessionCwdReported(tabId, path);
            Action titleHandler = () => OnScreenTitleChanged(tabId);
            Action becameIdleHandler = () => OnScreenBecameIdle(tabId);
            Action becameBusyHandler = () => OnScreenBecameBusy(tabId);
            _tabEventHandlers[tabId] = (session, exitedHandler, cwdHandler, titleHandler, becameIdleHandler, becameBusyHandler);

            session.Exited += exitedHandler;
            session.Screen.CwdReported += cwdHandler;
            session.Screen.TitleChanged += titleHandler;
            session.Screen.BecameIdlePrompt += becameIdleHandler;
            session.Screen.BecameBusy += becameBusyHandler;
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
        menu.Items.Add(new ToolStripMenuItem(L.GetString("Input.Rename"), null, (_, _) => ShowRenameTabDialog(tabId, tab)));
        menu.Items.Add(new ToolStripMenuItem(L.GetString("Terminal.CopyPath"), null, (_, _) =>
        {
            if (!string.IsNullOrEmpty(session.CurrentPath))
                ClipboardHelper.TrySetClipboard(session.CurrentPath);
        }));
        menu.Items.Add(new ToolStripSeparator());
        // Next/Previous Tab had localization keys reserved (Terminal.NextTab/PreviousTab) but no
        // menu presence at all - only reachable via their (rebindable) key chords. Always act on
        // the active tab, same as the chord path (NextTab/PreviousTab carry no tab index of their
        // own), regardless of which tab was right-clicked to open this menu.
        menu.Items.Add(new ToolStripMenuItem(L.GetString("Terminal.NextTab"), null, (_, _) => NextTab()) { Enabled = _tabControl.Pages.Count > 1 });
        menu.Items.Add(new ToolStripMenuItem(L.GetString("Terminal.PreviousTab"), null, (_, _) => PreviousTab()) { Enabled = _tabControl.Pages.Count > 1 });
        menu.Show(_tabControl, _tabControl.PointToClient(Cursor.Position));
    }

    /// <summary>Shared by the tab-strip right-click menu's "Rename" item and
    /// <see cref="TerminalAction.RenameTab"/> (a user-rebindable chord - see
    /// <see cref="OnCanvasActionRequested"/> - that previously had no handler at all: rename was
    /// reachable only via right-click, even though the action was already offered for rebinding in
    /// TerminalKeyBindingsForm).</summary>
    private void ShowRenameTabDialog(Guid tabId, TerminalTab tab)
    {
        var L = LocalizationService.Current;
        using var dlg = new InputDialogForm(L.GetString("Input.Rename"), L.GetString("Input.RenamePrompt"), tab.Name);
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.Value) && dlg.Value != tab.Name)
            _sessionManager?.RenameTab(tabId, dlg.Value);
    }

    /// <summary>Renames whichever tab is currently active - what <see cref="TerminalAction.RenameTab"/>
    /// needs, since a key chord (unlike a tab-strip right-click) carries no tab index of its own.</summary>
    private void RenameActiveTab()
    {
        if (_sessionManager?.ActiveTab is not { } tab) return;
        ShowRenameTabDialog(tab.Id, tab);
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
        // TitleChanged handler already subscribed in AddTerminalTab's beforeNotify callback

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
            if (IsDisposed || session.IsExited || view.Canvas.IsDisposed) return;
            var (cols, rows) = view.Canvas.GetTerminalSize();
            session.StartPty(cols, rows);
        });
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
        // Unsubscribe session/screen events to prevent callbacks into a disposed panel.
        // Session may already be removed from _sessions by CloseTerminalTab, so we use the
        // reference stored in _tabEventHandlers instead.
        if (_tabEventHandlers.TryGetValue(tabId, out var handlers))
        {
            handlers.Session.Exited -= handlers.Exited;
            handlers.Session.Screen.CwdReported -= handlers.CwdReported;
            handlers.Session.Screen.TitleChanged -= handlers.TitleChanged;
            handlers.Session.Screen.BecameIdlePrompt -= handlers.BecameIdle;
            handlers.Session.Screen.BecameBusy -= handlers.BecameBusy;
        }
        _tabEventHandlers.Remove(tabId);

        if (_tabControl != null && _tabPagesByGuid.TryGetValue(tabId, out var page))
        {
            // RemovePage first, so ThemedTabControl un-parents (and stops referencing) this page's
            // Content before it's disposed - disposing a still-parented control out from under a
            // pending re-parent in UpdateTabs() would risk an ObjectDisposedException there.
            _tabControl.RemovePage(page);
            _tabPagesByGuid.Remove(tabId);
            page.Content.Dispose();
        }
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

        // Sync the file panel to the newly active tab's own tracked path - a tab switch is a
        // *display* change (show where this tab already is), never a push: SetWorkingDirectory is
        // never called from here. Reuses the same DirectoryChanged event a live OSC7/9;9 cwd
        // report raises; MainForm.OnTerminalDirectoryChanged already gates on e.TabId matching the
        // active tab, which by this point it does. TerminalSessionManager only raises TabActivated
        // from an explicit SwitchTab or from auto-reactivating a sibling when the active tab
        // closes - never from CreateTab, so this never fires during tab restore at startup.
        if (_sessionManager?.GetTab(tabId) is { } tab && !string.IsNullOrEmpty(tab.CurrentPath))
            DirectoryChanged?.Invoke(this, new DirectoryChangedEventArgs { TabId = tabId, NewPath = tab.CurrentPath });
    }

    private void OnCanvasActionRequested(Guid tabId, TerminalAction action)
    {
        switch (action)
        {
            case TerminalAction.NewTab: ShowNewTabDialog(); break;
            case TerminalAction.CloseTab: CloseTerminalTab(tabId); break;
            case TerminalAction.NextTab: NextTab(); break;
            case TerminalAction.PrevTab: PreviousTab(); break;
            case TerminalAction.RenameTab: RenameActiveTab(); break;
            // The six App* actions (F5/F6/F7/F8/Ctrl+R/Ctrl+L by default) delegate to the file
            // panel's own commands - this class has no reference to CommandEngine/MainViewModel
            // (the terminal is deliberately decoupled from the rest of the app's command wiring,
            // per this enum's own doc comment), so it can only raise the action and let whoever
            // owns both sides (MainForm) map it to a real command.
            case TerminalAction.AppCopy:
            case TerminalAction.AppMove:
            case TerminalAction.AppMakeDir:
            case TerminalAction.AppDelete:
            case TerminalAction.AppRefresh:
            case TerminalAction.AppChangeDir:
                AppCommandRequested?.Invoke(this, action);
                break;
            // Find/scroll navigation land in later phases. ClearBuffer/ResetTerminal/SelectAll
            // and the scroll actions are screen-local and handled directly inside
            // TerminalCanvas.DispatchAction - they never reach ActionRequested at all.
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

    private void OnSessionExited(Guid tabId, int exitCode)
    {
        if (InvokeRequired) { BeginInvoke(() => OnSessionExited(tabId, exitCode)); return; }
        if (IsDisposed) return;
        // Only reached for a shell that exited on its own (typed "exit", crashed, connection
        // dropped) - CloseTerminalTab (Ctrl+Shift+W, the tab's close button) already removes the
        // tab from _sessionManager before its own teardown can raise this same Exited event, so
        // GetTab returns null for a user-requested close and this method never gets that far.
        if (_sessionManager?.GetTab(tabId) is not TerminalTab tab) return;

        LogService.Info($"Terminal: shell process exited for tab {tab.Name} (exit code {exitCode})");

        // A clean exit (typing "exit"/"logout", exit code 0) is the ordinary, expected way to
        // close a shell - every mainstream terminal just closes the tab for that, silently. A
        // non-zero code means the shell (or whatever it was running) actually crashed or the
        // connection dropped, which is worth surfacing since the tab otherwise just vanishes with
        // no explanation.
        if (exitCode != 0)
        {
            var L = LocalizationService.Current;
            StyledMessageBox.Show(L.GetString("Terminal.ProcessTerminated.WithCode", tab.Name, exitCode),
                L.GetString("Terminal.ProcessTerminated"), MsgBoxButtons.OK, MsgBoxIcon.Warning, FindForm());
        }

        CloseTerminalTab(tabId);
    }

    /// <summary>Show dialog to create new terminal tab.</summary>
    public async void ShowNewTabDialog()
    {
        if (_sessionManager == null || _newTabDialogOpen) return;
        _newTabDialogOpen = true;

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
        finally
        {
            _newTabDialogOpen = false;
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
                // Budget must exceed PtySession.DisposeAsync's own worst case, not the other way
                // around - its internal steps (2s WaitForExitAsync + 5s ClosePseudoConsole
                // watchdog + 2s reader-thread join, see PtySession.cs) can sum to ~9s for a single
                // stuck session. A 5s outer budget here used to be shorter than that sum, silently
                // abandoning sessions mid-teardown before PtySession's own watchdogs ever got a
                // chance to run. Sessions still dispose concurrently (Task.Run per session above),
                // so this bounds total wall time by the single slowest session, not by tab count.
                Task.WaitAll(disposeTasks, TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                Services.LogService.Error("Terminal panel bulk disposal failed", ex);
            }
            _sessions.Clear();
            _tabEventHandlers.Clear();

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
