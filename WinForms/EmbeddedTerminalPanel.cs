using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Utils;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace CoderCommander.WinForms;

/// <summary>
/// Embedded terminal panel with tabbed multi-session support.
/// Each tab can run cmd.exe or PowerShell independently with preserved output history.
/// </summary>
public sealed class EmbeddedTerminalPanel : Panel
{
    private TerminalSessionManager? _sessionManager;
    private Dictionary<Guid, TerminalProcessWrapper> _processes = new();
    private Dictionary<Guid, StringBuilder> _outputBuffers = new();
    private readonly Dictionary<Guid, ThemedTabPage> _tabPagesByGuid = new();
    private RichTextBox _outputBox = null!;
    private TextBox _inputBox = null!;
    private ThemedTabControl _tabControl = null!;
    private Panel _sharedContent = null!;
    private RoundedButton _newTabButton = null!;

    /// <summary>Gets the underlying <see cref="TerminalSessionManager"/> that owns tab lifecycle.</summary>
    public TerminalSessionManager? SessionManager => _sessionManager;

    /// <summary>Raised when a tab is created, closed, or the tab count changes.</summary>
    public event EventHandler? TabsChanged;

    /// <summary>Raised when the active tab's output buffer receives new text.</summary>
    public event EventHandler? OutputUpdated;

    public EmbeddedTerminalPanel()
    {
        InitializeComponents();
        ApplyTheme();
        ThemeService.ThemeChanged += OnThemeChanged;
        VisibleChanged += (_, _) => OnVisibleChanged();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private void InitializeComponents()
    {
        BackColor = ThemeService.Current.Background;

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

        // Output/input controls are shared across every tab (the tab strip only switches
        // which session is "active" - RefreshDisplay swaps in that tab's buffered history).
        _sharedContent = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeService.Current.Background
        };

        _outputBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BackColor = ThemeService.Current.Background,
            ForeColor = ThemeService.Current.Foreground,
            Font = ThemeService.Current.MonoFont,
            WordWrap = false,
            BorderStyle = BorderStyle.None,
            Margin = new Padding(0),
            Padding = new Padding(8)
        };
        _sharedContent.Controls.Add(_outputBox);

        _inputBox = new TextBox
        {
            Dock = DockStyle.Bottom,
            BackColor = ThemeService.Current.Background,
            ForeColor = ThemeService.Current.Foreground,
            Font = ThemeService.Current.MonoFont,
            BorderStyle = BorderStyle.FixedSingle,
            Multiline = false,
            Height = 24,
            Padding = new Padding(4, 2, 4, 2),
            Margin = new Padding(0)
        };
        _inputBox.KeyDown += InputBox_KeyDown;
        _sharedContent.Controls.Add(_inputBox);

