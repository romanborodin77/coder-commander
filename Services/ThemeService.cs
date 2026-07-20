using System.Drawing.Drawing2D;

namespace CoderCommander.Services;

/// <summary>
/// Syntax-highlight token colors used by <see cref="WinForms.CodeEditorCanvas"/>. Split out of
/// <see cref="ThemePalette"/> so Dark/Light can each supply a full set without a wall of
/// individual color properties on the palette itself.
/// </summary>
public sealed class SyntaxPalette
{
    public Color Comment { get; init; }
    public Color Number { get; init; }
    public Color Function { get; init; }
    public Color Attribute { get; init; }
    public Color TagName { get; init; }
    public Color TagAttribute { get; init; }
    public Color Selector { get; init; }
    public Color JsonKey { get; init; }
    public Color SqlFunction { get; init; }
}

/// <summary>
/// Process-lifetime cache of <see cref="Font"/> instances keyed by (family, size, style).
/// Dark and Light palettes share the same font metrics, so caching here means neither
/// palette exclusively owns a Font it would need to dispose. Previously each theme switch
/// created and eventually disposed a fresh set of fonts, which risked GDI+ exceptions if a
/// control was still mid-repaint with a disposed font from two switches ago - the cache
/// removes that risk entirely since fonts now live for the whole process.
/// </summary>
internal static class FontCache
{
    private static readonly Dictionary<(string Name, float Size, FontStyle Style), Font> Cache = new();

    public static Font Get(string name, float size, FontStyle style = FontStyle.Regular)
    {
        var key = (name, size, style);
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var existing))
                return existing;

            var font = CreateFont(name, size, style);
            Cache[key] = font;
            return font;
        }
    }

    private static Font CreateFont(string name, float size, FontStyle style)
    {
        try
        {
            using var test = new Font(name, size, style);
            if (test.Name == name)
                return new Font(name, size, style);
        }
        catch (Exception ex)
        {
            LogService.Warning($"Font '{name}' not available: {ex.Message}");
        }
        return new Font("Segoe UI", size, style);
    }
}

/// <summary>
/// VSCode-style theme palette (Dark+ and Light+ color schemes).
/// </summary>
public sealed class ThemePalette
{
    // Backgrounds — VSCode Dark+ (editor: #1e1e1e, sidebar: #252526)
    public Color Background { get; init; } = Color.FromArgb(30, 30, 30);
    public Color PanelBackground { get; init; } = Color.FromArgb(37, 37, 38);
    public Color PanelActiveBorder { get; init; } = Color.FromArgb(0, 127, 212);
    public Color PanelInactiveBorder { get; init; } = Color.FromArgb(60, 60, 60);

    // Text — VSCode editor.foreground
    public Color Foreground { get; init; } = Color.FromArgb(212, 212, 212);
    public Color DimForeground { get; init; } = Color.FromArgb(136, 136, 136);

    // Selection — VSCode list colors
    public Color Selection { get; init; } = Color.FromArgb(9, 71, 113);
    public Color SelectionForeground { get; init; } = Color.White;
    public Color InactiveSelection { get; init; } = Color.FromArgb(55, 55, 61);

    // Headers / toolbars — VSCode titleBar/activityBar
    public Color HeaderBackground { get; init; } = Color.FromArgb(60, 60, 60);
    public Color HeaderForeground { get; init; } = Color.FromArgb(204, 204, 204);
    public Color ToolbarBackground { get; init; } = Color.FromArgb(51, 51, 51);
    // Must read lighter than ToolbarBackground, or hovering a toolbar button visually
    // dims it instead of highlighting it.
    public Color ToolbarHover { get; init; } = Color.FromArgb(70, 73, 74);

    // Grid — VSCode editorWidget.background
    public Color GridLine { get; init; } = Color.FromArgb(64, 64, 64);
    public Color AlternatingRow { get; init; } = Color.FromArgb(34, 34, 34);

    // Accent — VSCode focusBorder / button.background
    public Color Accent { get; init; } = Color.FromArgb(14, 99, 156);
    public Color AccentHover { get; init; } = Color.FromArgb(17, 119, 187);

    // File type colors — VSCode syntax highlighting
    public Color DirectoryColor { get; init; } = Color.FromArgb(78, 201, 176);
    public Color ExecutableColor { get; init; } = Color.FromArgb(197, 134, 192);
    public Color HiddenColor { get; init; } = Color.FromArgb(128, 128, 128);
    public Color ArchiveColor { get; init; } = Color.FromArgb(206, 145, 120);

