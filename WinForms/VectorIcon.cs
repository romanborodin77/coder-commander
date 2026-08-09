using System.Drawing.Drawing2D;
using System.Globalization;

namespace CoderCommander.WinForms;

/// <summary>
/// Renders icons written as SVG path data onto a bitmap, with real anti-aliasing.
///
/// Why not an SVG library: a full renderer (SkiaSharp/Svg.Skia) is a large native dependency for
/// what icons here actually need - single-colour outline strokes on a fixed grid. Path data is
/// still the right *notation* though: one compact string per icon instead of a dozen imperative
/// DrawLine/DrawRectangle calls, so the whole set can be read, diffed, and kept visually
/// consistent, and shapes can be lifted from any icon font or SVG file.
///
/// Crispness rules baked in here, which the previous hand-drawn icons broke:
/// <list type="bullet">
/// <item><see cref="SmoothingMode.AntiAlias"/>, not <c>None</c> - the old setting is why every
/// diagonal and arc (the delete X, the refresh arc, the magnifier) came out with visible
/// stair-stepping.</item>
/// <item>A single 16x16 design grid with axis-aligned strokes on half-pixel centres (x.5), so a
/// 1px stroke covers exactly one pixel column/row at 100% scaling instead of straddling two and
/// rendering as two half-intensity rows.</item>
/// <item>Scaling happens by rendering the vector geometry at the target pixel size, never by
/// stretching a 16px bitmap - so a 20px or 24px icon on a high-DPI monitor is genuinely sharp.</item>
/// </list>
/// </summary>
internal static class VectorIcon
{
    /// <summary>Side of the coordinate system every icon in <see cref="ToolbarIcons"/> is drawn on.</summary>
    public const float Grid = 16f;

    /// <summary>Renders <paramref name="pathData"/> stroked in <paramref name="color"/>.
    /// <paramref name="fillData"/>, when given, is filled first (for solid accents like a dot or
    /// an arrowhead) - keeping fills in their own path avoids the classic mistake of stroking a
    /// shape that was meant to be solid.</summary>
    public static Bitmap Render(string pathData, int pixelSize, Color color,
                                float strokeWidth = 1f, string? fillData = null,
                                Color? fillColor = null)
    {
        var bmp = new Bitmap(pixelSize, pixelSize);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);

        var scale = pixelSize / Grid;
        g.ScaleTransform(scale, scale);

        if (!string.IsNullOrEmpty(fillData))
        {
            using var fillPath = Parse(fillData);
            using var brush = new SolidBrush(fillColor ?? color);
            g.FillPath(brush, fillPath);
        }

        if (!string.IsNullOrEmpty(pathData))
        {
            using var path = Parse(pathData);
            // Pen width is in pre-transform units, so it scales with the icon - one declared
            // stroke weight stays visually identical at every size.
            using var pen = new Pen(color, strokeWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            g.DrawPath(pen, path);
        }

        return bmp;
    }

    /// <summary>Circle as path data - four cubic segments, the standard 0.5523 kappa
    /// approximation. Saves every rounded icon from spelling the same beziers out by hand.</summary>
    public static string Circle(float cx, float cy, float r)
    {
        var k = r * 0.5523f;
        var ci = CultureInfo.InvariantCulture;
        string N(float v) => v.ToString("0.###", ci);
        return $"M {N(cx - r)} {N(cy)} " +
               $"C {N(cx - r)} {N(cy - k)} {N(cx - k)} {N(cy - r)} {N(cx)} {N(cy - r)} " +
               $"C {N(cx + k)} {N(cy - r)} {N(cx + r)} {N(cy - k)} {N(cx + r)} {N(cy)} " +
               $"C {N(cx + r)} {N(cy + k)} {N(cx + k)} {N(cy + r)} {N(cx)} {N(cy + r)} " +
               $"C {N(cx - k)} {N(cy + r)} {N(cx - r)} {N(cy + k)} {N(cx - r)} {N(cy)} Z";
    }

    // ── SVG path-data parsing ───────────────────────────────────────────────────────────────

