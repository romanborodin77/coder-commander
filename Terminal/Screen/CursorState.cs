namespace CoderCommander.Terminal.Screen;

/// <summary>Everything DECSC (ESC 7) saves and DECRC (ESC 8) restores - kept per-screen (main and
/// alt each have their own saved cursor).</summary>
internal struct CursorState
{
    public int Row;
    public int Col;

    /// <summary>Set when a printable character just filled the last column - the next printable
    /// character (not a cursor-positioning sequence) triggers the actual wrap. Any
    /// cursor-positioning sequence clears this.</summary>
    public bool PendingWrap;

    public CellColor Fg;
    public CellColor Bg;
    public CellFlags Attrs;

    /// <summary>0 = G0 charset selected, 1 = G1. <see cref="G0IsDecGraphics"/>/
    /// <see cref="G1IsDecGraphics"/> say which charset each slot currently holds.</summary>
    public bool UsingG1;
    public bool G0IsDecGraphics;
    public bool G1IsDecGraphics;

    public static CursorState Initial(CellColor defaultFg, CellColor defaultBg) => new()
    {
        Row = 0,
        Col = 0,
        PendingWrap = false,
        Fg = defaultFg,
        Bg = defaultBg,
        Attrs = CellFlags.None,
        UsingG1 = false,
        G0IsDecGraphics = false,
        G1IsDecGraphics = false
    };
}
