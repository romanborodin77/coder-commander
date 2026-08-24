using CoderCommander.WinForms;

namespace CoderCommander.Services;

/// <summary>
/// Developer-only "live layout tuning" mode - gated behind the same
/// <see cref="DiagnosticCommandChannel.EnvironmentVariable"/> (CODERCOMMANDER_UI_DEBUG=1) as
/// <see cref="DiagnosticCommandChannel"/>/<see cref="UiDumpService"/>, toggled by F11
/// (<c>LayoutEditModeMessageFilter</c> in Program.cs). Click a control in the active dialog to
/// select it, nudge its geometry with arrow keys (live - no rebuild), then Ctrl+C a ready-to-paste
/// C# snippet reflecting the accumulated change.
///
/// <para><b>Why this exists.</b> Fixing a pixel-level layout bug in this app's code-only UI
/// (no .Designer.cs/.resx - see CLAUDE.md) previously meant: measure a screenshot's pixels, guess a
/// Margin/Padding/RowStyle value, edit source, rebuild (~10s), screenshot again, re-measure -
/// sometimes 2-3 iterations per bug. This turns that loop into: click, nudge, copy.</para>
///
/// <para>Also supports mouse drag: dragging the selected control's body moves it, dragging one of
/// the 8 handles <see cref="LayoutEditHighlight"/> draws around it resizes it - see
/// <see cref="BeginDrag"/>/<see cref="ContinueDrag"/>/<see cref="EndDrag"/>.</para>
///
/// <para><b>Deliberately out of scope</b> (see the approved plan): no new controls, no
/// auto-applying changes to source files (clipboard only - the developer decides where a snippet
/// belongs), no multi-step undo (Export always diffs against the snapshot taken at
/// <see cref="Select"/> time, not the previous nudge/drag).</para>
/// </summary>
public static class LayoutEditModeService
{
    public static bool IsActive { get; private set; }
    public static Control? Selected { get; private set; }

    /// <summary>Whether a mouse drag (move or resize) is currently in progress - the message
    /// filter needs this to decide whether to swallow WM_MOUSEMOVE/WM_LBUTTONUP at all, since those
    /// fire constantly for ordinary mouse traffic elsewhere in the app while merely
    /// <see cref="IsActive"/> and not dragging.</summary>
    public static bool IsDragging => _dragActive;

    private static LayoutEditHud? _hud;
    private static Snapshot? _baseline;
    private static Control? _watchedTopLevel;

    private static bool _dragActive;
    private static bool _dragIsMove;
    private static HandleKind _dragHandle = HandleKind.None;
    private static Point _dragLastScreenPoint;

    /// <summary>Immutable geometry read at <see cref="Select"/> time - <see cref="ExportToClipboard"/>
    /// always diffs the control's current state against this, never against the previous nudge.</summary>
    private sealed record Snapshot(
        Padding Margin, Padding Padding, Point Location,
        int? RowIndex, float? RowHeight, int? ColIndex, float? ColWidth);

    /// <summary>Flips edit mode on/off. Turning off clears selection, highlight and the HUD.</summary>
    public static void Toggle()
    {
        IsActive = !IsActive;
        LogService.Debug($"Layout edit mode {(IsActive ? "ON" : "OFF")}", "LayoutEdit");

        if (IsActive)
        {
            _hud = new LayoutEditHud();
            _hud.Show();
        }
        else
        {
            EndDrag();
            ClearSelection();
            _hud?.Dispose();
            _hud = null;
        }
    }

    /// <summary>Selects <paramref name="control"/>: erases any previous highlight, snapshots its
    /// current geometry as the export baseline, and draws a fresh highlight around it.</summary>
    public static void Select(Control control)
    {
        UnwatchTopLevel();
        Selected = control;
        _baseline = CaptureSnapshot(control);
        LayoutEditHighlight.Show(control);
        WatchTopLevel(control);
        _hud?.UpdateFor(control);
    }