    // Danger — VSCode errorForeground
    public Color Danger { get; init; } = Color.FromArgb(244, 71, 71);

    // Warning — used by StyledMessageBox and any warning iconography
    public Color Warning { get; init; } = Color.FromArgb(255, 180, 0);

    // Gloss overlay for the subtle highlight sheen on buttons/checkboxes. White in the
    // dark theme, black in the light theme so the "light hits the top edge" effect still
    // reads correctly instead of washing out a light background.
    public Color GlossOverlay { get; init; } = Color.FromArgb(30, 255, 255, 255);

    // Interactive — hover, focus, splitter
    public Color RowHover { get; init; } = Color.FromArgb(44, 48, 50);
    public Color SplitterNormal { get; init; } = Color.FromArgb(64, 64, 64);
    public Color SplitterHover { get; init; } = Color.FromArgb(14, 99, 156);
    public Color FocusBorder { get; init; } = Color.FromArgb(14, 99, 156);
    public Color ColumnHeaderGradient { get; init; } = Color.FromArgb(68, 68, 68);

    // Scrollbar — VSCode Dark+ (semi-transparent simulation over #1e1e1e)
    public Color ScrollbarTrack { get; init; } = Color.FromArgb(30, 30, 30);
    public Color ScrollbarThumb { get; init; } = Color.FromArgb(74, 74, 74);
    public Color ScrollbarThumbHover { get; init; } = Color.FromArgb(102, 102, 102);
    public Color ScrollbarThumbPressed { get; init; } = Color.FromArgb(135, 135, 135);
    public Color ScrollbarArrow { get; init; } = Color.FromArgb(135, 135, 135);
    public Color ScrollbarArrowHover { get; init; } = Color.FromArgb(180, 180, 180);
    public Color ScrollbarBorder { get; init; } = Color.FromArgb(30, 30, 30);

    // Syntax highlighting (CodeEditorCanvas) — the only palette member not a flat Color.
    public SyntaxPalette Syntax { get; init; } = new()
    {
        Comment = Color.FromArgb(106, 153, 85),
        Number = Color.FromArgb(181, 206, 168),
        Function = Color.FromArgb(220, 220, 170),
        Attribute = Color.FromArgb(156, 220, 254),
        TagName = Color.FromArgb(86, 156, 214),
        TagAttribute = Color.FromArgb(156, 220, 254),
        Selector = Color.FromArgb(215, 186, 125),
        JsonKey = Color.FromArgb(156, 220, 254),
        SqlFunction = Color.FromArgb(220, 220, 170)
    };

    // Fonts — VSCode uses Segoe UI (Windows), Consolas for monospace. Sourced from the
    // process-lifetime FontCache: Dark and Light share the same metrics, so no palette
    // instance exclusively owns a Font it would need to dispose (see ApplyTheme).
    public Font GridFont { get; init; } = FontCache.Get("Segoe UI", 9F);
    public Font GridFontBold { get; init; } = FontCache.Get("Segoe UI", 9F, FontStyle.Bold);
    public Font HeaderFont { get; init; } = FontCache.Get("Segoe UI", 9F, FontStyle.Bold);
    public Font StatusBarFont { get; init; } = FontCache.Get("Segoe UI", 8.5F);
    public Font CaptionFont { get; init; } = FontCache.Get("Segoe UI", 9F);
    public Font MonoFont { get; init; } = FontCache.Get("Consolas", 9.5F);

    // Typography roles for dialog chrome — titles, section headers, hint text, glyph
    // icons — so individual dialogs no longer construct their own ad-hoc `new Font(...)`.
    public Font TitleFont { get; init; } = FontCache.Get("Segoe UI", 15F, FontStyle.Bold);
    public Font SubtitleFont { get; init; } = FontCache.Get("Segoe UI", 13F, FontStyle.Bold);
    public Font SectionFont { get; init; } = FontCache.Get("Segoe UI", 10F, FontStyle.Bold);
    public Font SmallFont { get; init; } = FontCache.Get("Segoe UI", 8.5F);
    public Font ItalicFont { get; init; } = FontCache.Get("Segoe UI", 9F, FontStyle.Italic);
    public Font IconGlyphFont { get; init; } = FontCache.Get("Segoe UI", 24F);

    /// <summary>Bold glyph on a small square button (e.g. <c>EmbeddedTerminalPanel</c>'s "+" new-tab
    /// button) - used to be a bare <c>new Font(...)</c> built once at construction, so it never
    /// rebuilt on a theme switch and was never disposed.</summary>
    public Font ButtonGlyphFont { get; init; } = FontCache.Get("Segoe UI", 12F, FontStyle.Bold);

