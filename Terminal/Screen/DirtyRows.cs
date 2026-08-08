namespace CoderCommander.Terminal.Screen;

/// <summary>Tracks which visible rows changed since the canvas last painted, as a bitset -
/// consumed and cleared by the UI layer under the screen's lock. <see cref="FullRepaint"/> covers
/// the cases where tracking individual rows isn't worth it (ED 2, alt-screen switch, resize,
/// theme change).</summary>
internal sealed class DirtyRows
{
    private ulong[] _bits;
    private int _rows;

    public bool FullRepaint { get; private set; } = true;

    public DirtyRows(int rows) => Reset(rows);

    public void Reset(int rows)
    {
        _rows = rows;
        _bits = new ulong[(rows + 63) / 64];
        FullRepaint = true;
    }

    public void MarkRow(int row)
    {
        if (row < 0 || row >= _rows) return;
        _bits[row >> 6] |= 1UL << (row & 63);
    }

    public void MarkAll() => FullRepaint = true;

    public bool IsDirty(int row) =>
        FullRepaint || (row >= 0 && row < _rows && (_bits[row >> 6] & (1UL << (row & 63))) != 0);

    /// <summary>Consumes the current dirty state - callers should snapshot what's dirty (or just
    /// repaint everything if <see cref="FullRepaint"/>) before calling this.</summary>
    public void Clear()
    {
        Array.Clear(_bits);
        FullRepaint = false;
    }
}