    /// <summary>Deselects the current control, if any, and erases its highlight.</summary>
    public static void ClearSelection()
    {
        UnwatchTopLevel();
        Selected = null;
        _baseline = null;
        LayoutEditHighlight.Clear();
        _hud?.UpdateFor(null);
    }

    public static void NudgeMargin(int dx, int dy) => Nudge((c, x, y) =>
    {
        var m = c.Margin;
        int left = m.Left, top = m.Top, right = m.Right, bottom = m.Bottom;
        if (x < 0) left = Math.Max(0, left + x);
        else if (x > 0) right = Math.Max(0, right + x);
        if (y < 0) top = Math.Max(0, top + y);
        else if (y > 0) bottom = Math.Max(0, bottom + y);
        c.Margin = new Padding(left, top, right, bottom);
    }, dx, dy);

    public static void NudgePadding(int dx, int dy) => Nudge((c, x, y) =>
    {
        var p = c.Padding;
        int left = p.Left, top = p.Top, right = p.Right, bottom = p.Bottom;
        if (x < 0) left = Math.Max(0, left + x);
        else if (x > 0) right = Math.Max(0, right + x);
        if (y < 0) top = Math.Max(0, top + y);
        else if (y > 0) bottom = Math.Max(0, bottom + y);
        c.Padding = new Padding(left, top, right, bottom);
    }, dx, dy);

    /// <summary>Moves a Dock=None control's Bounds.Location directly - there's no Margin-driven
    /// position to nudge for a freely-positioned control.</summary>
    public static void NudgeLocation(int dx, int dy) => Nudge((c, x, y) =>
        c.Location = new Point(c.Location.X + x, c.Location.Y + y), dx, dy);

    /// <summary>Adjusts the selected control's TableLayoutPanel row's Absolute height, if
    /// applicable. No-op (logged, not thrown) otherwise.</summary>
    public static void NudgeTableRow(int delta)
    {
        if (Selected?.Parent is not TableLayoutPanel tlp) return;
        var pos = tlp.GetPositionFromControl(Selected);
        if (pos.Row < 0 || pos.Row >= tlp.RowStyles.Count) return;
        var style = tlp.RowStyles[pos.Row];
        if (style.SizeType != SizeType.Absolute) return;

        LayoutEditHighlight.Clear();
        style.Height = Math.Max(0, style.Height + delta);
        tlp.PerformLayout();
        tlp.Update();
        LayoutEditHighlight.Show(Selected);
        _hud?.UpdateFor(Selected);
    }

    /// <summary>Adjusts the selected control's TableLayoutPanel column's Absolute width, if
    /// applicable. No-op (logged, not thrown) otherwise.</summary>
    public static void NudgeTableColumn(int delta)
    {
        if (Selected?.Parent is not TableLayoutPanel tlp) return;
        var pos = tlp.GetPositionFromControl(Selected);
        if (pos.Column < 0 || pos.Column >= tlp.ColumnStyles.Count) return;
        var style = tlp.ColumnStyles[pos.Column];
        if (style.SizeType != SizeType.Absolute) return;

        LayoutEditHighlight.Clear();
        style.Width = Math.Max(0, style.Width + delta);
        tlp.PerformLayout();
        tlp.Update();
        LayoutEditHighlight.Show(Selected);
        _hud?.UpdateFor(Selected);
    }

