namespace CoderCommander.Terminal.Screen;

/// <summary>
/// One screen's worth of rows, plus (for the main buffer only) scrollback. Owns tab stops and the
/// scroll region - both are per-buffer state so switching to the alt screen and back doesn't
/// disturb the main screen's.
/// </summary>
internal sealed class TerminalBuffer
{
    private TerminalRow[] _grid;

    public int Rows { get; private set; }
    public int Cols { get; private set; }

    /// <summary>Null for the alt screen - it has no scrollback at all.</summary>
    public ScrollbackRing? Scrollback { get; }

    /// <summary>0-based, inclusive scroll region.</summary>
    public int ScrollTop { get; set; }
    public int ScrollBottom { get; set; }

    public bool[] TabStops { get; internal set; }

    public TerminalBuffer(int rows, int cols, bool withScrollback, int scrollbackCapacity, CellColor bg)
    {
        Rows = rows;
        Cols = cols;
        _grid = new TerminalRow[rows];
        for (var i = 0; i < rows; i++) _grid[i] = new TerminalRow(cols, bg);
        Scrollback = withScrollback ? new ScrollbackRing(scrollbackCapacity) : null;
        ScrollTop = 0;
        ScrollBottom = rows - 1;
        TabStops = BuildDefaultTabStops(cols);
    }

    public TerminalRow this[int row] => _grid[row];

    public bool IsFullScreenRegion => ScrollTop == 0 && ScrollBottom == Rows - 1;

    internal static bool[] BuildDefaultTabStops(int cols)
    {
        var stops = new bool[cols];
        for (var i = 0; i < cols; i += 8) stops[i] = true;
        return stops;
    }

    /// <summary>
    /// Resizes in place. Width changes resize each row (truncate/pad). Height changes append
    /// blank rows on grow; on shrink, rows falling off the TOP (which is what "the window got
    /// shorter" means visually - content anchors to the bottom) are pushed to scrollback if this
    /// buffer has one (silently dropped for the alt screen, which has none). Deliberately does
    /// NOT reflow long lines - ConPTY's own client-side buffer already reflows and re-emits on
    /// resize, so reflowing here too would double-correct.
    /// </summary>
    public void Resize(int newRows, int newCols, CellColor bg)
    {
        foreach (var row in _grid) row.Resize(newCols, bg);

        if (newRows != Rows)
        {
            var newGrid = new TerminalRow[newRows];
            if (newRows > Rows)
            {
                Array.Copy(_grid, newGrid, Rows);
                for (var i = Rows; i < newRows; i++) newGrid[i] = new TerminalRow(newCols, bg);
            }
            else
            {
                var overflow = Rows - newRows;
                for (var i = 0; i < overflow; i++)
                    Scrollback?.Push(_grid[i]);
                Array.Copy(_grid, overflow, newGrid, 0, newRows);
            }
            _grid = newGrid;
            Rows = newRows;
        }

        Cols = newCols;
        ScrollTop = 0;
        ScrollBottom = Rows - 1;

        var newTabStops = new bool[newCols];
        Array.Copy(TabStops, newTabStops, Math.Min(TabStops.Length, newCols));
        for (var i = TabStops.Length; i < newCols; i++)
            if (i % 8 == 0) newTabStops[i] = true;
        TabStops = newTabStops;
    }

    /// <summary>
    /// Scrolls the scroll region up by one line (new blank line appears at ScrollBottom). The row
    /// scrolled off ScrollTop goes to scrollback ONLY when the region is the full screen - a
    /// narrower region (set by DECSTBM, e.g. a status-bar-locking TUI app) means the row is
    /// discarded instead. Without this distinction, status bars in less/vim/htop would spray
    /// garbage into the scrollback on every redraw.
    /// </summary>
    public void ScrollRegionUp(int lines = 1)
    {
        for (var n = 0; n < lines; n++)
        {
            if (IsFullScreenRegion)
                Scrollback?.Push(_grid[ScrollTop]);

            for (var r = ScrollTop; r < ScrollBottom; r++)
                _grid[r] = _grid[r + 1];

            _grid[ScrollBottom] = new TerminalRow(Cols, CellColor.Default);
        }
    }

    /// <summary>Scrolls the scroll region down by one line (new blank line appears at ScrollTop).
    /// Never touches scrollback - SD only ever discards the bottom row of the region.</summary>
    public void ScrollRegionDown(int lines = 1)
    {
        for (var n = 0; n < lines; n++)
        {
            for (var r = ScrollBottom; r > ScrollTop; r--)
                _grid[r] = _grid[r - 1];

            _grid[ScrollTop] = new TerminalRow(Cols, CellColor.Default);
        }
    }

    /// <summary>IL (Insert Line): inserts <paramref name="n"/> blank lines at <paramref name="atRow"/>
    /// (which must be inside the scroll region), shifting rows between it and ScrollBottom down;
    /// rows pushed past ScrollBottom are discarded - never scrollback, this is a region-local
    /// edit, not a screen-wide scroll. Clipped to the region: a count larger than the remaining
    /// region height is clamped rather than affecting rows outside [ScrollTop, ScrollBottom].</summary>
    public void InsertLinesAt(int atRow, int n)
    {
        if (atRow < ScrollTop || atRow > ScrollBottom) return;
        n = Math.Min(n, ScrollBottom - atRow + 1);
        for (var k = 0; k < n; k++)
        {
            for (var r = ScrollBottom; r > atRow; r--)
                _grid[r] = _grid[r - 1];
            _grid[atRow] = new TerminalRow(Cols, CellColor.Default);
        }
    }

    /// <summary>DL (Delete Line): deletes <paramref name="n"/> lines starting at
    /// <paramref name="atRow"/>, shifting rows below it up within the scroll region and filling
    /// the vacated bottom rows with blanks. Same region-clipping rule as
    /// <see cref="InsertLinesAt"/>.</summary>
    public void DeleteLinesAt(int atRow, int n)
    {
        if (atRow < ScrollTop || atRow > ScrollBottom) return;
        n = Math.Min(n, ScrollBottom - atRow + 1);
        for (var k = 0; k < n; k++)
        {
            for (var r = atRow; r < ScrollBottom; r++)
                _grid[r] = _grid[r + 1];
            _grid[ScrollBottom] = new TerminalRow(Cols, CellColor.Default);
        }
    }
}
