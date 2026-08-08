using CoderCommander.Services;
using CoderCommander.Terminal.Screen;
using CoderCommander.Terminal.Vt;

namespace CoderCommander.Terminal.Ui;

/// <summary>
/// Resolves a cell's packed <see cref="CellColor"/> to a real <see cref="Color"/> against a given
/// <see cref="TerminalPalette"/> - the 16-entry ANSI table for <see cref="ColorKind.Indexed"/>
/// 0-15, <see cref="Xterm256"/>'s fixed cube/ramp math for 16-255, direct RGB for
/// <see cref="ColorKind.Rgb"/>, and the palette's default fg/bg for <see cref="ColorKind.Default"/>.
/// Rebuilt (cheaply - 16 Color values) whenever the active theme changes.
/// </summary>
internal sealed class TerminalColorCache
{
    private readonly TerminalPalette _palette;
    private readonly Color[] _ansi16;

    public TerminalColorCache(TerminalPalette palette)
    {
        _palette = palette;
        _ansi16 =
        [
            palette.Black, palette.Red, palette.Green, palette.Yellow,
            palette.Blue, palette.Magenta, palette.Cyan, palette.White,
            palette.BrightBlack, palette.BrightRed, palette.BrightGreen, palette.BrightYellow,
            palette.BrightBlue, palette.BrightMagenta, palette.BrightCyan, palette.BrightWhite,
        ];
    }

    public Color Foreground(CellColor c) => c.Kind == ColorKind.Default ? _palette.DefaultForeground : Resolve(c);
    public Color Background(CellColor c) => c.Kind == ColorKind.Default ? _palette.DefaultBackground : Resolve(c);

    private Color Resolve(CellColor c) => c.Kind switch
    {
        ColorKind.Indexed => Xterm256.Resolve(c.Index, _ansi16),
        ColorKind.Rgb => Color.FromArgb(c.R, c.G, c.B),
        _ => _palette.DefaultForeground
    };
}