        AppendOutput("Terminal Panel Ready\r\nUse [+] button to create a new tab\r\nSupported: cmd.exe, PowerShell\r\n\r\n", ThemeService.Current.Foreground);
    }

    private void ApplyTheme()
    {
        if (IsDisposed)
            return;

        var p = ThemeService.Current;

        BackColor = p.Background;
        if (_outputBox != null)
        {
            _outputBox.BackColor = p.Background;
            _outputBox.ForeColor = p.Foreground;
            // EmbeddedTerminalPanel only ever lives inside MainForm, which isn't a ThemedForm,
            // so nothing else walks in here to apply native dark-scrollbar theming - without
            // this the output box's scrollbar stayed system-light in dark mode.
            NativeControlThemer.ApplyDarkScrollbars(_outputBox);
        }
        if (_inputBox != null)
        {
            _inputBox.BackColor = p.Background;
            _inputBox.ForeColor = p.Foreground;
        }
        if (_sharedContent != null)
            _sharedContent.BackColor = p.Background;
        if (_newTabButton != null)
        {
            _newTabButton.BackColor = p.Background;
            _newTabButton.ForeColor = p.Foreground;
            _newTabButton.HoverColor = p.ToolbarHover;
            _newTabButton.BorderColor = p.GridLine;
            _newTabButton.BorderWidth = 1;
            _newTabButton.Invalidate();
        }
        // ThemedTabControl self-themes via its own ThemeService.ThemeChanged subscription.
    }

    /// <summary>Fallback working directory for new tabs when there's no active tab to inherit from.</summary>
    public string? DefaultPath { get; set; }

    /// <summary>Change the working directory of the active terminal tab.</summary>
    /// <param name="path">New working directory path.</param>
    public void SetWorkingDirectory(string path)
    {
        if (!ShellValidator.IsPathAccessible(path))
            return;

        var activeTab = _sessionManager?.ActiveTab;
        if (activeTab != null)
        {
            activeTab.CurrentPath = path;
            if (_processes.TryGetValue(activeTab.Id, out var process))
                process.SetWorkingDirectory(path);
        }
    }

    /// <summary>Restore previously saved tabs (shell type + working directory) on startup.</summary>
    public void RestoreTabs(IEnumerable<(ShellType Shell, string Path)> tabs)
    {
        foreach (var (shell, path) in tabs)
            AddTerminalTab(shell, path);
    }

    /// <summary>Create a new terminal tab, inheriting the working directory from the active tab (or DefaultPath).</summary>
    public TerminalTab? AddTerminalTab(ShellType shellType) => AddTerminalTab(shellType, null);

    /// <summary>Create a new terminal tab with an explicit working directory.</summary>
    public TerminalTab? AddTerminalTab(ShellType shellType, string? workingDirectory)
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

        var tab = _sessionManager.CreateTab(shellType, seedPath);
        if (tab == null)
            return null;

        // Create process for this tab
        var process = new TerminalProcessWrapper(shellType, tab.CurrentPath);
        _processes[tab.Id] = process;
        _outputBuffers[tab.Id] = new StringBuilder();

        // Wire up process events
        process.OutputReceived += (_, text) => OnProcessOutput(tab.Id, text, false);
        process.ErrorReceived += (_, text) => OnProcessOutput(tab.Id, text, true);
        process.ProcessExited += (_, _) => OnProcessExited(tab.Id);

        if (!process.Start())
        {
            AppendOutput($"Failed to start {shellType.GetDisplayName()}\r\n", ThemeService.Current.Danger);
            CloseTerminalTab(tab.Id);
            return null;
        }

        return tab;
    }

    /// <summary>Close a terminal tab.</summary>
    public void CloseTerminalTab(Guid tabId)
    {
        if (_processes.TryGetValue(tabId, out var process))
        {
            process.Terminate();
            process.Dispose();
            _processes.Remove(tabId);
        }

        if (_outputBuffers.TryGetValue(tabId, out _))
            _outputBuffers.Remove(tabId);

        _sessionManager?.CloseTab(tabId);
        TabsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Switch to a tab.</summary>
    public void SwitchToTab(Guid tabId)
    {
        if (!_sessionManager?.SwitchTab(tabId) ?? false)
            return;

        RefreshDisplay();
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

    /// <summary>Refresh display with active tab output.</summary>
    public void RefreshDisplay()
    {
        _outputBox.Clear();
        var activeTab = _sessionManager?.ActiveTab;
        if (activeTab != null && _outputBuffers.TryGetValue(activeTab.Id, out var buffer))
        {
            var text = buffer.ToString();
            AppendOutput(text, ThemeService.Current.Foreground);
        }
        _inputBox.Focus();
    }

    private void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var page = _tabControl.SelectedPage;
        if (page == null)
            return;

        var tabId = _tabPagesByGuid.FirstOrDefault(kv => kv.Value == page).Key;
        if (tabId != Guid.Empty && tabId != _sessionManager?.ActiveTab?.Id)
            SwitchToTab(tabId);
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
    }

    private void OnTabCreated(object? sender, Guid tabId)
    {
        if (_sessionManager?.GetTab(tabId) is not TerminalTab tab)
            return;

        var page = new ThemedTabPage(tab.GetDisplayName(), _sharedContent);
        _tabPagesByGuid[tabId] = page;
        _tabControl.AddPage(page);
        _tabControl.SelectedIndex = _tabControl.Pages.Count - 1;

        // For the very first page AddPage selects index 0 internally without raising
        // SelectedIndexChanged, so the display would keep showing the previous session's
        // leftover text. Sync the session and display explicitly instead of relying on the
        // event alone.
        if (_sessionManager.ActiveTab?.Id != tabId)
            _sessionManager.SwitchTab(tabId);
        else
            RefreshDisplay();

        TabsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTabClosed(object? sender, Guid tabId)
    {
        if (_tabPagesByGuid.TryGetValue(tabId, out var page))
        {
            _tabControl.RemovePage(page);
            _tabPagesByGuid.Remove(tabId);
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
        }
        RefreshDisplay();
    }

    private void OnProcessOutput(Guid tabId, string text, bool isError)
    {
        if (InvokeRequired)
        {
            try { Invoke(() => OnProcessOutput(tabId, text, isError)); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            return;
        }
        if (IsDisposed)
            return;

        if (!_outputBuffers.TryGetValue(tabId, out var buffer))
            return;

        buffer.Append(text);

        var activeTab = _sessionManager?.ActiveTab;
        if (activeTab?.Id == tabId)
        {
            var color = isError ? ThemeService.Current.Danger : ThemeService.Current.Foreground;
            AppendOutput(text, color);
            OutputUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnProcessExited(Guid tabId)
    {
        if (InvokeRequired)
        {
            try { Invoke(() => OnProcessExited(tabId)); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            return;
        }
        if (IsDisposed)
            return;

        if (_sessionManager?.GetTab(tabId) is TerminalTab tab)
        {
            AppendOutput($"\r\n[Process terminated for {tab.Name}]\r\n", ThemeService.Current.Danger);
            CloseTerminalTab(tabId);
        }
    }

    /// <summary>Show dialog to create new terminal tab.</summary>
    public void ShowNewTabDialog()
    {
        var available = ShellValidator.GetAvailableShells();
        if (available.Count == 0)
        {
            var L = LocalizationService.Current;
            StyledMessageBox.Show(
                L.GetString("Terminal.NoShellAvailable"),
                L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error, FindForm());
            return;
        }

        using (var dlg = new SelectShellDialog(available))
        {
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                AddTerminalTab(dlg.SelectedShell);
            }
        }
    }

    private void AppendOutput(string text, Color color)
    {
        if (InvokeRequired)
        {
            Invoke(() => AppendOutput(text, color));
            return;
        }

        _outputBox.SelectionStart = _outputBox.TextLength;
        _outputBox.SelectionLength = 0;
        _outputBox.SelectionColor = color;
        _outputBox.AppendText(text);
        _outputBox.SelectionStart = _outputBox.TextLength;
        _outputBox.ScrollToCaret();
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return && !e.Control && !e.Shift)
        {
            e.Handled = true;
            var rawText = _inputBox.Text;
            var command = rawText.Trim();

            if (string.IsNullOrEmpty(command))
                return;

            LogService.Info($"Input: raw=[{rawText}] trimmed=[{command}]");
            // Don't output command here - let the shell handle echo output
            _inputBox.Clear();

            if (command.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                var activeTab = _sessionManager?.ActiveTab;
                if (activeTab != null)
                    CloseTerminalTab(activeTab.Id);
                return;
            }

            var tab = _sessionManager?.ActiveTab;
            if (tab != null && _processes.TryGetValue(tab.Id, out var process))
            {
                process.ExecuteCommand(command);
                // Track directory changes for cd/chdir commands
                UpdateDirectoryIfChanged(tab, command);
            }
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            Visible = false;
        }
    }

    // Best-effort heuristics for tracking the shell's working directory by re-parsing the
    // literal command the user typed. There is no cheap way to ask a fully redirected child
    // console process for its real cwd without issuing an extra query command after every
    // input line (which would pollute the terminal's own visible output). The goal here is to
    // eliminate active false positives (silently wrong tracked path) and cover cheap, common
    // gaps - not to achieve 100% shell fidelity. Explicitly out of scope: pushd/popd and
    // Push-Location/Pop-Location (need a directory stack, not a parsing fix), chained commands
    // like "cd Foo && dir" or "cd Foo; ls" (fails validation and is safely skipped today, but
    // properly splitting needs a quote-aware command-line parser), and PowerShell provider
    // paths like "cd HKLM:\" (naturally rejected by Directory.Exists, safe no-op).
    private static readonly Regex CdNoSpaceRegex = new(@"^(cd|chdir)\.\.$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BareDriveRegex = new(@"^[a-zA-Z]:$", RegexOptions.Compiled);
    private static readonly Regex PowerShellPathFlagRegex = new(@"^-Path[:\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PowerShellEnvVarRegex = new(@"\$env:(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private void UpdateDirectoryIfChanged(TerminalTab tab, string command)
    {
        var cmd = command.Trim();

        // Bare drive-letter switch, e.g. "D:" (works in both cmd.exe and PowerShell)
        if (BareDriveRegex.IsMatch(cmd))
        {
            TryApplyNewPath(tab, cmd + "\\");
            return;
        }

        string cmdName;
        string? argsText;
        if (CdNoSpaceRegex.IsMatch(cmd))
        {
            // "cd.." with no space
            cmdName = "cd";
            argsText = "..";
        }
        else
        {
            var parts = cmd.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return;
            cmdName = parts[0].ToLowerInvariant();
            argsText = parts.Length > 1 ? parts[1] : null;
        }

        var isCdCommand = cmdName is "cd" or "chdir" or "set-location" or "sl";
        if (!isCdCommand)
            return;

        var isPowerShell = tab.ShellType == ShellType.PowerShell;

        // Bare cd/sl: PowerShell goes to $HOME; cmd.exe's bare "cd" only prints the cwd
        // (does not change it), so leaving it untracked there is correct, not a gap.
        if (string.IsNullOrWhiteSpace(argsText))
        {
            if (isPowerShell)
                TryApplyNewPath(tab, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            return;
        }

        var rawArgs = argsText.Trim();
        var hasDFlag = false;

        if (!isPowerShell)
        {
            // cmd.exe cross-drive flag: cd /d C:\path
            if (rawArgs.StartsWith("/d ", StringComparison.OrdinalIgnoreCase))
            {
                hasDFlag = true;
                rawArgs = rawArgs[3..].Trim();
            }
        }
        else
        {
            // PowerShell named-parameter form: Set-Location -Path "C:\Foo"
            rawArgs = PowerShellPathFlagRegex.Replace(rawArgs, "").Trim();
        }

        var newPath = rawArgs.Trim('"', '\'');

        // Expand environment variables (%VAR% works in both shells; $env:VAR is PowerShell-only)
        newPath = Environment.ExpandEnvironmentVariables(newPath);
        if (isPowerShell)
        {
            newPath = PowerShellEnvVarRegex.Replace(newPath,
                m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
        }

        if (newPath == "..")
        {
            newPath = Path.GetDirectoryName(tab.CurrentPath) ?? tab.CurrentPath;
        }
        else if (!Path.IsPathRooted(newPath))
        {
            newPath = Path.Combine(tab.CurrentPath, newPath);
        }
        else if (!isPowerShell && !hasDFlag)
        {
            // cmd.exe without /d does NOT actually change the shell's current directory when
            // switching to a different drive - it only updates that drive's remembered
            // directory. Tracking this as a move would be an active false positive.
            var currentDrive = Path.GetPathRoot(tab.CurrentPath);
            var targetDrive = Path.GetPathRoot(newPath);
            if (!string.IsNullOrEmpty(currentDrive) && !string.IsNullOrEmpty(targetDrive) &&
                !string.Equals(currentDrive, targetDrive, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        TryApplyNewPath(tab, newPath);
    }

    private void TryApplyNewPath(TerminalTab tab, string candidatePath)
    {
        if (!ShellValidator.IsPathAccessible(candidatePath))
            return;

        var fullPath = Path.GetFullPath(candidatePath);
        if (string.Equals(fullPath, tab.CurrentPath, StringComparison.OrdinalIgnoreCase))
            return;

        tab.CurrentPath = fullPath;
        LogService.Info($"Terminal directory changed: {tab.CurrentPath}");
        DirectoryChanged?.Invoke(this, new DirectoryChangedEventArgs { TabId = tab.Id, NewPath = tab.CurrentPath });
    }

    private void OnVisibleChanged()
    {
        if (!Visible)
            _inputBox?.Focus();
    }

    // Intercept tab-management hotkeys locally so they work while the terminal's input box
    // has focus. ProcessCmdKey runs before KeyDown/dialog-navigation key routing, so it
    // reliably wins regardless of what the focused TextBox itself does with these key
    // combinations (this is why MainForm's global hotkey guard is not widened instead).
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.T:
                ShowNewTabDialog();
                return true;
            case Keys.Control | Keys.W:
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

    /// <summary>Raised when the tracked shell working directory changes.</summary>
    public event EventHandler<DirectoryChangedEventArgs>? DirectoryChanged;

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

            // Each wrapper's Dispose() kills its process tree and blocks up to ~1s waiting for it
            // to exit; doing that one tab at a time could stall closing the app/panel for several
            // seconds with multiple terminal tabs open. Run them concurrently instead.
            var disposeTasks = _processes.Values
                .Where(p => p != null)
                .Select(p => Task.Run(() => p!.Dispose()))
                .ToArray();
            Task.WaitAll(disposeTasks, TimeSpan.FromSeconds(3));

            _processes.Clear();
            _outputBuffers.Clear();
            _tabPagesByGuid.Clear();
            _sessionManager?.Dispose();
            _outputBox?.Dispose();
            _inputBox?.Dispose();
            _sharedContent?.Dispose();
            _tabControl?.Dispose();
            _newTabButton?.Dispose();
        }
        base.Dispose(disposing);
    }
}