    /// <summary>Underlined link text (e.g. <c>AboutForm</c>'s GitHub/license links) - same
    /// FontCache-vs-bare-<c>new Font</c> reasoning as <see cref="ButtonGlyphFont"/>.</summary>
    public Font LinkFont { get; init; } = FontCache.Get("Segoe UI", 9F, FontStyle.Underline);
}

/// <summary>
/// Applies theme colors to WinForms controls.
/// </summary>
public static class ThemeService
{
    public static ThemePalette Current { get; private set; } = CreateDark();

    public static ThemePalette CreateDark() => new();

    public static ThemePalette CreateLight() => new()
    {
        // VSCode Light+ colors
        Background = Color.FromArgb(255, 255, 255),
        PanelBackground = Color.FromArgb(243, 243, 243),
        PanelActiveBorder = Color.FromArgb(0, 122, 204),
        PanelInactiveBorder = Color.FromArgb(200, 200, 200),
        Foreground = Color.FromArgb(60, 60, 60),
        DimForeground = Color.FromArgb(128, 128, 128),
        Selection = Color.FromArgb(0, 96, 192),
        SelectionForeground = Color.White,
        InactiveSelection = Color.FromArgb(228, 230, 241),
        HeaderBackground = Color.FromArgb(221, 221, 221),
        HeaderForeground = Color.FromArgb(30, 30, 30),
        ToolbarBackground = Color.FromArgb(237, 237, 237),
        ToolbarHover = Color.FromArgb(232, 232, 232),
        GridLine = Color.FromArgb(220, 220, 220),
        AlternatingRow = Color.FromArgb(248, 248, 248),
        Accent = Color.FromArgb(0, 122, 204),
        AccentHover = Color.FromArgb(2, 110, 193),
        DirectoryColor = Color.FromArgb(0, 110, 180),
        ExecutableColor = Color.FromArgb(130, 80, 160),
        HiddenColor = Color.FromArgb(128, 128, 128),
        ArchiveColor = Color.FromArgb(180, 100, 40),
        Danger = Color.FromArgb(205, 49, 49),
        Warning = Color.FromArgb(255, 180, 0),
        GlossOverlay = Color.FromArgb(30, 0, 0, 0), // Black in light theme
        RowHover = Color.FromArgb(232, 232, 232),
        SplitterNormal = Color.FromArgb(200, 200, 200),
        SplitterHover = Color.FromArgb(0, 122, 204),
        FocusBorder = Color.FromArgb(0, 122, 204),
        ColumnHeaderGradient = Color.FromArgb(210, 210, 210),
        ScrollbarTrack = Color.FromArgb(243, 243, 243),
        ScrollbarThumb = Color.FromArgb(193, 193, 193),
        ScrollbarThumbHover = Color.FromArgb(168, 168, 168),
        ScrollbarThumbPressed = Color.FromArgb(136, 136, 136),
        ScrollbarArrow = Color.FromArgb(136, 136, 136),
        ScrollbarArrowHover = Color.FromArgb(90, 90, 90),
        ScrollbarBorder = Color.FromArgb(243, 243, 243),
        Syntax = new()
        {
            Comment = Color.FromArgb(106, 153, 85),
            Number = Color.FromArgb(100, 140, 60),
            Function = Color.FromArgb(150, 130, 0),
            Attribute = Color.FromArgb(0, 100, 150),
            TagName = Color.FromArgb(0, 110, 180),
            TagAttribute = Color.FromArgb(0, 100, 150),
            Selector = Color.FromArgb(180, 100, 0),
            JsonKey = Color.FromArgb(0, 100, 150),
            SqlFunction = Color.FromArgb(150, 130, 0)
        }
    };

    public static event EventHandler? ThemeChanged;

    /// <summary>
    /// Whether the active palette is a dark theme, by background luminance. Single source of
    /// truth for the "is this dark?" check that used to be duplicated at every dark-titlebar/
    /// dark-scrollbar call site.
    /// </summary>
    public static bool IsDark => Current.Background.GetBrightness() < 0.5f;

