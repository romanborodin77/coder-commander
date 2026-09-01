using CoderCommander.Services;
using System.Runtime.InteropServices;

namespace CoderCommander.WinForms;

/// <summary>
/// Covers a ListView's native scrollbars with themed <see cref="ThemedScrollBar"/> sibling
/// controls, sized to match the native bars' exact non-client footprint. The native scrollbars
/// are left alone - they still reserve their own space in the ListView's client area (that
/// reservation is what makes their footprint measurable at all, and it's the same signal
/// <see cref="Views.FilePanelUserControl.FillLastColumnWidth"/> needs to fit the last column) and
/// comctl32 remains the single source of truth for scroll position/visibility. WS_CLIPSIBLINGS
/// (set on every WinForms control) means a same-sized sibling fully clips the native bar's own
/// non-client painting, so "exact size match" is suffient to make the native bar invisible
/// without disabling it.
///
/// An earlier version tried to hide the native bars via <c>ShowScrollBar(SB_BOTH, false)</c> and
/// draw a fixed-width (14px) overlay on top. Both parts were wrong: <c>ShowScrollBar</c> was only
/// ever called twice (construction + HandleCreated) and never re-applied, so the native bars kept
/// reappearing on their own; and the native vertical/horizontal scrollbar is 17px+ (grows with
/// DPI), so the fixed 14px overlay left a 3+px strip of unstyled native scrollbar visible right
/// next to it - the reported "dark strip to the left of the vertical bar, light strip above the
/// horizontal one". Un-reserving and re-reserving the native bar's space also made
/// <c>ClientSize.Width</c> oscillate, which was the root cause of a second bug: a spurious
/// horizontal scrollbar appearing in a panel whose columns exactly fit.
/// </summary>
internal sealed class ListViewScrollbarOverlay : IDisposable
{
    private readonly ListView _listView;
    private readonly ThemedScrollBar _scrollBar;
    private readonly ThemedScrollBar _hScrollBar;
    private readonly ScrollCorner _corner;
    private readonly System.Windows.Forms.Timer _syncTimer;
    private bool _suppressSync;
    private bool _suppressHSync;
    private (int V, int H) _lastMetrics = (-1, -1);

    /// <summary>
    /// Raised when the native scrollbars' measured footprint changes (a bar appeared/disappeared,
    /// or its thickness changed e.g. after a DPI change). <see cref="Views.FilePanelUserControl"/>
    /// listens to this to re-fit its last column - toggling a native scrollbar changes
    /// <c>ClientSize</c> via <c>SetWindowPos(SWP_FRAMECHANGED)</c>, which does not reliably raise
    /// <c>Resize</c>, so without this the last column could stay sized against a stale width.
    /// </summary>
    public event EventHandler? NativeMetricsChanged;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetScrollInfo(IntPtr hWnd, int nBar, ref SCROLLINFO lpsi);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [StructLayout(LayoutKind.Sequential)]
    private struct SCROLLINFO
    {
        public int cbSize;
        public int fMask;
        public int nMin;
        public int nMax;
        public uint nPage;
        public int nPos;
        public int nTrackPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    private const int LVM_FIRST = 0x1000;
    private const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
    private const int LVM_GETTOPINDEX = LVM_FIRST + 39;
    private const int LVM_GETCOUNTPERPAGE = LVM_FIRST + 40;
    private const int LVM_SCROLL = LVM_FIRST + 20;
    private const int SB_HORZ = 0;
    private const int SIF_ALL = 0x0017;
    private const int GWL_STYLE = -16;
    private const int WS_HSCROLL = 0x00100000;
    private const int WS_VSCROLL = 0x00200000;
    private const int SM_CXVSCROLL = 2;
    private const int SM_CYHSCROLL = 3;

    /// <summary>
    /// Attaches an overlay to any dialog <see cref="ListView"/>, regardless of its parent's
    /// layout kind. The constructor below adds its three sibling controls with absolute
    /// <see cref="Control.Bounds"/> in the ListView's parent coordinate space - safe for a plain
    /// <see cref="Panel"/> (as <see cref="Views.FilePanelUserControl"/> already uses), but it
    /// would corrupt a <see cref="TableLayoutPanel"/>/<see cref="FlowLayoutPanel"/> parent, which
    /// assigns cell/flow position to every child itself. For those, this first re-parents the
    /// ListView into a plain host <see cref="Panel"/> that takes over the ListView's original
    /// cell/flow slot, then constructs the overlay against that host - keeping the
    /// already-proven parent-coordinate positioning path untouched.
    /// </summary>
    public static ListViewScrollbarOverlay Attach(ListView lv)
    {
        if (lv.Parent is TableLayoutPanel or FlowLayoutPanel)
            WrapInHostPanel(lv);
        return new ListViewScrollbarOverlay(lv);
    }

