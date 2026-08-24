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
/// <para><b>Deliberately out of scope</b> (see the approved plan): no drag-and-drop, no new
/// controls, no auto-applying changes to source files (clipboard only - the developer decides where
/// a snippet belongs), no multi-step undo (Export always diffs against the snapshot taken at
/// <see cref="Select"/> time, not the previous nudge).</para>
/// </summary>
public static class LayoutEditModeService
{
    public static bool IsActive { get; private set; }
    public static Control? Selected { get; private set; }

    private static LayoutEditHud? _hud;
    private static Snapshot? _baseline;
    private static Control? _watchedTopLevel;

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
        UnwatchTopLevel();
        LayoutEditHighlight.Clear();
        _hud?.Dispose();
        _hud = null;
        IsActive = false;
        Selected = null;
        _baseline = null;
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

    /// <summary>Defensive highlight clearing (see <see cref="LayoutEditHighlight"/>'s own doc
    /// comment on XOR fragility): the selected control's top-level window moving, resizing,
    /// losing activation, or closing all invalidate the drawn frame's premise, so all four just
    /// drop it rather than try to track/repair it.</summary>
    private static void WatchTopLevel(Control c)
    {
        _watchedTopLevel = c.TopLevelControl;
        if (_watchedTopLevel is not { } top) return;
        top.Move += OnTopLevelInvalidated;
        top.Resize += OnTopLevelInvalidated;
        if (top is Form f)
        {
            f.Deactivate += OnTopLevelInvalidated;
            f.FormClosing += OnTopLevelInvalidated;
        }
    }

    private static void UnwatchTopLevel()
    {
        if (_watchedTopLevel is not { } top) { _watchedTopLevel = null; return; }
        top.Move -= OnTopLevelInvalidated;
        top.Resize -= OnTopLevelInvalidated;
        if (top is Form f)
        {
            f.Deactivate -= OnTopLevelInvalidated;
            f.FormClosing -= OnTopLevelInvalidated;
        }
        _watchedTopLevel = null;
    }

    private static void OnTopLevelInvalidated(object? sender, EventArgs e) => ClearSelection();
}
