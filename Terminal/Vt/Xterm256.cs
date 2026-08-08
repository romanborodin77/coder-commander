using System.Drawing;

namespace CoderCommander.Terminal.Vt;

/// <summary>
/// Resolves an xterm 256-color palette index to an RGB color. Indices 0-15 are the base ANSI
/// palette - theme-dependent, supplied by the caller rather than hardcoded here, since those ARE
/// theme colors and belong in <c>ThemePalette.Terminal</c> (wired up in a later phase). Indices
/// 16-231 are a 6x6x6 RGB color cube and 232-255 a 24-step grayscale ramp; both of THOSE are
/// fixed constants defined by the xterm 256-color extension itself, not theme colors, which is
/// why they're computed here instead of stored anywhere.
/// </summary>
internal static class Xterm256
{
    private static readonly int[] CubeLevels = [0, 95, 135, 175, 215, 255];

    /// <param name="index">0-255.</param>
    /// <param name="ansi16">The 16 base ANSI colors (index 0 = black .. 15 = bright white),
    /// theme-supplied.</param>
    public static Color Resolve(int index, IReadOnlyList<Color> ansi16)
    {
        if (index is >= 0 and <= 15)
            return ansi16[index];

        if (index is >= 16 and <= 231)
        {
            var i = index - 16;
            var r = CubeLevels[i / 36];
            var g = CubeLevels[i / 6 % 6];
            var b = CubeLevels[i % 6];
            return Color.FromArgb(r, g, b);
        }

        if (index is >= 232 and <= 255)
        {
            var gray = 8 + (index - 232) * 10;
            return Color.FromArgb(gray, gray, gray);
        }

        // Out-of-range index (a malformed/attacker-controlled SGR parameter) - fall back to a
        // neutral gray rather than throwing or indexing out of bounds.
        return Color.FromArgb(128, 128, 128);
    }
}