    private static void WrapInHostPanel(ListView lv)
    {
        var layoutParent = lv.Parent!;
        var originalDock = lv.Dock;

        // Added to tlp.Controls or flp.Controls a few lines below (one of the two branches always
        // runs - see the guard in Attach() above) - disposed recursively with its new parent, the
        // same as any other Controls-collection child.
#pragma warning disable CA2000
        var host = new Panel
        {
            BackColor = Color.Transparent, // ControlThemer's Panel/FlowLayoutPanel case skips
                                            // transparent containers, so this never gets painted
                                            // over with the form's background.
            Margin = lv.Margin,
            Dock = originalDock
        };
#pragma warning restore CA2000

        if (layoutParent is TableLayoutPanel tlp)
        {
            var pos = tlp.GetPositionFromControl(lv);
            var colSpan = tlp.GetColumnSpan(lv);
            var rowSpan = tlp.GetRowSpan(lv);

            tlp.Controls.Remove(lv);
            lv.Dock = DockStyle.Fill;
            lv.Margin = Padding.Empty;
            host.Controls.Add(lv);

            tlp.Controls.Add(host, pos.Column, pos.Row);
            if (colSpan > 1) tlp.SetColumnSpan(host, colSpan);
            if (rowSpan > 1) tlp.SetRowSpan(host, rowSpan);
        }
        else if (layoutParent is FlowLayoutPanel flp)
        {
            var childIndex = flp.Controls.GetChildIndex(lv);
            host.Size = lv.Size;

            flp.Controls.Remove(lv);
            lv.Dock = DockStyle.Fill;
            lv.Margin = Padding.Empty;
            host.Controls.Add(lv);

            flp.Controls.Add(host);
            flp.Controls.SetChildIndex(host, childIndex);
        }
    }

    /// <summary>
    /// Initializes the overlay by creating themed vertical and horizontal scrollbars, a corner
    /// fill control, and starting the sync timer to track the native scroll position.
    /// </summary>
    public ListViewScrollbarOverlay(ListView listView)
    {
        _listView = listView;

        _scrollBar = new ThemedScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 1,
            LargeChange = 1,
            SmallChange = 1,
            Visible = false
        };
        _scrollBar.ValueChanged += OnScrollBarValueChanged;

        _hScrollBar = new ThemedScrollBar
        {
            Orientation = Orientation.Horizontal,
            Minimum = 0,
            Maximum = 1,
            LargeChange = 1,
            SmallChange = 1,
            Visible = false
        };
        _hScrollBar.ValueChanged += OnHScrollBarValueChanged;

        _corner = new ScrollCorner { Visible = false };

        // Add scrollbars as siblings of ListView (same parent)
        if (_listView.Parent != null)
        {
            _listView.Parent.Controls.Add(_scrollBar);
            _listView.Parent.Controls.Add(_hScrollBar);
            _listView.Parent.Controls.Add(_corner);
            _scrollBar.BringToFront();
            _hScrollBar.BringToFront();
            _corner.BringToFront();
        }

        PositionOverlay();

        // Sync timer — polls scroll position and re-measures the native footprint.
        _syncTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _syncTimer.Tick += SyncFromListView;
        if (_listView.Visible) _syncTimer.Start();

        _listView.Resize += OnListViewResize;
        _listView.HandleCreated += OnListViewHandleCreated;
        _listView.VisibleChanged += OnListViewVisibleChanged;
    }

    /// <summary>
    /// Forces a reposition from the ListView's current bounds - the entry point
    /// <see cref="Views.MainForm.OnDpiChanged"/> calls after a DPI-monitor change. The
    /// measurement itself never caches DPI (<see cref="MeasureNative"/> reads
    /// <c>_listView.DeviceDpi</c> fresh every call), so no DPI-specific logic is needed here
    /// beyond restoring z-order in case it was disturbed.
    /// </summary>
    public void Reposition()
    {
        PositionOverlay();
        _scrollBar.BringToFront();
        _hScrollBar.BringToFront();
        _corner.BringToFront();
    }

