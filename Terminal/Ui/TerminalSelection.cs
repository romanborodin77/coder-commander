namespace CoderCommander.Terminal.Ui;

/// <summary>
/// Text selection anchored in the "combined" coordinate space - scrollback rows followed by the
/// active screen's rows, line 0 = the oldest retained scrollback line (see
/// <c>TerminalCanvas.CombinedLineCount</c>/<c>GetCombinedRow</c>) - rather than viewport-relative
/// rows, so the selection stays anchored to the same text as the user scrolls. Two shapes: linear
/// (normal drag, follows line wrapping) and block (Alt+drag, a rectangular column range applied to
/// every covered line independently - useful for pulling a column out of tabular output).
/// </summary>
internal sealed class TerminalSelection
{
    public int AnchorLine { get; private set; }
    public int AnchorCol { get; private set; }
    public int ActiveLine { get; private set; }
    public int ActiveCol { get; private set; }
    public bool IsBlock { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>True once the drag has covered more than a single cell - a bare click (no drag)
    /// leaves nothing meaningfully selected.</summary>
    public bool HasSelection => IsActive && (AnchorLine != ActiveLine || AnchorCol != ActiveCol);

    public void Start(int line, int col, bool block)
    {
        AnchorLine = ActiveLine = line;
        AnchorCol = ActiveCol = col;
        IsBlock = block;
        IsActive = true;
    }

    public void Extend(int line, int col)
    {
        if (!IsActive) return;
        ActiveLine = line;
        ActiveCol = col;
    }

    public void Clear() => IsActive = false;

    /// <summary>Shifts both endpoints by <paramref name="delta"/> lines - used when new rows are
    /// pushed into scrollback while this selection is anchored to a now-stale combined-space
    /// index (see <c>TerminalCanvas.ReanchorForScrollbackGrowth</c>).</summary>
    public void ShiftLines(int delta)
    {
        AnchorLine += delta;
        ActiveLine += delta;
    }

    /// <summary>Linear-mode normalized range: (startLine, startCol) is always chronologically
    /// before or equal to (endLine, endCol), regardless of drag direction.</summary>
    public (int Line, int Col, int EndLine, int EndCol) NormalizedRange()
    {
        if (AnchorLine < ActiveLine || (AnchorLine == ActiveLine && AnchorCol <= ActiveCol))
            return (AnchorLine, AnchorCol, ActiveLine, ActiveCol);
        return (ActiveLine, ActiveCol, AnchorLine, AnchorCol);
    }

    /// <summary>Block-mode normalized rectangle: independent min/max of line and column.</summary>
    public (int TopLine, int LeftCol, int BottomLine, int RightCol) NormalizedBlock()
    {
        return (Math.Min(AnchorLine, ActiveLine), Math.Min(AnchorCol, ActiveCol),
                Math.Max(AnchorLine, ActiveLine), Math.Max(AnchorCol, ActiveCol));
    }

    /// <summary>True if the cell at (line, col) falls inside the current selection. Both start and
    /// end cells are inclusive - dragging "through" a character selects it, matching the drag
    /// gesture's visual endpoint.</summary>
    public bool Contains(int line, int col)
    {
        if (!HasSelection) return false;

        if (IsBlock)
        {
            var (top, left, bottom, right) = NormalizedBlock();
            return line >= top && line <= bottom && col >= left && col <= right;
        }

        var (l1, c1, l2, c2) = NormalizedRange();
        if (line < l1 || line > l2) return false;
        if (line == l1 && col < c1) return false;
        if (line == l2 && col > c2) return false;
        return true;
    }
}