    public static void ApplyTheme(string themeName)
    {
        Current = themeName == "Light" ? CreateLight() : CreateDark();
        WinForms.ToolbarIcons.ClearCache();
        WinForms.FileIcons.ClearCache();
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void StyleForm(Form form)
    {
        var p = Current;
        form.BackColor = p.Background;
        form.ForeColor = p.Foreground;
        form.Font = p.GridFont;
    }

    public static void StyleToolStrip(ToolStrip ts)
    {
        var p = Current;
        ts.BackColor = p.ToolbarBackground;
        ts.ForeColor = p.HeaderForeground;
        ts.Renderer = new ThemeRenderer();
    }

    public static void StyleMenu(MenuStrip ms)
    {
        var p = Current;
        ms.BackColor = p.ToolbarBackground;
        ms.ForeColor = p.HeaderForeground;
        ms.Renderer = new ThemeRenderer();
        ms.Padding = new Padding(2, 3, 0, 3);
    }

    public static void StyleStatusStrip(StatusStrip ss)
    {
        var p = Current;
        ss.BackColor = p.HeaderBackground;
        ss.ForeColor = p.DimForeground;
        ss.SizingGrip = false;
        ss.Padding = new Padding(8, 2, 8, 2);
        ss.Renderer = new ThemeRenderer();
    }

    /// <summary>
    /// Dims a color by the given percentage (100 = unchanged, 70 = darker).
    /// </summary>
    public static Color DimColor(Color color, int brightness = 80)
    {
        if (brightness >= 100) return color;
        brightness = Math.Max(0, Math.Min(100, brightness));
        var factor = brightness / 100.0;
        return Color.FromArgb(color.A,
            (int)(color.R * factor),
            (int)(color.G * factor),
            (int)(color.B * factor));
    }

    /// <summary>
    /// Blends two colors by the given ratio (0 = color1, 1 = color2).
    /// </summary>
    public static Color BlendColors(Color color1, Color color2, float ratio)
    {
        ratio = Math.Clamp(ratio, 0f, 1f);
        return Color.FromArgb(
            (int)(color1.A + (color2.A - color1.A) * ratio),
            (int)(color1.R + (color2.R - color1.R) * ratio),
            (int)(color1.G + (color2.G - color1.G) * ratio),
            (int)(color1.B + (color2.B - color1.B) * ratio));
    }
}

/// <summary>
/// ProfessionalColorTable driven by the active ThemePalette.
/// Provides dark-theme-aware colors for menu dropdowns, toolstrip buttons, separators.
/// </summary>
public sealed class ThemeColorTable : ProfessionalColorTable
{
    public ThemeColorTable() : base() { }

    private static ThemePalette P => ThemeService.Current;

