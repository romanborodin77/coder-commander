namespace CoderCommander.Terminal.Screen;

/// <summary>One row of terminal cells.</summary>
internal sealed class TerminalRow
{
    public TerminalCell[] Cells;

    /// <summary>True if this row's content continues onto the next row because autowrap kicked
    /// in - copy/selection joins wrapped rows without inserting a line break; ED/eviction logic
    /// treats a wrapped row differently from a hard-terminated one.</summary>
    public bool Wrapped;

    /// <summary>Column -&gt; combining-mark tail, allocated lazily (most rows never need it).
    /// Capped per cell at <see cref="Vt.VtLimits.MaxCombiningPerCell"/> by the caller.</summary>
    public Dictionary<int, string>? Combining;

    /// <summary>Bumped on every mutation - cheap staleness check for anything caching per-row
    /// derived data (e.g. a rendered text cache).</summary>
    public int Version;

    public TerminalRow(int cols, CellColor bg)
    {
        Cells = new TerminalCell[cols];
        for (var i = 0; i < cols; i++)
            Cells[i] = TerminalCell.Blank(bg);
    }

    public void ClearRange(int fromCol, int toColExclusive, CellColor bg)
    {
        fromCol = Math.Max(0, fromCol);
        toColExclusive = Math.Min(Cells.Length, toColExclusive);
        for (var i = fromCol; i < toColExclusive; i++)
        {
            Cells[i] = TerminalCell.Blank(bg);
            Combining?.Remove(i);
        }
        Version++;
    }

    public void ClearAll(CellColor bg)
    {
        ClearRange(0, Cells.Length, bg);
        Wrapped = false;
    }

    /// <summary>Grows or shrinks the row to <paramref name="newCols"/>, preserving existing
    /// content and padding new cells with <paramref name="bg"/>.</summary>
    public void Resize(int newCols, CellColor bg)
    {
        if (newCols == Cells.Length) return;

        var old = Cells;
        Cells = new TerminalCell[newCols];
        var copyCount = Math.Min(old.Length, newCols);
        Array.Copy(old, Cells, copyCount);
        for (var i = copyCount; i < newCols; i++)
            Cells[i] = TerminalCell.Blank(bg);

        if (Combining != null)
        {
            List<int>? toRemove = null;
            foreach (var col in Combining.Keys)
            {
                if (col >= newCols)
                    (toRemove ??= []).Add(col);
            }
            if (toRemove != null)
                foreach (var col in toRemove)
                    Combining.Remove(col);
        }

        Version++;
    }

    public void AttachCombining(int col, int rune, int maxPerCell)
    {
        if (col < 0 || col >= Cells.Length) return;

        Combining ??= new Dictionary<int, string>();
        var existing = Combining.TryGetValue(col, out var tail) ? tail : "";
        var count = 0;
        for (var i = 0; i < existing.Length; i += char.IsSurrogatePair(existing, i) ? 2 : 1)
            count++;
        if (count >= maxPerCell)
            return; // zalgo guard - silently drop marks beyond the cap

        Combining[col] = existing + char.ConvertFromUtf32(rune);
        Cells[col].Flags |= CellFlags.HasCombining;
        Version++;
    }
}
