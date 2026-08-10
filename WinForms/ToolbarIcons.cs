using CoderCommander.Services;
using System.Collections.Concurrent;

namespace CoderCommander.WinForms;

/// <summary>
/// The app's icon set for toolbar buttons, function-key buttons, menu items and the drive bar.
/// Every icon is one SVG path string rendered by <see cref="VectorIcon"/> - no image assets, and
/// the colour comes from the live <see cref="ThemePalette"/> so icons follow a theme switch.
///
/// House rules for the set, so it reads as one family rather than 38 unrelated drawings:
/// <list type="bullet">
/// <item>16x16 grid, 1px stroke, outline style (VSCode/Fluent-like).</item>
/// <item>Axis-aligned strokes sit on half-pixel centres (2.5, 8.5, ...) so they land on exactly
/// one pixel row at 100% scaling. An icon drawn on integer coordinates renders as a 2px blur -
/// that, plus the anti-aliasing that used to be switched off entirely, is what made the previous
/// generation of these icons look fuzzy and ragged.</item>
/// <item>Content stays inside a 1.5..14.5 box, leaving a consistent margin.</item>
/// <item>Colour is a role (foreground / accent / danger), never a literal.</item>
/// </list>
/// </summary>
public static class ToolbarIcons
{
    private static readonly ConcurrentDictionary<string, Bitmap> _cache = new();
    private static int _lastDpi = 96;
    private static List<Bitmap>? _pendingDisposal;

    /// <summary>Which palette colour an icon is drawn in.</summary>
    private enum Tint { Foreground, Accent, Danger, Dim }

    /// <summary><c>Stroke</c> is the outline path; <c>Fill</c> is an optional solid path drawn
    /// underneath it (arrowheads, dots, indicator squares).</summary>
    private readonly record struct IconSpec(string Stroke, Tint Tint = Tint.Foreground,
                                            string? Fill = null, float Width = 1f);

    /// <summary>Gets a toolbar icon bitmap for the specified key, generating it if not cached.</summary>
    public static Image? Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        var currentDpi = GetCurrentDpi();
        if (currentDpi != _lastDpi)
        {
            ClearCache();
            _lastDpi = currentDpi;
        }