    /// <summary>Mouse-move-only: shifts a Dock=Fill TableLayoutPanel child within its cell while
    /// preserving its size - grows the margin on the leading side and shrinks the trailing side by
    /// the same (clamped) amount, so <c>CellWidth - Left - Right</c> (the Dock=Fill width formula)
    /// stays constant while the box visually slides. Not reachable from the keyboard (arrow keys
    /// keep their original single-edge "nudge one margin value" behavior via <see cref="NudgeMargin"/>
    /// unchanged) - this is deliberately a mouse-drag-only refinement so existing, already-verified
    /// keyboard nudging never changes behavior.</summary>
    public static void NudgeMoveFill(int dx, int dy) => Nudge((c, x, y) =>
    {
        var m = c.Margin;
        int left = m.Left, top = m.Top, right = m.Right, bottom = m.Bottom;
        if (x > 0) { var d = Math.Min(x, right); left += d; right -= d; }
        else if (x < 0) { var d = Math.Min(-x, left); left -= d; right += d; }
        if (y > 0) { var d = Math.Min(y, bottom); top += d; bottom -= d; }
        else if (y < 0) { var d = Math.Min(-y, top); top -= d; bottom += d; }
        c.Margin = new Padding(left, top, right, bottom);
    }, dx, dy);

    /// <summary>Mouse-resize-handle-only: grows/shrinks a Dock=Fill TableLayoutPanel child from the
    /// dragged edge, matching ordinary drag-resize intuition (dragging the East edge rightward grows
    /// the control by shrinking its Right margin, not growing it). Deliberately a separate method
    /// from <see cref="NudgeMargin"/> rather than reusing it: that method's arrow-key semantics
    /// (Right always increases Margin.Right, e.g. to widen a *gap*) are the opposite of what a resize
    /// handle needs on the trailing edges, and reusing it under the arrow-key sign convention would
    /// make dragging a control's right/bottom edge outward visibly shrink it.</summary>
    public static void NudgeResizeMargin(HandleKind handle, int dx, int dy) => Nudge((c, x, y) =>
    {
        var m = c.Margin;
        int left = m.Left, top = m.Top, right = m.Right, bottom = m.Bottom;
        switch (handle)
        {
            case HandleKind.W: left = Math.Max(0, left + x); break;
            case HandleKind.E: right = Math.Max(0, right - x); break;
            case HandleKind.N: top = Math.Max(0, top + y); break;
            case HandleKind.S: bottom = Math.Max(0, bottom - y); break;
            case HandleKind.NW: left = Math.Max(0, left + x); top = Math.Max(0, top + y); break;
            case HandleKind.NE: right = Math.Max(0, right - x); top = Math.Max(0, top + y); break;
            case HandleKind.SW: left = Math.Max(0, left + x); bottom = Math.Max(0, bottom - y); break;
            case HandleKind.SE: right = Math.Max(0, right - x); bottom = Math.Max(0, bottom - y); break;
        }
        c.Margin = new Padding(left, top, right, bottom);
    }, dx, dy);

    /// <summary>Mouse-resize-handle-only: grows/shrinks a Dock=None control's own Bounds from the
    /// dragged edge - there's no independent size lever for a Dock=None control other than its own
    /// Size, regardless of what kind of parent it sits in (TableLayoutPanel/FlowLayoutPanel/plain
    /// Panel all leave a Dock=None child's Size alone). North/West-side handles also shift Location
    /// by the same delta the opposite edge grows by, so the anchored (opposite) edge stays put -
    /// the standard "drag the top-left handle" resize convention.</summary>
    public static void NudgeBounds(HandleKind handle, int dx, int dy) => Nudge((c, x, y) =>
    {
        var (dxLoc, dyLoc, dw, dh) = handle switch
        {
            HandleKind.E => (0, 0, x, 0),
            HandleKind.W => (x, 0, -x, 0),
            HandleKind.S => (0, 0, 0, y),
            HandleKind.N => (0, y, 0, -y),
            HandleKind.SE => (0, 0, x, y),
            HandleKind.SW => (x, 0, -x, y),
            HandleKind.NE => (0, y, x, -y),
            HandleKind.NW => (x, y, -x, -y),
            _ => (0, 0, 0, 0),
        };
        c.SetBounds(c.Left + dxLoc, c.Top + dyLoc, Math.Max(1, c.Width + dw), Math.Max(1, c.Height + dh));
    }, dx, dy);

