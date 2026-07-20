using CoderCommander.Models;
using CoderCommander.Services;

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

    public int MaxConcurrentSessions { get; set; } = 10;
    public IReadOnlyList<TerminalTab> Tabs => _tabs.AsReadOnly();
    public TerminalTab? ActiveTab => _activeTabId.HasValue
        ? _tabs.FirstOrDefault(t => t.Id == _activeTabId)
        : null;

    public event EventHandler<Guid>? TabCreated;
    public event EventHandler<Guid>? TabClosed;
    public event EventHandler<Guid>? TabActivated;
    public event EventHandler<(Guid TabId, string NewName)>? TabRenamed;

    /// <summary>Create a new terminal tab.</summary>
    public TerminalTab? CreateTab(ShellType shellType, string workingDirectory = "")
    {
        if (_disposed)
            return null;

        if (_tabs.Count >= MaxConcurrentSessions)
        {
            LogService.Warning($"Maximum concurrent sessions ({MaxConcurrentSessions}) reached");
            return null;
        }

        var tab = new TerminalTab(shellType, workingDirectory)
        {
            IsActive = _tabs.Count == 0 // First tab is active by default
        };

        if (tab.IsActive)
            _activeTabId = tab.Id;

        _tabs.Add(tab);
        TabCreated?.Invoke(this, tab.Id);
        LogService.Info($"Terminal tab created: {tab.Id} ({shellType})");
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