        return _cache.GetOrAdd(key, Draw);
    }

    /// <summary>
    /// Clears the icon cache (call on theme change). Bitmaps handed out by <see cref="Get"/> are
    /// assigned directly to long-lived controls (toolbar buttons, menu items) that don't necessarily
    /// re-fetch and reassign their Image right away, so disposing them on the spot risks an
    /// already-shown control drawing a disposed Bitmap. Keep this generation alive for one more
    /// clear instead - bounds the leak to at most one extra generation.
    /// </summary>
    public static void ClearCache()
    {
        _pendingDisposal?.ForEach(b => b.Dispose());
        _pendingDisposal = _cache.Values.ToList();
        _cache.Clear();
    }

    private static int GetCurrentDpi()
    {
        try
        {
            // Graphics.FromImage(new Bitmap(1,1)) always reports 96 - it reads the bitmap's own
            // resolution metadata, never the screen. FromHwnd(IntPtr.Zero) reads the desktop DPI.
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return (int)g.DpiX;
        }
        catch
        {
            return 96;
        }
    }

    private static Bitmap Draw(string key)
    {
        var p = ThemeService.Current;
        var scale = _lastDpi / 96.0f;
        var size = Math.Max(16, (int)Math.Round(VectorIcon.Grid * scale));

        var spec = Icons.TryGetValue(key, out var s) ? s : Generic;
        var color = spec.Tint switch
        {
            Tint.Accent => p.Accent,
            Tint.Danger => p.Danger,
            Tint.Dim => p.DimForeground,
            _ => p.HeaderForeground, // better contrast than Foreground on the light theme's toolbar
        };

        return VectorIcon.Render(spec.Stroke, size, color, spec.Width, spec.Fill);
    }

    private static readonly IconSpec Generic = new("M 2.5 2.5 H 13.5 V 13.5 H 2.5 Z");

    // Arrowheads are filled triangles rather than two stroked lines: at 16px a stroked chevron
    // loses its tip to anti-aliasing, a filled one stays legible.
    private const string ArrowLeftHead = "M 2.5 8 L 7 4.75 V 11.25 Z";
    private const string ArrowRightHead = "M 13.5 8 L 9 4.75 V 11.25 Z";
    private const string ArrowUpHead = "M 8 2.5 L 4.75 7 H 11.25 Z";
    private const string ArrowDownHead = "M 8 13.5 L 4.75 9 H 11.25 Z";

    /// <summary>A folder outline with a tab, shared by every folder-based icon.</summary>
    private const string Folder = "M 1.5 12.5 V 3.5 H 6.5 L 8 5.5 H 14.5 V 12.5 Z";

    /// <summary>A page outline with a folded corner, shared by the file-based icons.</summary>
    private const string Page = "M 3.5 1.5 H 9.5 L 12.5 4.5 V 14.5 H 3.5 Z M 9.5 1.5 V 4.5 H 12.5";

    private static readonly Dictionary<string, IconSpec> Icons = new(StringComparer.Ordinal)
    {
        // ── Navigation ──────────────────────────────────────────────────────────────────────
        ["back"] = new("M 13.5 8 H 4", Fill: ArrowLeftHead),
        ["forward"] = new("M 2.5 8 H 12", Fill: ArrowRightHead),
        ["up"] = new("M 8 13.5 V 4", Fill: ArrowUpHead),
        ["root"] = new("M 2.5 2.5 H 13.5 M 8 13.5 V 6", Fill: "M 8 4 L 4.75 8.5 H 11.25 Z"),
        ["home"] = new("M 2.5 7.25 L 8 2.5 L 13.5 7.25 M 4 6.5 V 13.5 H 12 V 6.5 M 6.5 13.5 V 9.5 H 9.5 V 13.5",
                        Tint.Accent),

        // ── File operations ─────────────────────────────────────────────────────────────────
        ["view"] = new("M 1.5 8 C 4 4.25 12 4.25 14.5 8 C 12 11.75 4 11.75 1.5 8 Z "
                        + VectorIcon.Circle(8, 8, 1.9f)),
        ["edit"] = new("M 10.5 2.5 L 13.5 5.5 L 5.5 13.5 L 2.5 13.5 L 2.5 10.5 Z M 9 4 L 12 7"),
        ["editnew"] = new(Page + " M 6 9.5 H 10 M 8 7.5 V 11.5", Tint.Accent),
        ["copy"] = new("M 5.5 1.5 H 11.5 L 14.5 4.5 V 10.5 H 5.5 Z M 10.5 5.5 H 1.5 V 14.5 H 10.5 Z"),
        // Deliberately not the same shape as "forward": the leading bar reads as "out of here,
        // to the other panel", so the F6 button can't be mistaken for the navigation arrow.
        ["move"] = new("M 2.5 4 V 12 M 4.5 8 H 10", Fill: ArrowRightHead),
        ["rename"] = new("M 1.5 4.5 H 14.5 V 11.5 H 1.5 Z M 4.5 3 V 13 M 3 3 H 6 M 3 13 H 6"),
        ["delete"] = new("M 2.5 4.5 H 13.5 M 6.5 4.5 V 2.5 H 9.5 V 4.5 "
                          + "M 4 4.5 L 4.75 13.5 H 11.25 L 12 4.5 M 6.75 7 V 11 M 9.25 7 V 11", Tint.Danger),
        ["wipe"] = new("M 2.5 4.5 H 13.5 M 6.5 4.5 V 2.5 H 9.5 V 4.5 M 4 4.5 L 4.75 13.5 H 11.25 L 12 4.5 "
                        + "M 5.5 6.5 L 10.5 11.5 M 10.5 6.5 L 5.5 11.5", Tint.Danger),
        ["newdir"] = new(Folder + " M 8 7.5 V 11.5 M 6 9.5 H 10", Tint.Accent),
        ["properties"] = new(Page + " M 6 7.5 H 10 M 6 9.5 H 10 M 6 11.5 H 8.5"),
        ["multirename"] = new("M 1.5 3.5 H 10.5 M 1.5 8 H 10.5 M 1.5 12.5 H 10.5 "
                               + "M 13 2.5 V 5 M 13 7 V 9.5 M 13 11.5 V 14", Tint.Accent),

        // ── Clipboard / history ─────────────────────────────────────────────────────────────
        ["cut"] = new(VectorIcon.Circle(4, 12, 2) + VectorIcon.Circle(12, 12, 2)
                       + " M 5.25 10.5 L 11 2.5 M 10.75 10.5 L 5 2.5"),
        ["paste"] = new("M 4.5 3.5 H 2.5 V 14.5 H 13.5 V 3.5 H 11.5 M 5.5 1.5 H 10.5 V 4.5 H 5.5 Z "
                         + "M 5 8 H 11 M 5 11 H 9"),
        ["undo"] = new("M 3.5 7.5 C 5.5 3.5 12 3.5 12.5 8.5 C 12.8 11.5 10.5 13 8 13",
                        Fill: "M 3.5 4 L 6.5 7.5 H 1 Z"),
        ["redo"] = new("M 12.5 7.5 C 10.5 3.5 4 3.5 3.5 8.5 C 3.2 11.5 5.5 13 8 13",
                        Fill: "M 12.5 4 L 15 7.5 H 9.5 Z"),
        ["refresh"] = new("M 13 8 C 13 10.76 10.76 13 8 13 C 5.24 13 3 10.76 3 8 C 3 5.24 5.24 3 8 3 "
                           + "C 9.9 3 11.55 4.06 12.4 5.62", Tint.Accent,
                           Fill: "M 13.5 7 L 9.75 6 L 12.25 3.25 Z"),

        // ── Selection ───────────────────────────────────────────────────────────────────────
        ["selectall"] = new("M 1.5 1.5 H 14.5 V 14.5 H 1.5 Z M 4.5 8.25 L 7 10.75 L 11.5 5.5", Tint.Accent),
        ["deselectall"] = new("M 1.5 1.5 H 14.5 V 14.5 H 1.5 Z M 5 8 H 11", Tint.Dim),
        ["invert"] = new("M 1.5 1.5 H 14.5 V 14.5 H 1.5 Z", Fill: "M 1.5 14.5 L 14.5 1.5 V 14.5 Z"),

        // ── Archives ────────────────────────────────────────────────────────────────────────
        ["pack"] = new("M 1.5 5.5 H 14.5 V 13.5 H 1.5 Z M 1.5 5.5 L 3.5 2.5 H 12.5 L 14.5 5.5 "
                        + "M 8 5.5 V 8.5", Fill: "M 8 11.5 L 5.75 8.75 H 10.25 Z"),
        ["extract"] = new("M 1.5 5.5 H 14.5 V 13.5 H 1.5 Z M 1.5 5.5 L 3.5 2.5 H 12.5 L 14.5 5.5 "
                           + "M 8 11.5 V 8.5", Fill: "M 8 5.5 L 5.75 8.25 H 10.25 Z"),

        // ── Tools ───────────────────────────────────────────────────────────────────────────
        ["search"] = new(VectorIcon.Circle(6.75f, 6.75f, 4.25f) + " M 9.9 9.9 L 13.5 13.5"),
        // Sliders rather than a cogwheel: a gear's teeth turn to mush at 16px, three tracks with
        // handles stay readable and match the outline style.
        ["settings"] = new("M 1.5 4.5 H 14.5 M 1.5 8 H 14.5 M 1.5 11.5 H 14.5",
                            Fill: "M 4.5 2.75 H 6.5 V 6.25 H 4.5 Z M 9.5 6.25 H 11.5 V 9.75 H 9.5 Z "
                                  + "M 5.5 9.75 H 7.5 V 13.25 H 5.5 Z"),
        ["bookmarks"] = new("M 4.5 1.5 H 11.5 V 14.5 L 8 11 L 4.5 14.5 Z", Tint.Accent),
        ["terminal"] = new("M 1.5 2.5 H 14.5 V 13.5 H 1.5 Z M 4 6 L 6.5 8.25 L 4 10.5 M 8.5 10.5 H 12"),
        ["syncdirs"] = new("M 3 5.5 H 10 M 13 10.5 H 6", Tint.Accent,
                            Fill: "M 13.5 5.5 L 10 3.25 V 7.75 Z M 2.5 10.5 L 6 8.25 V 12.75 Z"),
        ["exit"] = new("M 9.5 2.5 H 2.5 V 13.5 H 9.5 M 7 8 H 13", Fill: "M 14.5 8 L 11 5.5 V 10.5 Z"),

        // ── Drive bar ───────────────────────────────────────────────────────────────────────
        ["drive"] = new("M 1.5 4.5 H 14.5 V 11.5 H 1.5 Z", Tint.Accent, Fill: "M 11.5 7.25 H 13 V 8.75 H 11.5 Z"),
        ["drive_fixed"] = new("M 1.5 4.5 H 14.5 V 11.5 H 1.5 Z M 1.5 8 H 14.5", Tint.Accent,
                               Fill: "M 11.5 9.25 H 13 V 10.5 H 11.5 Z"),
        ["drive_removable"] = new("M 3.5 1.5 H 12.5 V 14.5 H 3.5 Z M 5.5 1.5 V 6.5 H 10.5 V 1.5", Tint.Accent,
                                   Fill: "M 8.25 2.5 H 9.75 V 5.5 H 8.25 Z"),
        ["drive_cdrom"] = new(VectorIcon.Circle(8, 8, 6.25f) + VectorIcon.Circle(8, 8, 1.75f), Tint.Accent),
        ["drive_network"] = new("M 2.5 3.5 H 13.5 V 8.5 H 2.5 Z M 8 8.5 V 11 M 4 14 H 12 M 4 11 H 12 V 14 H 4 Z",
                                 Tint.Accent),
        ["drive_ram"] = new("M 1.5 5.5 H 14.5 V 10.5 H 1.5 Z M 4 10.5 V 12.5 M 12 10.5 V 12.5", Tint.Accent,
                             Fill: "M 3.5 3.5 H 5.5 V 5.5 H 3.5 Z M 7 3.5 H 9 V 5.5 H 7 Z M 10.5 3.5 H 12.5 V 5.5 H 10.5 Z"),

        // ── Terminal panel ──────────────────────────────────────────────────────────────────
        ["plus"] = new("M 8 3.5 V 12.5 M 3.5 8 H 12.5", Tint.Accent, Width: 1.25f),

        // ── Remote connections ──────────────────────────────────────────────────────────────
        // A cloud: three overlapping arcs on the shared 16x16 grid. Reads at 16px where a more
        // literal "server rack" would collapse into stripes.
        ["connection"] = new("M 4.25 12.5 C 2.45 12.5 1.5 11.2 1.5 9.9 C 1.5 8.6 2.5 7.5 3.9 7.4 "
                              + "C 4.2 5.2 6 3.5 8.2 3.5 C 10.4 3.5 12.2 5.1 12.5 7.2 "
                              + "C 13.7 7.5 14.5 8.5 14.5 9.8 C 14.5 11.3 13.4 12.5 11.9 12.5 Z",
                              Tint.Accent),
    };
}