    /// <summary>Shared nudge plumbing: erase highlight at the old rect, mutate via
    /// <paramref name="apply"/>, re-layout the immediate parent (the parent always owns both
    /// Margin's interpretation and its own RowStyles/ColumnStyles - no deeper ancestor call is
    /// needed, WinForms' own layout cascade handles anything beyond that), force the repaint to
    /// happen before the highlight is redrawn, then draw the new highlight and refresh the HUD.</summary>
    private static void Nudge(Action<Control, int, int> apply, int dx, int dy)
    {
        if (Selected is not { } c) return;

        LayoutEditHighlight.Clear();
        apply(c, dx, dy);
        c.Parent?.PerformLayout();
        c.Parent?.Update();
        LayoutEditHighlight.Show(c);
        _hud?.UpdateFor(c);
    }

    /// <summary>Diffs the selected control's current geometry against the snapshot taken at
    /// <see cref="Select"/> time and copies a C# snippet for the properties that actually changed.
    /// Uses the control's own <see cref="Control.Name"/> as the snippet's receiver when set,
    /// otherwise a "&lt;selected&gt;" placeholder - most dialogs in this codebase never set a
    /// button/field's Name, so the developer substitutes the real field identifier by hand; that's
    /// an accepted limitation, not a defect to solve here.</summary>
    public static void ExportToClipboard()
    {
        if (Selected is not { } c || _baseline is not { } baseline) return;

        var receiver = string.IsNullOrEmpty(c.Name) ? "<selected>" : c.Name;
        var lines = new List<string>();

        if (c.Margin != baseline.Margin)
            lines.Add($"{receiver}.Margin = new Padding({c.Margin.Left}, {c.Margin.Top}, {c.Margin.Right}, {c.Margin.Bottom}); " +
                      $"// was ({baseline.Margin.Left}, {baseline.Margin.Top}, {baseline.Margin.Right}, {baseline.Margin.Bottom})");

        if (c.Padding != baseline.Padding)
            lines.Add($"{receiver}.Padding = new Padding({c.Padding.Left}, {c.Padding.Top}, {c.Padding.Right}, {c.Padding.Bottom}); " +
                      $"// was ({baseline.Padding.Left}, {baseline.Padding.Top}, {baseline.Padding.Right}, {baseline.Padding.Bottom})");

        if (c.Dock == DockStyle.None && c.Location != baseline.Location)
            lines.Add($"{receiver}.Location = new Point({c.Location.X}, {c.Location.Y}); " +
                      $"// was ({baseline.Location.X}, {baseline.Location.Y})");

        if (c.Parent is TableLayoutPanel tlp)
        {
            var parentRef = string.IsNullOrEmpty(tlp.Name) ? $"((TableLayoutPanel){receiver}.Parent)" : tlp.Name;
            var pos = tlp.GetPositionFromControl(c);

            if (baseline.RowIndex == pos.Row && pos.Row >= 0 && pos.Row < tlp.RowStyles.Count)
            {
                var row = tlp.RowStyles[pos.Row];
                if (row.SizeType == SizeType.Absolute && Math.Abs(row.Height - (baseline.RowHeight ?? row.Height)) > 0.01f)
                    lines.Add($"{parentRef}.RowStyles[{pos.Row}] = new RowStyle(SizeType.Absolute, {row.Height}f); " +
                              $"// was {baseline.RowHeight}");
            }
            if (baseline.ColIndex == pos.Column && pos.Column >= 0 && pos.Column < tlp.ColumnStyles.Count)
            {
                var col = tlp.ColumnStyles[pos.Column];
                if (col.SizeType == SizeType.Absolute && Math.Abs(col.Width - (baseline.ColWidth ?? col.Width)) > 0.01f)
                    lines.Add($"{parentRef}.ColumnStyles[{pos.Column}] = new ColumnStyle(SizeType.Absolute, {col.Width}f); " +
                              $"// was {baseline.ColWidth}");
            }
        }

        if (lines.Count == 0)
        {
            LogService.Debug("Layout edit: nothing changed since selection, nothing copied", "LayoutEdit");
            return;
        }

        Clipboard.SetText(string.Join(Environment.NewLine, lines));
        LogService.Debug($"Layout edit: copied {lines.Count} line(s) to clipboard", "LayoutEdit");
    }

