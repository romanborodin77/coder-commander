using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// The application mark: <c>{ / }</c> — a pair of curly braces around a forward slash, set in a
/// rounded accent badge. Braces for "written by and for people who write code", the slash for a
/// path separator, which is what a file manager is actually about.
///
/// Drawn as vector paths through <see cref="VectorIcon"/> rather than shipped as a bitmap, for
/// the same reasons the icon set is: it stays sharp at any size and DPI (the About dialog draws
/// it far larger than any icon), it needs no binary asset in the repo, and its colours come from
/// the live <see cref="ThemePalette"/> so it follows a theme switch instead of being baked in.
/// </summary>
public static class AppLogo
{
    /// <summary>The logo's own coordinate system. Everything below is expressed on this grid and
    /// scaled at render time.</summary>
    private const float Grid = 64f;

    // Two mirrored braces around a slash. Each brace is drawn as four cubic segments: out from
    // the terminal, straight down, pinched toward the centre at the waist, and back out - the
    // shape a typographic brace actually has, which a pair of arcs doesn't capture.
    private const string LeftBrace =
        "M 25 15 C 21 15 21 18.5 21 22 C 21 26.5 20.5 28.5 16 32 " +
        "C 20.5 35.5 21 37.5 21 42 C 21 45.5 21 49 25 49";

    private const string RightBrace =
        "M 39 15 C 43 15 43 18.5 43 22 C 43 26.5 43.5 28.5 48 32 " +
        "C 43.5 35.5 43 37.5 43 42 C 43 45.5 43 49 39 49";

    private const string Slash = "M 36 19 L 28 45";

    private static string Glyphs => $"{LeftBrace} {RightBrace} {Slash}";

    /// <summary>The badge-less mark, stroked in <paramref name="color"/> - for places that
    /// already have their own surface (a header, a watermark).</summary>
    public static Bitmap RenderGlyph(int pixelSize, Color color, float strokeWidth = 3.2f)
        => VectorIcon.RenderOn(Grid, Glyphs, pixelSize, color, strokeWidth);

    /// <summary>The full mark: accent badge with the glyphs knocked out in the palette's
    /// on-accent foreground.</summary>
    public static Bitmap Render(int pixelSize)
    {
        var p = ThemeService.Current;
        var badge = VectorIcon.RoundedRect(2, 2, 60, 60, 14);
        // Stroke weight is in grid units, so it scales with the badge and the mark keeps the
        // same proportions whether it's drawn at 32px or 256px.
        return VectorIcon.RenderOn(Grid, Glyphs, pixelSize, p.SelectionForeground, 3.4f,
                                    fillData: badge, fillColor: p.Accent);
    }
}