    public override Color MenuStripGradientBegin => P.ToolbarBackground;
    public override Color MenuStripGradientEnd => P.ToolbarBackground;
    public override Color MenuBorder => P.GridLine;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemPressedGradientBegin => P.ToolbarBackground;
    public override Color MenuItemPressedGradientMiddle => P.ToolbarBackground;
    public override Color MenuItemPressedGradientEnd => P.ToolbarBackground;
    public override Color MenuItemSelected => P.ToolbarHover;
    public override Color MenuItemSelectedGradientBegin => P.ToolbarHover;
    public override Color MenuItemSelectedGradientEnd => P.ToolbarHover;
    public override Color ImageMarginGradientBegin => P.PanelBackground;
    public override Color ImageMarginGradientMiddle => P.PanelBackground;
    public override Color ImageMarginGradientEnd => P.PanelBackground;
    public override Color ToolStripGradientBegin => P.ToolbarBackground;
    public override Color ToolStripGradientMiddle => P.ToolbarBackground;
    public override Color ToolStripGradientEnd => P.ToolbarBackground;
    public override Color ToolStripBorder => P.ToolbarBackground;
    public override Color ToolStripDropDownBackground => P.PanelBackground;
    public override Color ToolStripContentPanelGradientBegin => P.Background;
    public override Color ToolStripContentPanelGradientEnd => P.Background;
    public override Color ToolStripPanelGradientBegin => P.ToolbarBackground;
    public override Color ToolStripPanelGradientEnd => P.ToolbarBackground;
    public override Color ButtonSelectedBorder => P.Accent;
    public override Color ButtonSelectedGradientBegin => P.ToolbarHover;
    public override Color ButtonSelectedGradientMiddle => P.ToolbarHover;
    public override Color ButtonSelectedGradientEnd => P.ToolbarHover;
    public override Color ButtonPressedBorder => P.Accent;
    public override Color ButtonPressedGradientBegin => P.ToolbarHover;
    public override Color ButtonPressedGradientMiddle => P.ToolbarHover;
    public override Color ButtonPressedGradientEnd => P.ToolbarHover;
    public override Color ButtonCheckedGradientBegin => P.ToolbarHover;
    public override Color ButtonCheckedGradientMiddle => P.ToolbarHover;
    public override Color ButtonCheckedGradientEnd => P.ToolbarHover;
    public override Color ButtonCheckedHighlight => P.ToolbarHover;
    public override Color ButtonCheckedHighlightBorder => P.Accent;
    public override Color SeparatorDark => P.GridLine;
    public override Color SeparatorLight => P.GridLine;
    public override Color CheckBackground => P.Accent;
    public override Color CheckPressedBackground => P.AccentHover;
    public override Color CheckSelectedBackground => P.AccentHover;
    public override Color OverflowButtonGradientBegin => P.ToolbarBackground;
    public override Color OverflowButtonGradientMiddle => P.ToolbarBackground;
    public override Color OverflowButtonGradientEnd => P.ToolbarBackground;
    public override Color StatusStripGradientBegin => P.HeaderBackground;
    public override Color StatusStripGradientEnd => P.HeaderBackground;
}

/// <summary>
/// Custom ToolStripProfessionalRenderer that applies theme colors to backgrounds
/// and provides flat hover/selected states for toolbar buttons (VSCode style).
/// </summary>
public sealed class ThemeRenderer : ToolStripProfessionalRenderer
{
    public ThemeRenderer() : base(new ThemeColorTable()) { }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        var p = ThemeService.Current;
        if (e.ToolStrip is StatusStrip)
        {
            using var brush = new SolidBrush(p.HeaderBackground);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
            using var topLine = new Pen(Color.FromArgb(40, p.GridLine), 1f);
            e.Graphics.DrawLine(topLine, e.AffectedBounds.X, e.AffectedBounds.Y, e.AffectedBounds.Right, e.AffectedBounds.Y);
            return;
        }
        using (var bg = new SolidBrush(p.ToolbarBackground))
            e.Graphics.FillRectangle(bg, e.AffectedBounds);
        if (e.ToolStrip.Dock == DockStyle.Bottom)
        {
            using var topSep = new Pen(Color.FromArgb(40, p.GridLine), 1f);
            e.Graphics.DrawLine(topSep, e.AffectedBounds.X, e.AffectedBounds.Y, e.AffectedBounds.Right, e.AffectedBounds.Y);
        }
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var p = ThemeService.Current;
        var item = e.Item;
        var rect = new Rectangle(1, 1, item.Width - 3, item.Height - 3);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        // Always-visible rounded/gradient chrome, matching RoundedButton's look in the
        // dialogs (UiHelpers.cs) instead of the old "flat, invisible until hover" style.
        var baseColor = item.Pressed
            ? p.ToolbarHover
            : item.Selected
                ? ControlPaint.Light(p.ToolbarHover, 0.05f)
                : p.ToolbarBackground;
        var topColor = ControlPaint.Light(baseColor, 0.10f);
        var bottomColor = ControlPaint.Dark(baseColor, 0.04f);

        using var path = WinForms.GraphicsHelpers.GetRoundedRect(rect, 4);
        using (var gradBrush = new LinearGradientBrush(rect, topColor, bottomColor, 90f))
            g.FillPath(gradBrush, path);

        if (item.Selected)
        {
            using var borderPen = new Pen(Color.FromArgb(160, p.Accent), 1f);
            g.DrawPath(borderPen, path);
        }
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var p = ThemeService.Current;
        var item = e.Item;

        if (item.Owner is MenuStrip)
        {
            if (item.Selected)
            {
                var rect = new Rectangle(0, 0, item.Width - 1, item.Height - 1);
                using var brush = new SolidBrush(p.ToolbarHover);
                e.Graphics.FillRectangle(brush, rect);
            }
            return;
        }

        if (item.Selected)
        {
            var rect = new Rectangle(2, 0, item.Width - 5, item.Height - 1);
            using var brush = new SolidBrush(p.ToolbarHover);
            e.Graphics.FillRectangle(brush, rect);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var p = ThemeService.Current;
        var isVertical = e.ToolStrip is not StatusStrip && !(e.ToolStrip is MenuStrip);
        if (isVertical && e.Item is ToolStripSeparator sep && sep.Owner is ToolStrip ts)
        {
            var x = sep.Width / 2;
            using var pen = new Pen(Color.FromArgb(50, p.GridLine), 1f);
            e.Graphics.DrawLine(pen, x, 4, x, ts.Height - 5);
            return;
        }
        base.OnRenderSeparator(e);
    }
}