    /// <summary>
    /// Disposes all overlay controls, stops the sync timer, and unsubscribes from
    /// ListView events.
    /// </summary>
    public void Dispose()
    {
        _syncTimer.Stop();
        _syncTimer.Dispose();
        _listView.Resize -= OnListViewResize;
        _listView.HandleCreated -= OnListViewHandleCreated;
        _listView.VisibleChanged -= OnListViewVisibleChanged;

        _scrollBar.ValueChanged -= OnScrollBarValueChanged;
        _scrollBar.Parent?.Controls.Remove(_scrollBar);
        _scrollBar.Dispose();
        _hScrollBar.ValueChanged -= OnHScrollBarValueChanged;
        _hScrollBar.Parent?.Controls.Remove(_hScrollBar);
        _hScrollBar.Dispose();
        _corner.Parent?.Controls.Remove(_corner);
        _corner.Dispose();
    }

    /// <summary>Repositions the overlay when the ListView is resized.</summary>
    private void OnListViewResize(object? sender, EventArgs e) => PositionOverlay();

    /// <summary>Repositions the overlay when the ListView handle is created.</summary>
    private void OnListViewHandleCreated(object? sender, EventArgs e) => PositionOverlay();

    /// <summary>Starts or stops the sync timer based on ListView visibility.</summary>
    private void OnListViewVisibleChanged(object? sender, EventArgs e)
    {
        if (_listView.Visible) _syncTimer.Start();
        else _syncTimer.Stop();
    }

    /// <summary>
    /// Measures the native scrollbars' current non-client footprint: the style bit says whether
    /// a bar is present at all (and rules out a false positive from e.g. a border), the delta
    /// between the window's outer size and its live client rect gives the exact thickness, and
    /// <c>GetSystemMetricsForDpi</c> is only a fallback for the (normally unreachable) case where
    /// that delta isn't positive. Nothing here is cached - DeviceDpi and the client rect are read
    /// fresh on every call, which is what lets <see cref="Reposition"/> stay DPI-agnostic.
    /// </summary>
    private (int V, int H) MeasureNative()
    {
        if (!_listView.IsHandleCreated) return (0, 0);

        var handle = _listView.Handle;
        var style = GetWindowLong(handle, GWL_STYLE);
        if (!GetClientRect(handle, out var client)) return (0, 0);

        var v = 0;
        if ((style & WS_VSCROLL) != 0)
        {
            var delta = _listView.Width - (client.right - client.left);
            v = delta > 0 ? delta : GetSystemMetricsForDpi(SM_CXVSCROLL, (uint)_listView.DeviceDpi);
        }

        var h = 0;
        if ((style & WS_HSCROLL) != 0)
        {
            var delta = _listView.Height - (client.bottom - client.top);
            h = delta > 0 ? delta : GetSystemMetricsForDpi(SM_CYHSCROLL, (uint)_listView.DeviceDpi);
        }

        return (v, h);
    }

