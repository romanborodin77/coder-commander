namespace CoderCommander.WinForms;

/// <summary>Which edge/corner of the selection rectangle a resize handle sits at - also doubles as
/// the "no handle, this is a body click" sentinel (<see cref="None"/>) for
/// <see cref="LayoutEditHighlight.TryHitTestHandle"/>'s callers.</summary>
public enum HandleKind { None, N, S, E, W, NE, NW, SE, SW }

/// <summary>
/// Draws (and erases) a screen-space selection frame plus 8 resize handles around a control for
/// <see cref="Services.LayoutEditModeService"/>, via <see cref="ControlPaint.DrawReversibleFrame"/>
/// and <see cref="ControlPaint.FillReversibleRectangle"/> - the classic Win32 rubber-band technique
/// (XOR directly on the screen DC, draw the same shape twice to draw-then-erase). Deliberately not
/// sibling overlay controls (contrast <see cref="ListViewScrollbarOverlay"/>, which lives for as
/// long as its target does and has to handle re-parenting out of a TableLayoutPanel/FlowLayoutPanel
/// parent): these shapes are drawn and erased on demand, have no lifecycle of their own, and never
/// touch the target's control tree.
///
/// XOR painting is fragile against any repaint of the same screen region it didn't cause itself
/// (the target dialog moving, another window overlapping it) - erasing then XORs against
/// already-changed pixels and leaves visible garbage. <see cref="Services.LayoutEditModeService"/>
/// is responsible for calling <see cref="Clear"/> defensively on the target's Move/Resize/
/// FormClosing - this class only tracks the currently-drawn shapes so an erase always targets the
/// exact rects it drew.
/// </summary>
internal static class LayoutEditHighlight
{
    private const int HandleSizePx96 = 6;

    private static Rectangle? _current;
    private static Dictionary<HandleKind, Rectangle>? _handleRects;

    /// <summary>Erases any existing frame/handles, then draws fresh ones around <paramref name="target"/>'s
    /// current screen bounds.</summary>
    public static void Show(Control target)
    {
        Clear();
        _current = new Rectangle(target.PointToScreen(Point.Empty), target.Size);
        ControlPaint.DrawReversibleFrame(_current.Value, Color.Lime, FrameStyle.Thick);

        _handleRects = BuildHandleRects(_current.Value, target.DeviceDpi);
        foreach (var r in _handleRects.Values)
            ControlPaint.FillReversibleRectangle(r, Color.Lime);
    }

    /// <summary>Erases the currently-drawn frame and handles, if any. Safe to call when nothing is shown.</summary>
    public static void Clear()
    {
        if (_handleRects is { } handles)
        {
            foreach (var r in handles.Values)
                ControlPaint.FillReversibleRectangle(r, Color.Lime);
            _handleRects = null;
        }

        if (_current is not { } rect) return;
        ControlPaint.DrawReversibleFrame(rect, Color.Lime, FrameStyle.Thick);
        _current = null;
    }

    /// <summary>Tests a screen point against the currently-drawn handle rects (not the target
    /// control's real HWND - handles are XOR marks, not windows, so callers must hit-test this
    /// explicitly before falling back to normal click routing).</summary>
    public static bool TryHitTestHandle(Point screenPoint, out HandleKind kind)
    {
        if (_handleRects is { } handles)
        {
            foreach (var (k, r) in handles)
            {
                if (!r.Contains(screenPoint)) continue;
                kind = k;
                return true;
            }
        }
        kind = HandleKind.None;
        return false;
    }

    private static Dictionary<HandleKind, Rectangle> BuildHandleRects(Rectangle frame, int dpi)
    {
        var size = (int)Math.Round(HandleSizePx96 * dpi / 96.0);
        var half = size / 2;
        int left = frame.Left, right = frame.Right, top = frame.Top, bottom = frame.Bottom;
        int midX = frame.Left + frame.Width / 2, midY = frame.Top + frame.Height / 2;

        Rectangle At(int cx, int cy) => new(cx - half, cy - half, size, size);

        return new Dictionary<HandleKind, Rectangle>
        {
            [HandleKind.N] = At(midX, top),
            [HandleKind.S] = At(midX, bottom),
            [HandleKind.E] = At(right, midY),
            [HandleKind.W] = At(left, midY),
            [HandleKind.NE] = At(right, top),
            [HandleKind.NW] = At(left, top),
            [HandleKind.SE] = At(right, bottom),
            [HandleKind.SW] = At(left, bottom),
        };
    }
}