    /// <summary>Called from MainForm's FormClosed - erases any live highlight and disposes the HUD
    /// so app shutdown never leaves a stray always-on-top window or an un-erased XOR frame.</summary>
    public static void Shutdown()
    {
        EndDrag();
        UnwatchTopLevel();
        LayoutEditHighlight.Clear();
        _hud?.Dispose();
        _hud = null;
        IsActive = false;
        Selected = null;
        _baseline = null;
    }

    /// <summary>Starts a mouse drag on the currently-selected control - <paramref name="handle"/>
    /// is <see cref="HandleKind.None"/> for a body drag (move) or the specific handle for a resize
    /// drag, with <paramref name="isBodyMove"/> disambiguating the two (a body click still resolves
    /// to <see cref="HandleKind.None"/> from the hit-test, so the caller has to say which one this
    /// is explicitly). Captures the mouse on the HUD - always alive while <see cref="IsActive"/>,
    /// so it's a stable target - because the triggering WM_LBUTTONDOWN was swallowed by the message
    /// filter before any real control's WndProc ran, meaning no implicit <c>Control.Capture</c> ever
    /// happened; without this, a fast drag that crosses outside the target dialog's window bounds
    /// would stop receiving WM_MOUSEMOVE/WM_LBUTTONUP entirely.</summary>
    public static void BeginDrag(HandleKind handle, bool isBodyMove, Point screenPoint)
    {
        if (Selected is null) return;
        _dragActive = true;
        _dragIsMove = isBodyMove;
        _dragHandle = handle;
        _dragLastScreenPoint = screenPoint;
        if (_hud is { IsDisposed: false }) _hud.Capture = true;
    }

    /// <summary>Applies the mouse-move delta since the last call (or since <see cref="BeginDrag"/>)
    /// to the selected control's geometry - move or resize, per the mode <see cref="BeginDrag"/> was
    /// started with. Routes through <see cref="Selected"/>'s Dock/Parent exactly the way the
    /// keyboard's arrow-key handling does for move (unchanged, see <c>LayoutEditModeMessageFilter.
    /// HandleKey</c> in Program.cs), plus the new Dock=Fill-in-TableLayoutPanel and Dock=None resize
    /// paths this drag feature adds. Holding Alt while resizing a TableLayoutPanel child's cell
    /// (any Dock, not just Fill) resizes that row/column's Absolute size instead - the same Alt
    /// meaning the keyboard's Alt+Arrow already has.</summary>
    public static void ContinueDrag(Point screenPoint)
    {
        if (!_dragActive || Selected is not { } c) return;

        int dx = screenPoint.X - _dragLastScreenPoint.X;
        int dy = screenPoint.Y - _dragLastScreenPoint.Y;
        if (dx == 0 && dy == 0) return;
        _dragLastScreenPoint = screenPoint;

        var fillInTable = c.Dock == DockStyle.Fill && c.Parent is TableLayoutPanel;

        if (_dragIsMove)
        {
            if (fillInTable) NudgeMoveFill(dx, dy);
            else if (c.Parent is TableLayoutPanel or FlowLayoutPanel) NudgeMargin(dx, dy);
            else if (c.Dock == DockStyle.None) NudgeLocation(dx, dy);
            // else: position is Dock-computed on a plain container - not directly draggable,
            // matching the keyboard's own gap for the same case.
        }
        else
        {
            var alt = (Control.ModifierKeys & Keys.Alt) != 0;
            if (alt && c.Parent is TableLayoutPanel)
            {
                if (dx != 0) NudgeTableColumn(dx);
                if (dy != 0) NudgeTableRow(dy);
            }
            else if (fillInTable)
            {
                NudgeResizeMargin(_dragHandle, dx, dy);
            }
            else if (c.Dock == DockStyle.None)
            {
                NudgeBounds(_dragHandle, dx, dy);
            }
            // else: no handles are ever shown for this control (see LayoutEditHighlight - handle
            // drawing is unconditional today; the resize-unsupported case simply no-ops here).
        }
    }