    /// <summary>Parses the subset of SVG path syntax the icon set uses: M/L/H/V/C/S/Q/T/Z, in
    /// both absolute and relative (lowercase) form. Elliptical arcs (A) are deliberately not
    /// supported - every curve here is a bezier, and <see cref="Circle"/> covers the one case
    /// that would otherwise want an arc.</summary>
    public static GraphicsPath Parse(string d)
    {
        var path = new GraphicsPath();
        var tokens = Tokenize(d);
        int i = 0;

        PointF current = PointF.Empty, start = PointF.Empty, lastControl = PointF.Empty;
        char command = '\0', previous = '\0';
        bool figureOpen = false;

        float Next() => i < tokens.Count ? tokens[i++].Number : 0f;

        while (i < tokens.Count)
        {
            if (tokens[i].IsCommand)
            {
                command = tokens[i].Command;
                i++;
            }
            else if (command == '\0')
            {
                break; // numbers before any command - malformed, stop rather than guess
            }
            else if (command is 'M') command = 'L';   // implicit lineto after a moveto
            else if (command is 'm') command = 'l';

            bool relative = char.IsLower(command);
            char op = char.ToUpperInvariant(command);
            float ox = relative ? current.X : 0f;
            float oy = relative ? current.Y : 0f;

            switch (op)
            {
                case 'M':
                {
                    current = new PointF(Next() + ox, Next() + oy);
                    start = current;
                    path.StartFigure();
                    figureOpen = true;
                    break;
                }
                case 'L':
                {
                    var to = new PointF(Next() + ox, Next() + oy);
                    path.AddLine(current, to);
                    current = to;
                    break;
                }
                case 'H':
                {
                    var to = new PointF(Next() + ox, current.Y);
                    path.AddLine(current, to);
                    current = to;
                    break;
                }
                case 'V':
                {
                    var to = new PointF(current.X, Next() + oy);
                    path.AddLine(current, to);
                    current = to;
                    break;
                }
                case 'C':
                {
                    var c1 = new PointF(Next() + ox, Next() + oy);
                    var c2 = new PointF(Next() + ox, Next() + oy);
                    var to = new PointF(Next() + ox, Next() + oy);
                    path.AddBezier(current, c1, c2, to);
                    lastControl = c2;
                    current = to;
                    break;
                }
                case 'S':
                {
                    var c1 = previous is 'C' or 'c' or 'S' or 's'
                        ? new PointF(2 * current.X - lastControl.X, 2 * current.Y - lastControl.Y)
                        : current;
                    var c2 = new PointF(Next() + ox, Next() + oy);
                    var to = new PointF(Next() + ox, Next() + oy);
                    path.AddBezier(current, c1, c2, to);
                    lastControl = c2;
                    current = to;
                    break;
                }
                case 'Q':
                {
                    var q = new PointF(Next() + ox, Next() + oy);
                    var to = new PointF(Next() + ox, Next() + oy);
                    AddQuadratic(path, current, q, to);
                    lastControl = q;
                    current = to;
                    break;
                }
                case 'T':
                {
                    var q = previous is 'Q' or 'q' or 'T' or 't'
                        ? new PointF(2 * current.X - lastControl.X, 2 * current.Y - lastControl.Y)
                        : current;
                    var to = new PointF(Next() + ox, Next() + oy);
                    AddQuadratic(path, current, q, to);
                    lastControl = q;
                    current = to;
                    break;
                }
                case 'Z':
                {
                    if (figureOpen)
                    {
                        path.CloseFigure();
                        figureOpen = false;
                    }
                    current = start;
                    break;
                }
                default:
                    i = tokens.Count; // unknown command - bail out instead of looping forever
                    break;
            }

            previous = command;
        }

        return path;
    }

    /// <summary>GDI+ has no quadratic bezier, so raise it to the equivalent cubic.</summary>
    private static void AddQuadratic(GraphicsPath path, PointF from, PointF ctrl, PointF to)
    {
        var c1 = new PointF(from.X + 2f / 3f * (ctrl.X - from.X), from.Y + 2f / 3f * (ctrl.Y - from.Y));
        var c2 = new PointF(to.X + 2f / 3f * (ctrl.X - to.X), to.Y + 2f / 3f * (ctrl.Y - to.Y));
        path.AddBezier(from, c1, c2, to);
    }

    private readonly record struct Token(bool IsCommand, char Command, float Number);

    private static List<Token> Tokenize(string d)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < d.Length)
        {
            char c = d[i];
            if (char.IsWhiteSpace(c) || c == ',')
            {
                i++;
            }
            else if (char.IsLetter(c))
            {
                tokens.Add(new Token(true, c, 0f));
                i++;
            }
            else
            {
                int startIdx = i;
                if (c is '-' or '+') i++;
                while (i < d.Length && (char.IsDigit(d[i]) || d[i] == '.')) i++;
                if (i < d.Length && (d[i] is 'e' or 'E'))
                {
                    i++;
                    if (i < d.Length && (d[i] is '-' or '+')) i++;
                    while (i < d.Length && char.IsDigit(d[i])) i++;
                }
                if (i == startIdx)
                {
                    i++; // unrecognised character - skip so we can't spin here
                    continue;
                }
                var span = d.AsSpan(startIdx, i - startIdx);
                tokens.Add(new Token(false, '\0',
                    float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f));
            }
        }
        return tokens;
    }
}
