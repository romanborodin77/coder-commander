namespace CoderCommander.Terminal.Screen;

/// <summary>Which of the three ways a <see cref="CellColor"/> encodes a color.</summary>
internal enum ColorKind : byte
{
    Default = 0,
    Indexed = 1,
    Rgb = 2
}

/// <summary>
/// A cell's foreground/background color, packed into 4 bytes: the top byte is
/// <see cref="ColorKind"/>, the low 24 bits are the payload (a palette index 0-255, or a packed
/// R,G,B triple). Deliberately a value type wrapping a single <see cref="uint"/> so
/// <c>TerminalCell</c> stays exactly 16 bytes.
/// </summary>
internal readonly struct CellColor : IEquatable<CellColor>
{
    private readonly uint _value;

    public static readonly CellColor Default = new(0);

    private CellColor(uint value) => _value = value;

    public static CellColor FromIndex(byte index) => new(((uint)ColorKind.Indexed << 24) | index);

    public static CellColor FromRgb(byte r, byte g, byte b) =>
        new(((uint)ColorKind.Rgb << 24) | ((uint)r << 16) | ((uint)g << 8) | b);

    public ColorKind Kind => (ColorKind)(_value >> 24);
    public byte Index => (byte)(_value & 0xFF);
    public byte R => (byte)((_value >> 16) & 0xFF);
    public byte G => (byte)((_value >> 8) & 0xFF);
    public byte B => (byte)(_value & 0xFF);

    public bool Equals(CellColor other) => _value == other._value;
    public override bool Equals(object? obj) => obj is CellColor other && Equals(other);
    public override int GetHashCode() => (int)_value;
    public static bool operator ==(CellColor a, CellColor b) => a.Equals(b);
    public static bool operator !=(CellColor a, CellColor b) => !a.Equals(b);
}

/// <summary>Per-cell rendering attributes.</summary>
[Flags]
internal enum CellFlags : ushort
{
    None = 0,
    Bold = 1 << 0,
    Dim = 1 << 1,
    Italic = 1 << 2,
    Underline = 1 << 3,
    DoubleUnderline = 1 << 4,
    Blink = 1 << 5,
    Reverse = 1 << 6,
    Invisible = 1 << 7,
    Strike = 1 << 8,
    Overline = 1 << 9,
    /// <summary>The left half of a 2-cell-wide character. The paired cell to the right is
    /// <see cref="WideTrail"/> and carries no rune of its own.</summary>
    WideLead = 1 << 10,
    /// <summary>The right half of a wide character - <see cref="TerminalCell.Rune"/> is
    /// meaningless here; the glyph lives on the paired <see cref="WideLead"/> cell.</summary>
    WideTrail = 1 << 11,
    /// <summary>This cell has one or more combining marks recorded in its row's
    /// <see cref="TerminalRow.Combining"/> dictionary.</summary>
    HasCombining = 1 << 12
}

/// <summary>
/// One screen cell. Exactly 16 bytes (4 + 2 + 2 + 4 + 4) - at the default 5000-line scrollback x
/// 120 cols x 10 tabs, cell storage alone is the dominant cost, so the layout is deliberately
/// tight rather than convenient.
/// </summary>
internal struct TerminalCell
{
    /// <summary>Full Unicode code point. 0 for a <see cref="CellFlags.WideTrail"/> cell (no rune
    /// of its own).</summary>
    public int Rune;
    public CellFlags Flags;
    /// <summary>OSC 8 hyperlink id, 0 = none. Added when hyperlinks are wired up in a later phase.</summary>
    public ushort LinkId;
    public CellColor Fg;
    public CellColor Bg;

    public static TerminalCell Blank(CellColor bg) => new() { Rune = ' ', Fg = CellColor.Default, Bg = bg };
}