    /// <summary>Ends the current drag (if any) and releases the mouse capture <see cref="BeginDrag"/>
    /// took. Safe to call with no drag in progress.</summary>
    public static void EndDrag()
    {
        _dragActive = false;
        _dragHandle = HandleKind.None;
        if (_hud is { IsDisposed: false }) _hud.Capture = false;
    }

    private static Snapshot CaptureSnapshot(Control c)
    {
        int? rowIdx = null, colIdx = null;
        float? rowH = null, colW = null;
        if (c.Parent is TableLayoutPanel tlp)
        {
            var pos = tlp.GetPositionFromControl(c);
            if (pos.Row >= 0 && pos.Row < tlp.RowStyles.Count)
            {
                rowIdx = pos.Row;
                var rs = tlp.RowStyles[pos.Row];
                if (rs.SizeType == SizeType.Absolute) rowH = rs.Height;
            }
            if (pos.Column >= 0 && pos.Column < tlp.ColumnStyles.Count)
            {
                colIdx = pos.Column;
                var cs = tlp.ColumnStyles[pos.Column];
                if (cs.SizeType == SizeType.Absolute) colW = cs.Width;
            }
        }
        return new Snapshot(c.Margin, c.Padding, c.Location, rowIdx, rowH, colIdx, colW);
    }

    /// <summary>
    /// Keeps the XOR highlight valid across the selected control's top-level window moving or
    /// resizing (its screen position changed, so the frame is redrawn at the new spot - not
    /// dropped, the selection itself is still perfectly valid) and drops the selection outright
    /// only on FormClosing (the window is being destroyed, nothing left to highlight).
    ///
    /// <para>An earlier version also cleared on <c>Deactivate</c>, reasoning that losing window
    /// activation invalidates the highlight the same way. In practice this fired far more often
    /// than a genuine "user clicked away to another window" - clicking a control and immediately
    /// pressing an arrow key or Ctrl+C could observe <see cref="Selected"/> already cleared,
    /// silently no-opping the nudge/export the user just asked for. Since a stale highlight while
    /// the window is merely inactive is a minor, self-correcting cosmetic gap (any next click on
    /// any control redraws it), not clearing on Deactivate at all is the better trade - see the
    /// bug report this fixed.</para>
    /// </summary>
    private static void WatchTopLevel(Control c)
    {
        _watchedTopLevel = c.TopLevelControl;
        if (_watchedTopLevel is not { } top) return;
        top.Move += OnTopLevelMovedOrResized;
        top.Resize += OnTopLevelMovedOrResized;
        if (top is Form f)
            f.FormClosing += OnTopLevelClosing;
    }

    private static void UnwatchTopLevel()
    {
        if (_watchedTopLevel is not { } top) { _watchedTopLevel = null; return; }
        top.Move -= OnTopLevelMovedOrResized;
        top.Resize -= OnTopLevelMovedOrResized;
        if (top is Form f)
            f.FormClosing -= OnTopLevelClosing;
        _watchedTopLevel = null;
    }

    private static void OnTopLevelMovedOrResized(object? sender, EventArgs e)
    {
        if (Selected is { } c) LayoutEditHighlight.Show(c);
    }

    private static void OnTopLevelClosing(object? sender, EventArgs e) => ClearSelection();
}
