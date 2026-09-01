using CoderCommander.Services;
using System.Runtime.InteropServices;

namespace CoderCommander.WinForms;

/// <summary>
/// Covers a <see cref="RichTextBox"/>'s native scrollbars with themed <see cref="ThemedScrollBar"/>
/// sibling controls, sized to match the native bars' exact non-client footprint - same technique as
/// <see cref="ListViewScrollbarOverlay"/> (WS_CLIPSIBLINGS makes an exact-size sibling fully clip the
/// native bar's own non-client painting, so covering it is enough without disabling it), needed here
/// because <c>SetWindowTheme</c> alone does not darken a RichEdit control's scrollbar the way it does
/// for a plain Edit/ListView/TreeView - confirmed live: the native bar stayed light-themed regardless
/// of when <c>NativeControlThemer.ApplyDarkScrollbars</c> was called (construction, after content
/// load, after a WM_THEMECHANGED nudge) or in what order.
///
/// Simpler than the ListView version in one respect: RichEdit's vertical scroll position is a plain
/// pixel/line range via <c>GetScrollInfo(SB_VERT, ...)</c>, the same call already used for the
/// horizontal bar - no item-count/top-index translation layer is needed for either axis, and both
/// scroll by sending <c>WM_VSCROLL</c>/<c>WM_HSCROLL</c> with <c>SB_THUMBPOSITION</c>, the standard
/// way to reposition a native scrollbar-owning control from outside.
/// </summary>
internal sealed class RichTextBoxScrollbarOverlay : IDisposable
{
    private readonly RichTextBox _box;
    private readonly ThemedScrollBar _vScrollBar;
    private readonly ThemedScrollBar _hScrollBar;
    private readonly ScrollCorner _corner;
    private readonly System.Windows.Forms.Timer _syncTimer;
    private bool _suppressVSync;
    private bool _suppressHSync;

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

    private const int WM_VSCROLL = 0x0115;
    private const int WM_HSCROLL = 0x0114;
    private const int SB_THUMBPOSITION = 4;
    private const int SB_HORZ = 0;
    private const int SB_VERT = 1;
    private const int SIF_ALL = 0x0017;
    private const int GWL_STYLE = -16;
    private const int WS_HSCROLL = 0x00100000;
    private const int WS_VSCROLL = 0x00200000;
    private const int SM_CXVSCROLL = 2;
    private const int SM_CYHSCROLL = 3;

    /// <summary>
    /// Attaches an overlay to a <see cref="RichTextBox"/> whose parent is a plain
    /// <see cref="Panel"/> (as <see cref="Viewers.ViewerHostControl"/>'s content host is) - the
    /// overlay's sibling controls use absolute <see cref="Control.Bounds"/> in that parent's
    /// coordinate space, which a <see cref="TableLayoutPanel"/>/<see cref="FlowLayoutPanel"/> parent
    /// would fight over on its own layout pass. No re-parenting fallback like
    /// <see cref="ListViewScrollbarOverlay.Attach"/>'s: every current caller already parents into a
    /// plain <see cref="Panel"/>, so that complexity isn't pulled in until a caller actually needs it.
    /// </summary>
    public RichTextBoxScrollbarOverlay(RichTextBox box)
    {
        _box = box;

        _vScrollBar = new ThemedScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 1,
            LargeChange = 1,
            SmallChange = 1,
            Visible = false
        };
        _vScrollBar.ValueChanged += OnVScrollBarValueChanged;

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

        if (_box.Parent != null)
        {
            _box.Parent.Controls.Add(_vScrollBar);
            _box.Parent.Controls.Add(_hScrollBar);
            _box.Parent.Controls.Add(_corner);
            _vScrollBar.BringToFront();
            _hScrollBar.BringToFront();
            _corner.BringToFront();
        }

        PositionOverlay();

        _syncTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _syncTimer.Tick += SyncFromBox;
        if (_box.Visible) _syncTimer.Start();

