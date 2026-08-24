namespace CoderCommander.WinForms;

/// <summary>
/// Draws (and erases) a screen-space selection frame around a control for
/// <see cref="Services.LayoutEditModeService"/>, via <see cref="ControlPaint.DrawReversibleFrame"/> -
/// the classic Win32 rubber-band technique (XOR directly on the screen DC, draw the same rect twice
/// to draw-then-erase). Deliberately not a sibling overlay control (contrast
/// <see cref="ListViewScrollbarOverlay"/>, which lives for as long as its target does and has to
/// handle re-parenting out of a TableLayoutPanel/FlowLayoutPanel parent): this frame is drawn and
/// erased on demand, has no lifecycle of its own, and never touches the target's control tree.
///
/// XOR painting is fragile against any repaint of the same screen region it didn't cause itself
/// (the target dialog moving, another window overlapping it) - erasing then XORs against
/// already-changed pixels and leaves visible garbage. <see cref="Services.LayoutEditModeService"/>
/// is responsible for calling <see cref="Clear"/> defensively on the target's Move/Resize/
/// Deactivate/FormClosing, not just on an explicit deselect - this class only tracks the one
/// currently-drawn rectangle so an erase always targets the exact rect it drew.
/// </summary>
internal static class LayoutEditHighlight
{
    private static Rectangle? _current;

    /// <summary>Erases any existing frame, then draws a fresh one around <paramref name="target"/>'s
    /// current screen bounds.</summary>
    public static void Show(Control target)
    {
        Clear();
        _current = new Rectangle(target.PointToScreen(Point.Empty), target.Size);
        ControlPaint.DrawReversibleFrame(_current.Value, Color.Lime, FrameStyle.Thick);
    }

    /// <summary>Erases the currently-drawn frame, if any. Safe to call when nothing is shown.</summary>
    public static void Clear()
    {
        if (_current is not { } r) return;
        ControlPaint.DrawReversibleFrame(r, Color.Lime, FrameStyle.Thick);
        _current = null;
    }
}
