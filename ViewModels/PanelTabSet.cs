namespace CoderCommander.ViewModels;

/// <summary>
/// Owns every tab on one side (left or right) of the main window - the ordered tab list and which
/// one is active. <see cref="MainViewModel.LeftPanel"/>/<see cref="MainViewModel.RightPanel"/>
/// read <see cref="Active"/> from one of these instead of holding a single <see cref="PanelViewModel"/>
/// field each, so the rest of the app - which already treats "the left panel" as one stable
/// identity read many times per action - keeps working unmodified while multiple tabs exist
/// underneath.
///
/// <para><b>Event subscriptions are per-tab, not per-active-tab.</b> A tab is subscribed to
/// (<see cref="TabAdded"/>) the moment it is added and stays subscribed for its whole lifetime,
/// never re-wired on an activation switch - <see cref="MainViewModel"/>'s own panel-event handlers
/// (git status, status-bar refresh) already tolerate firing for a background tab: git status is
/// tracked per <see cref="PanelViewModel"/> instance regardless of which one is active, and a
/// redundant status-bar rebuild triggered by a background tab is harmless, just slightly wasted
/// work. Re-wiring on every switch would be strictly more moving parts for a case none of today's
/// handlers actually need.</para>
/// </summary>
public sealed class PanelTabSet : IDisposable
{
    private readonly List<PanelViewModel> _tabs = new();
    private int _activeIndex = -1;
    private bool _disposed;

    /// <summary>Every tab on this side, in display order.</summary>
    public IReadOnlyList<PanelViewModel> Tabs => _tabs;

    /// <summary>Index of <see cref="Active"/> within <see cref="Tabs"/>.</summary>
    public int ActiveIndex => _activeIndex;

    /// <summary>The tab currently shown - what <c>MainViewModel.LeftPanel</c>/<c>RightPanel</c>
    /// resolve to. Throws if this set has no tabs yet (before the first <see cref="AddTab"/> call) -
    /// callers are expected to add at least one tab immediately after construction, the same way
    /// the two-field version always started with a non-null panel.</summary>
    public PanelViewModel Active => _tabs[_activeIndex];

    /// <summary>Raised right after a new tab is added and appended to <see cref="Tabs"/>, so a
    /// caller can wire its own per-panel event subscriptions before the tab does anything (e.g. its
    /// constructor's initial navigation).</summary>
    public event EventHandler<PanelViewModel>? TabAdded;

    /// <summary>Raised right before a tab is removed and disposed, so a caller can unsubscribe its
    /// own event handlers first - the panel is still fully valid at this point.</summary>
    public event EventHandler<PanelViewModel>? TabRemoving;

    /// <summary>Raised after <see cref="ActiveIndex"/>/<see cref="Active"/> changes for any reason
    /// (a new tab activating itself on add, <see cref="SetActive"/>, or a close that reassigns
    /// activation) - a UI layer redraws from <see cref="Active"/> in response.</summary>
    public event EventHandler? ActiveChanged;

    /// <summary>Appends a new tab, making it active - suspending whichever tab was active before
    /// (a no-op the first time, when there is no previous tab to suspend). <paramref name="panel"/>
    /// is owned by this set from this point on - <see cref="CloseTab"/>/<see cref="Dispose"/>
    /// dispose it.</summary>
    public void AddTab(PanelViewModel panel)
    {
        if (_activeIndex >= 0)
            _tabs[_activeIndex].Suspend();
        _tabs.Add(panel);
        _activeIndex = _tabs.Count - 1;
        TabAdded?.Invoke(this, panel);
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Switches which tab is active - a background tab is suspended (its
    /// <see cref="FileSystemWatcher"/>/debounce timers stop) and the newly active one is resumed
    /// (watcher restarted, one refresh to pick up anything that changed while backgrounded). A
    /// no-op if <paramref name="index"/> is already active or out of range.</summary>
    public void SetActive(int index)
    {
        if (index < 0 || index >= _tabs.Count || index == _activeIndex) return;
        _tabs[_activeIndex].Suspend();
        _activeIndex = index;
        _tabs[_activeIndex].Resume();
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes and disposes the tab at <paramref name="index"/>. Reassigns
    /// <see cref="ActiveIndex"/> (preferring the same position, clamped, so closing a tab in the
    /// middle activates whichever tab slid into its spot) and fires <see cref="ActiveChanged"/>
    /// only when the active tab actually changed as a result. A no-op if this is the last
    /// remaining tab - callers (the UI layer) are expected to gate the close command on
    /// <c>Tabs.Count &gt; 1</c> before ever calling this, the same way every other "last one can't
    /// close" affordance in this app is gated at the point of action rather than inside the method
    /// that would otherwise leave a side with zero tabs.</summary>
    public void CloseTab(int index)
    {
        if (index < 0 || index >= _tabs.Count || _tabs.Count <= 1) return;

        var panel = _tabs[index];
        TabRemoving?.Invoke(this, panel);
        _tabs.RemoveAt(index);
        panel.Dispose();

        var wasActive = index == _activeIndex;
        if (index < _activeIndex)
            _activeIndex--;
        else if (_activeIndex >= _tabs.Count)
            _activeIndex = _tabs.Count - 1;

        if (wasActive)
            _tabs[_activeIndex].Resume(); // the tab that just became active may have been suspended
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var panel in _tabs)
        {
            TabRemoving?.Invoke(this, panel);
            panel.Dispose();
        }
        _tabs.Clear();
    }
}
