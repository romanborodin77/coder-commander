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

    /// <summary>Hit-test-only reach, deliberately NOT the same as the drawn handle square
    /// (<see cref="HandleSizePx96"/>): a symmetric hit box centered on the edge (half inside, half
    /// outside the control) meant a click meant as an ordinary body/move click anywhere within a few
    /// pixels of an edge - completely normal when grabbing a small control - would frequently land
    /// inside a handle's hit box and silently turn into a resize instead of a move. Biasing the hit
    /// box mostly OUTWARD (a small <see cref="HandleHitInwardPx96"/> reach into the body, a larger
    /// <see cref="HandleHitOutwardPx96"/> reach past the edge) keeps body clicks safely recognized as
    /// body clicks while still making the handle easy to grab deliberately from just outside the
    /// control - the same shape most design tools use for resize handles. Kept fairly modest (not
    /// larger still) specifically because this app packs controls with only a few px of margin
    /// between neighbors (e.g. DifferForm's two Browse buttons) - too generous an outward reach would
    /// start overlapping a NEIGHBORING control's own clickable area, misrouting a click meant to
    /// switch selection to that neighbor into a resize-drag on the control still selected instead.</summary>
    private const int HandleHitInwardPx96 = 2;
    private const int HandleHitOutwardPx96 = 5;
    private const int HandleHitAlongPx96 = 6;

    private static Rectangle? _current;
    private static Dictionary<HandleKind, Rectangle>? _handleRects;
    private static Dictionary<HandleKind, Rectangle>? _handleHitRects;

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

        _handleHitRects = BuildHandleHitRects(_current.Value, target.DeviceDpi);
    }

    /// <summary>Same as <see cref="Show(Control)"/>, for a <see cref="Services.LayoutEditModeService.SelectedItem"/>
    /// - a <see cref="ToolStripItem"/> has no window of its own, so its screen position is computed
    /// via its owning <see cref="ToolStrip"/>'s <see cref="Control.PointToScreen"/> instead of its
    /// own. Deliberately draws no resize handles: an AutoSize ToolStripItem (the overwhelming common
    /// case) has no independent Size to resize, so there is nothing for a handle to do - leaving
    /// <see cref="_handleRects"/> null/empty here also means <see cref="TryHitTestHandle"/> can never
    /// misfire against a stale handle rect from a previously-selected Control after switching to a
    /// ToolStripItem.</summary>
    public static void Show(ToolStripItem target)
    {
        Clear();
        if (target.Owner is not { } owner) return;
        _current = new Rectangle(owner.PointToScreen(target.Bounds.Location), target.Bounds.Size);
        ControlPaint.DrawReversibleFrame(_current.Value, Color.Lime, FrameStyle.Thick);
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
        _handleHitRects = null;

        if (_current is not { } rect) return;
        ControlPaint.DrawReversibleFrame(rect, Color.Lime, FrameStyle.Thick);
        _current = null;
    }

    /// <summary>Tests a screen point against the current handles' (larger, outward-biased) hit
    /// boxes - not the drawn squares themselves, and not the target control's real HWND (handles are
    /// XOR marks, not windows, so callers must hit-test this explicitly before falling back to
    /// normal click routing).</summary>
    public static bool TryHitTestHandle(Point screenPoint, out HandleKind kind)
    {
        if (_handleHitRects is { } handles)
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
        var size = Scale(HandleSizePx96, dpi);
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

    /// <summary>Builds the larger, outward-biased hit-test boxes <see cref="TryHitTestHandle"/> uses
    /// - see <see cref="HandleHitInwardPx96"/>'s doc comment for why this is deliberately NOT the
    /// same rects <see cref="BuildHandleRects"/> draws. Each edge handle is symmetric ALONG the edge
    /// (<see cref="HandleHitAlongPx96"/> half-width) but asymmetric ACROSS it (a small reach inward,
    /// a larger reach outward); a corner handle applies that same inward/outward split on both axes.</summary>
    private static Dictionary<HandleKind, Rectangle> BuildHandleHitRects(Rectangle frame, int dpi)
    {
        var inward = Scale(HandleHitInwardPx96, dpi);
        var outward = Scale(HandleHitOutwardPx96, dpi);
        var along = Scale(HandleHitAlongPx96, dpi);
        int left = frame.Left, right = frame.Right, top = frame.Top, bottom = frame.Bottom;
        int midX = frame.Left + frame.Width / 2, midY = frame.Top + frame.Height / 2;
        var depth = inward + outward;

        // "Edge" spans (across, along) - across is the inward/outward-biased axis, along is the
        // symmetric one running parallel to the edge.
        Rectangle North() => new(midX - along, top - outward, along * 2, depth);
        Rectangle South() => new(midX - along, bottom - inward, along * 2, depth);
        Rectangle East() => new(right - inward, midY - along, depth, along * 2);
        Rectangle West() => new(left - outward, midY - along, depth, along * 2);
        // Corners apply the inward/outward split on both axes at once.
        Rectangle NorthEast() => new(right - inward, top - outward, depth, depth);
        Rectangle NorthWest() => new(left - outward, top - outward, depth, depth);
        Rectangle SouthEast() => new(right - inward, bottom - inward, depth, depth);
        Rectangle SouthWest() => new(left - outward, bottom - inward, depth, depth);

        return new Dictionary<HandleKind, Rectangle>
        {
            [HandleKind.N] = North(),
            [HandleKind.S] = South(),
            [HandleKind.E] = East(),
            [HandleKind.W] = West(),
            [HandleKind.NE] = NorthEast(),
            [HandleKind.NW] = NorthWest(),
            [HandleKind.SE] = SouthEast(),
            [HandleKind.SW] = SouthWest(),
        };
    }

    private static int Scale(int px96, int dpi) => (int)Math.Round(px96 * dpi / 96.0);
}