    /// <summary>Positions the overlay controls to match the native scrollbar footprint.</summary>
    private void PositionOverlay()
    {
        if (_listView.Parent == null) return;

        var (vw, hh) = MeasureNative();
        int left = _listView.Left, top = _listView.Top, width = _listView.Width, height = _listView.Height;

        ApplyBounds(_scrollBar, vw > 0,
            new Rectangle(left + width - vw, top, vw, Math.Max(0, height - hh)));
        ApplyBounds(_hScrollBar, hh > 0,
            new Rectangle(left, top + height - hh, Math.Max(0, width - vw), hh));
        ApplyBounds(_corner, vw > 0 && hh > 0,
            new Rectangle(left + width - vw, top + height - hh, vw, hh));

        if (_lastMetrics != (vw, hh))
        {
            _lastMetrics = (vw, hh);
            NativeMetricsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Sets bounds and visibility on a control, avoiding unnecessary updates.
    ///
    /// <para>Bounds are applied whether or not the control is visible. Skipping them while hidden
    /// leaves stale geometry behind, and the next time the control is shown it appears at the size
    /// it had when it was last visible - which is how the vertical scrollbar came to extend under
    /// the scroll corner: it was last positioned while the horizontal bar was absent, so it kept
    /// the full height it had then. Setting bounds on a hidden control costs nothing.</para>
    /// </summary>
    private static void ApplyBounds(Control control, bool visible, Rectangle bounds)
    {
        if (visible && (bounds.Width <= 0 || bounds.Height <= 0)) visible = false;
        if (control.Bounds != bounds) control.Bounds = bounds;
        if (control.Visible != visible) control.Visible = visible;
    }

    /// <summary>Syncs the themed scrollbar position and range from the ListView's native scroll state.</summary>
    private void SyncFromListView(object? sender, EventArgs e)
    {
        if (!_listView.IsHandleCreated || !_listView.Visible) return;

        // Cheap and self-diffing (see ApplyBounds/PositionOverlay) - runs every tick instead of
        // only "when the vertical bar's visibility flipped", which used to leave the horizontal
        // bar's geometry stale whenever only ITS visibility changed.
        PositionOverlay();

        if (_scrollBar.Visible && !_suppressSync)
        {
            int itemCount = (int)SendMessage(_listView.Handle, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            int topIndex = (int)SendMessage(_listView.Handle, LVM_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero);
            int visibleCount = (int)SendMessage(_listView.Handle, LVM_GETCOUNTPERPAGE, IntPtr.Zero, IntPtr.Zero);

            if (itemCount > 0 && visibleCount > 0)
            {
                _scrollBar.Maximum = itemCount;
                _scrollBar.LargeChange = visibleCount;
                _scrollBar.SmallChange = 1;

                _suppressSync = true;
                _scrollBar.Value = Math.Min(topIndex, Math.Max(0, itemCount - visibleCount));
                _suppressSync = false;
            }
        }

        if (_hScrollBar.Visible && !_suppressHSync)
        {
            var si = new SCROLLINFO { cbSize = Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
            if (GetScrollInfo(_listView.Handle, SB_HORZ, ref si) && si.nPage > 0 &&
                (si.nMax - si.nMin + 1) > si.nPage)
            {
                _hScrollBar.Minimum = si.nMin;
                _hScrollBar.Maximum = si.nMax + 1;
                _hScrollBar.LargeChange = (int)si.nPage;
                // A pixel-sized step, not "1" — the horizontal range is in pixels (LVM_SCROLL),
                // so SmallChange (used by ThemedScrollBar's wheel handling) must be too.
                _hScrollBar.SmallChange = Math.Max(1, (int)Math.Round(16 * _listView.DeviceDpi / 96.0));

                _suppressHSync = true;
                _hScrollBar.Value = Math.Clamp(si.nPos, si.nMin,
                    Math.Max(si.nMin, _hScrollBar.Maximum - _hScrollBar.LargeChange));
                _suppressHSync = false;
            }
        }
    }

    /// <summary>Scrolls the ListView vertically when the vertical scrollbar value changes.</summary>
    private void OnScrollBarValueChanged(object? sender, EventArgs e)
    {
        if (_suppressSync || !_listView.IsHandleCreated) return;
        if (_scrollBar.Value < 0 || _scrollBar.Value >= _listView.Items.Count) return;

        // TopItem scrolls by item, not pixels — avoids the unit mismatch LVM_SCROLL had
        // (that message takes a pixel delta; this code was feeding it an item-index delta,
        // so a drag barely moved the list, and the poll timer kept snapping the thumb back
        // toward the near-stationary real position — visible as flicker/jitter while dragging).
        _suppressSync = true;
        _listView.TopItem = _listView.Items[_scrollBar.Value];
        _suppressSync = false;
    }

    /// <summary>Scrolls the ListView horizontally using LVM_SCROLL when the horizontal scrollbar value changes.</summary>
    private void OnHScrollBarValueChanged(object? sender, EventArgs e)
    {
        if (_suppressHSync || !_listView.IsHandleCreated) return;

        var si = new SCROLLINFO { cbSize = Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
        if (!GetScrollInfo(_listView.Handle, SB_HORZ, ref si)) return;

        // LVM_SCROLL takes a relative pixel delta, not an absolute position - unlike the vertical
        // path there's no item-based equivalent for horizontal, so this has to go through the
        // real native scroll position rather than around it.
        int dx = _hScrollBar.Value - si.nPos;
        if (dx == 0) return;

        _suppressHSync = true;
        SendMessage(_listView.Handle, LVM_SCROLL, (IntPtr)dx, IntPtr.Zero);
        _suppressHSync = false;
    }

    /// <summary>
    /// Fills the bottom-right corner square that appears when both scrollbars are visible -
    /// outside both bars' rects, so nothing else paints over the native corner there.
    /// </summary>
    private sealed class ScrollCorner : Control
    {
        public ScrollCorner()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            ThemeService.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();

        protected override void OnPaint(PaintEventArgs e)
        {
            using var brush = new SolidBrush(DesignerSafeThemeService.Current.ScrollbarTrack);
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ThemeService.ThemeChanged -= OnThemeChanged;
            base.Dispose(disposing);
        }
    }
}
