using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Terminal.Shells;

namespace CoderCommander.WinForms;

/// <summary>
/// Manages multiple terminal tab sessions.
/// Tracks active tab, handles tab lifecycle (create, close, switch).
/// </summary>
public sealed class TerminalSessionManager : IDisposable
{
    private readonly List<TerminalTab> _tabs = new();
    private Guid? _activeTabId;
    private bool _disposed;

    /// <summary>Gets or sets the maximum number of concurrent terminal sessions allowed.</summary>
    public int MaxConcurrentSessions { get; set; } = 10;
    /// <summary>Gets a read-only view of all active tabs.</summary>
    public IReadOnlyList<TerminalTab> Tabs => _tabs.AsReadOnly();
    /// <summary>Gets the currently active tab, or <c>null</c> if no tabs are open.</summary>
    public TerminalTab? ActiveTab => _activeTabId.HasValue
        ? _tabs.FirstOrDefault(t => t.Id == _activeTabId)
        : null;

    /// <summary>Raised when a new tab is created. The event data is the tab's unique identifier.</summary>
    public event EventHandler<Guid>? TabCreated;
    /// <summary>Raised when a tab is closed. The event data is the closed tab's unique identifier.</summary>
    public event EventHandler<Guid>? TabClosed;
    /// <summary>Raised when the active tab changes. The event data is the newly activated tab's unique identifier.</summary>
    public event EventHandler<Guid>? TabActivated;
    /// <summary>Raised when a tab is renamed. The event data contains the tab identifier and the new display name.</summary>
    public event EventHandler<(Guid TabId, string NewName)>? TabRenamed;

    /// <summary>Create a new terminal tab. <paramref name="beforeNotify"/>, if given, runs after the
    /// tab is registered but before <see cref="TabCreated"/> fires - it lets the caller register
    /// whatever state a <see cref="TabCreated"/> handler needs to look up (e.g. the session backing
    /// this tab's ID) before that handler can possibly run, since the tab's ID isn't known to the
    /// caller until this method returns.</summary>
    public TerminalTab? CreateTab(ShellDescriptor shell, string displayName, string workingDirectory = "", Action<Guid>? beforeNotify = null)
    {
        if (_disposed)
            return null;

        if (_tabs.Count >= MaxConcurrentSessions)
        {
            LogService.Warning($"Maximum concurrent sessions ({MaxConcurrentSessions}) reached");
            return null;
        }

        var tab = new TerminalTab(shell, displayName, workingDirectory)
        {
            IsActive = _tabs.Count == 0 // First tab is active by default
        };

        if (tab.IsActive)
            _activeTabId = tab.Id;

        _tabs.Add(tab);
        beforeNotify?.Invoke(tab.Id);
        TabCreated?.Invoke(this, tab.Id);
        LogService.Info($"Terminal tab created: {tab.Id} ({shell.Id})");
        return tab;
    }

    /// <summary>Close a terminal tab.</summary>
    public bool CloseTab(Guid tabId)
    {
        if (_disposed)
            return false;

        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null)
            return false;

        tab.IsDisposed = true;
        _tabs.Remove(tab);

        // If closed tab was active, activate another
        if (_activeTabId == tabId)
        {
            if (_tabs.Count > 0)
            {
                _activeTabId = _tabs[0].Id;
                _tabs[0].IsActive = true;
                TabActivated?.Invoke(this, _activeTabId.Value);
            }
            else
            {
                _activeTabId = null;
            }
        }

        TabClosed?.Invoke(this, tabId);
        LogService.Info($"Terminal tab closed: {tabId}");
        return true;
    }

    /// <summary>Switch to a different tab.</summary>
    public bool SwitchTab(Guid tabId)
    {
        if (_disposed)
            return false;

        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null || tab.IsDisposed)
            return false;

        // Deactivate current tab
        if (_activeTabId.HasValue)
        {
            var current = _tabs.FirstOrDefault(t => t.Id == _activeTabId);
            if (current != null)
                current.IsActive = false;
        }

        // Activate new tab
        tab.IsActive = true;
        _activeTabId = tabId;
        TabActivated?.Invoke(this, tabId);
        LogService.Info($"Switched to terminal tab: {tabId}");
        return true;
    }

    /// <summary>Rename a tab.</summary>
    public bool RenameTab(Guid tabId, string newName)
    {
        if (_disposed || string.IsNullOrWhiteSpace(newName))
            return false;

        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null)
            return false;

        tab.Name = newName.Trim();
        TabRenamed?.Invoke(this, (tabId, tab.Name));
        LogService.Info($"Terminal tab renamed: {tabId} -> {tab.Name}");
        return true;
    }

    /// <summary>Get tab by ID.</summary>
    public TerminalTab? GetTab(Guid tabId) =>
        _tabs.FirstOrDefault(t => t.Id == tabId);

    /// <summary>Get all tabs.</summary>
    public IEnumerable<TerminalTab> GetAllTabs() =>
        _tabs.Where(t => !t.IsDisposed);

    /// <summary>Get index of a tab.</summary>
    public int GetTabIndex(Guid tabId) =>
        _tabs.FindIndex(t => t.Id == tabId);

    /// <summary>Close all tabs and cleanup.</summary>
    public void CloseAllTabs()
    {
        var tabIds = _tabs.Select(t => t.Id).ToList();
        foreach (var id in tabIds)
            CloseTab(id);
    }

    /// <summary>Closes all tabs and releases resources.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CloseAllTabs();
        _tabs.Clear();
        LogService.Info("TerminalSessionManager disposed");
    }
}