        _box.Resize += OnBoxResize;
        _box.HandleCreated += OnBoxHandleCreated;
        _box.VisibleChanged += OnBoxVisibleChanged;
        _box.TextChanged += OnBoxResize;
    }

    public void Dispose()
    {
        _syncTimer.Stop();
        _syncTimer.Dispose();
        _box.Resize -= OnBoxResize;
        _box.HandleCreated -= OnBoxHandleCreated;
        _box.VisibleChanged -= OnBoxVisibleChanged;
        _box.TextChanged -= OnBoxResize;

        _vScrollBar.ValueChanged -= OnVScrollBarValueChanged;
        _vScrollBar.Parent?.Controls.Remove(_vScrollBar);
        _vScrollBar.Dispose();
        _hScrollBar.ValueChanged -= OnHScrollBarValueChanged;
        _hScrollBar.Parent?.Controls.Remove(_hScrollBar);
        _hScrollBar.Dispose();
        _corner.Parent?.Controls.Remove(_corner);
        _corner.Dispose();
    }

    private void OnBoxResize(object? sender, EventArgs e) => PositionOverlay();
    private void OnBoxHandleCreated(object? sender, EventArgs e) => PositionOverlay();

    private void OnBoxVisibleChanged(object? sender, EventArgs e)
    {
        if (_box.Visible) _syncTimer.Start();
        else _syncTimer.Stop();
    }

    /// <summary>Same delta-based, nothing-cached measurement as <see cref="ListViewScrollbarOverlay.MeasureNative"/>.</summary>
    private (int V, int H) MeasureNative()
    {
        if (!_box.IsHandleCreated) return (0, 0);

        var handle = _box.Handle;
        var style = GetWindowLong(handle, GWL_STYLE);
        if (!GetClientRect(handle, out var client)) return (0, 0);

        var v = 0;
        if ((style & WS_VSCROLL) != 0)
        {
            var delta = _box.Width - (client.right - client.left);
            v = delta > 0 ? delta : GetSystemMetricsForDpi(SM_CXVSCROLL, (uint)_box.DeviceDpi);
        }

        var h = 0;
        if ((style & WS_HSCROLL) != 0)
        {
            var delta = _box.Height - (client.bottom - client.top);
            h = delta > 0 ? delta : GetSystemMetricsForDpi(SM_CYHSCROLL, (uint)_box.DeviceDpi);
        }

        return (v, h);
    }

    private void PositionOverlay()
    {
        if (_box.Parent == null) return;

        var (vw, hh) = MeasureNative();
        int left = _box.Left, top = _box.Top, width = _box.Width, height = _box.Height;

        ApplyBounds(_vScrollBar, vw > 0,
            new Rectangle(left + width - vw, top, vw, Math.Max(0, height - hh)));
        ApplyBounds(_hScrollBar, hh > 0,
            new Rectangle(left, top + height - hh, Math.Max(0, width - vw), hh));
        ApplyBounds(_corner, vw > 0 && hh > 0,
            new Rectangle(left + width - vw, top + height - hh, vw, hh));
    }

    private static void ApplyBounds(Control control, bool visible, Rectangle bounds)
    {
        if (visible && (bounds.Width <= 0 || bounds.Height <= 0)) visible = false;
        if (control.Bounds != bounds) control.Bounds = bounds;
        if (control.Visible != visible) control.Visible = visible;
    }

    /// <summary>Syncs both themed scrollbars from the RichTextBox's native <c>GetScrollInfo</c> state.</summary>
    private void SyncFromBox(object? sender, EventArgs e)
    {
        if (!_box.IsHandleCreated || !_box.Visible) return;

        PositionOverlay();

        if (_vScrollBar.Visible && !_suppressVSync)
        {
            var si = new SCROLLINFO { cbSize = Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
            if (GetScrollInfo(_box.Handle, SB_VERT, ref si) && si.nPage > 0 &&
                (si.nMax - si.nMin + 1) > si.nPage)
            {
                _vScrollBar.Minimum = si.nMin;
                _vScrollBar.Maximum = si.nMax + 1;
                _vScrollBar.LargeChange = (int)si.nPage;
                _vScrollBar.SmallChange = Math.Max(1, (int)Math.Round(16 * _box.DeviceDpi / 96.0));

                _suppressVSync = true;
                _vScrollBar.Value = Math.Clamp(si.nPos, si.nMin,
                    Math.Max(si.nMin, _vScrollBar.Maximum - _vScrollBar.LargeChange));
                _suppressVSync = false;
            }
        }

        if (_hScrollBar.Visible && !_suppressHSync)
        {
            var si = new SCROLLINFO { cbSize = Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
            if (GetScrollInfo(_box.Handle, SB_HORZ, ref si) && si.nPage > 0 &&
                (si.nMax - si.nMin + 1) > si.nPage)
            {
                _hScrollBar.Minimum = si.nMin;
                _hScrollBar.Maximum = si.nMax + 1;
                _hScrollBar.LargeChange = (int)si.nPage;
                _hScrollBar.SmallChange = Math.Max(1, (int)Math.Round(16 * _box.DeviceDpi / 96.0));

                _suppressHSync = true;
                _hScrollBar.Value = Math.Clamp(si.nPos, si.nMin,
                    Math.Max(si.nMin, _hScrollBar.Maximum - _hScrollBar.LargeChange));
                _suppressHSync = false;
            }
        }
    }

    private void OnVScrollBarValueChanged(object? sender, EventArgs e)
    {
        if (_suppressVSync || !_box.IsHandleCreated) return;
        var wParam = (IntPtr)((SB_THUMBPOSITION & 0xFFFF) | (_vScrollBar.Value << 16));
        SendMessage(_box.Handle, WM_VSCROLL, wParam, IntPtr.Zero);
    }

    private void OnHScrollBarValueChanged(object? sender, EventArgs e)
    {
        if (_suppressHSync || !_box.IsHandleCreated) return;
        var wParam = (IntPtr)((SB_THUMBPOSITION & 0xFFFF) | (_hScrollBar.Value << 16));
        SendMessage(_box.Handle, WM_HSCROLL, wParam, IntPtr.Zero);
    }

    /// <summary>Fills the bottom-right corner square when both scrollbars are visible - same as
    /// <see cref="ListViewScrollbarOverlay"/>'s own nested <c>ScrollCorner</c>.</summary>
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
