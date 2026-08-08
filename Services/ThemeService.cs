using System.Drawing.Drawing2D;

namespace CoderCommander.Services;

/// <summary>
/// Syntax-highlight token colors used by <see cref="WinForms.CodeEditorCanvas"/>. Split out of
/// <see cref="ThemePalette"/> so Dark/Light can each supply a full set without a wall of
/// individual color properties on the palette itself.
/// </summary>
public sealed class SyntaxPalette
{
    /// <summary>Color for single-line and multi-line comments.</summary>
    public Color Comment { get; init; }
    /// <summary>Color for numeric literals.</summary>
    public Color Number { get; init; }
    /// <summary>Color for function and method names.</summary>
    public Color Function { get; init; }
    /// <summary>Color for XML/HTML attributes and decorators.</summary>
    public Color Attribute { get; init; }
    /// <summary>Color for XML/HTML tag names.</summary>
    public Color TagName { get; init; }
    /// <summary>Color for XML/HTML tag attribute names and values.</summary>
    public Color TagAttribute { get; init; }
    /// <summary>Color for CSS selectors.</summary>
    public Color Selector { get; init; }
    /// <summary>Color for JSON object keys.</summary>
    public Color JsonKey { get; init; }
    /// <summary>Color for SQL function and keyword names.</summary>
    public Color SqlFunction { get; init; }
}

/// <summary>
/// Terminal colors for <see cref="WinForms.TerminalCanvas"/> - the 16 ANSI palette entries plus
/// cursor/selection/search-match colors. Split out of <see cref="ThemePalette"/> the same way
/// <see cref="SyntaxPalette"/> is, so Dark (Campbell, Windows Terminal's default scheme) and Light
/// (One Half Light) can each supply a full set. The 256-color cube and truecolor values are NOT
/// here - they're computed by <see cref="Terminal.Vt.Xterm256"/> from these 16 entries plus the
/// protocol's fixed color-cube/grayscale-ramp math, since they're protocol constants, not theme
/// colors.
/// </summary>
public sealed class TerminalPalette
{
    /// <summary>ANSI 0 - black.</summary>
    public Color Black { get; init; }
    /// <summary>ANSI 1 - red.</summary>
    public Color Red { get; init; }
    /// <summary>ANSI 2 - green.</summary>
    public Color Green { get; init; }
    /// <summary>ANSI 3 - yellow.</summary>
    public Color Yellow { get; init; }
    /// <summary>ANSI 4 - blue.</summary>
    public Color Blue { get; init; }
    /// <summary>ANSI 5 - magenta.</summary>
    public Color Magenta { get; init; }
    /// <summary>ANSI 6 - cyan.</summary>
    public Color Cyan { get; init; }
    /// <summary>ANSI 7 - white.</summary>
    public Color White { get; init; }
    /// <summary>ANSI 8 - bright black (grey).</summary>
    public Color BrightBlack { get; init; }
    /// <summary>ANSI 9 - bright red.</summary>
    public Color BrightRed { get; init; }
    /// <summary>ANSI 10 - bright green.</summary>
    public Color BrightGreen { get; init; }
    /// <summary>ANSI 11 - bright yellow.</summary>
    public Color BrightYellow { get; init; }
    /// <summary>ANSI 12 - bright blue.</summary>
    public Color BrightBlue { get; init; }
    /// <summary>ANSI 13 - bright magenta.</summary>
    public Color BrightMagenta { get; init; }
    /// <summary>ANSI 14 - bright cyan.</summary>
    public Color BrightCyan { get; init; }
    /// <summary>ANSI 15 - bright white.</summary>
    public Color BrightWhite { get; init; }

    /// <summary>Default foreground (SGR 39 / no SGR yet) - not necessarily <see cref="White"/>.</summary>
    public Color DefaultForeground { get; init; }
    /// <summary>Default background (SGR 49 / no SGR yet) - not necessarily <see cref="Black"/>.</summary>
    public Color DefaultBackground { get; init; }

    /// <summary>Fill color of the block cursor when the terminal has focus.</summary>
    public Color Cursor { get; init; }
    /// <summary>Color of the character glyph drawn on top of a filled <see cref="Cursor"/>.</summary>
    public Color CursorText { get; init; }
    /// <summary>Outline color of the hollow cursor drawn when the terminal has lost focus.</summary>
    public Color InactiveCursor { get; init; }

    /// <summary>Background color for the active text selection.</summary>
    public Color SelectionBackground { get; init; }
    /// <summary>Foreground color for the active text selection.</summary>
    public Color SelectionForeground { get; init; }

    /// <summary>Highlight color for non-current scrollback search matches.</summary>
    public Color SearchMatch { get; init; }
    /// <summary>Highlight color for the current (focused) scrollback search match.</summary>
    public Color SearchMatchCurrent { get; init; }

    /// <summary>Underline color for detected clickable URLs/paths.</summary>
    public Color LinkUnderline { get; init; }
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

    /// <summary>Returns a cached <see cref="Font"/> for the specified family, size, and style,
    /// creating and caching it on first access.</summary>
    /// <param name="name">Font family name (e.g. <c>"Segoe UI"</c>).</param>
    /// <param name="size">Font size in points.</param>
    /// <param name="style">Font style (default: <see cref="FontStyle.Regular"/>).</param>
    /// <returns>A shared <see cref="Font"/> instance that lives for the process lifetime.</returns>
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

    /// <summary>Creates a new <see cref="Font"/>, falling back to Segoe UI if the requested
    /// family is unavailable.</summary>
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
    /// <summary>Main editor and panel background color.</summary>
    public Color Background { get; init; } = Color.FromArgb(30, 30, 30);
    /// <summary>Background color for side panels and secondary surfaces.</summary>
    public Color PanelBackground { get; init; } = Color.FromArgb(37, 37, 38);
    /// <summary>Border color for the active/selected panel.</summary>
    public Color PanelActiveBorder { get; init; } = Color.FromArgb(0, 127, 212);
    /// <summary>Border color for inactive panels.</summary>
    public Color PanelInactiveBorder { get; init; } = Color.FromArgb(60, 60, 60);

    /// <summary>Primary foreground (text) color.</summary>
    public Color Foreground { get; init; } = Color.FromArgb(212, 212, 212);
    /// <summary>Dimmed foreground for secondary or disabled text.</summary>
    public Color DimForeground { get; init; } = Color.FromArgb(136, 136, 136);

    /// <summary>Background color for selected items in lists and grids.</summary>
    public Color Selection { get; init; } = Color.FromArgb(9, 71, 113);
    /// <summary>Foreground color for selected items.</summary>
    public Color SelectionForeground { get; init; } = Color.White;
    /// <summary>Background color for selected items that are not focused.</summary>
    public Color InactiveSelection { get; init; } = Color.FromArgb(55, 55, 61);

    /// <summary>Background color for header areas and title bars.</summary>
    public Color HeaderBackground { get; init; } = Color.FromArgb(60, 60, 60);
    /// <summary>Foreground color for header areas and title bars.</summary>
    public Color HeaderForeground { get; init; } = Color.FromArgb(204, 204, 204);
    /// <summary>Background color for toolbars.</summary>
    public Color ToolbarBackground { get; init; } = Color.FromArgb(51, 51, 51);
    /// <summary>Background color when hovering a toolbar button. Must be lighter than
    /// <see cref="ToolbarBackground"/>, otherwise hovering visually dims the button.</summary>
    public Color ToolbarHover { get; init; } = Color.FromArgb(70, 73, 74);

    /// <summary>Color for grid lines in list views and data grids.</summary>
    public Color GridLine { get; init; } = Color.FromArgb(64, 64, 64);
    /// <summary>Background color for alternating rows in list views.</summary>
    public Color AlternatingRow { get; init; } = Color.FromArgb(34, 34, 34);

    /// <summary>Primary accent color for links, focus borders, and active states.</summary>
    public Color Accent { get; init; } = Color.FromArgb(14, 99, 156);
    /// <summary>Accent color when hovering interactive elements.</summary>
    public Color AccentHover { get; init; } = Color.FromArgb(17, 119, 187);

    /// <summary>Color for directory/folder items in file lists.</summary>
    public Color DirectoryColor { get; init; } = Color.FromArgb(78, 201, 176);
    /// <summary>Color for executable file items.</summary>
    public Color ExecutableColor { get; init; } = Color.FromArgb(197, 134, 192);
    /// <summary>Color for hidden file items.</summary>
    public Color HiddenColor { get; init; } = Color.FromArgb(128, 128, 128);
    /// <summary>Color for archive file items.</summary>
    public Color ArchiveColor { get; init; } = Color.FromArgb(206, 145, 120);

    /// <summary>Color for modified/renamed git-tracked items in file lists (overrides <see cref="DirectoryColor"/>/
    /// extension-based coloring - see <c>FilePanelUserControl.GetItemForeColor</c>).</summary>
    public Color GitModifiedColor { get; init; } = Color.FromArgb(226, 192, 141);
    /// <summary>Color for new/untracked git items in file lists.</summary>
    public Color GitAddedColor { get; init; } = Color.FromArgb(115, 201, 145);

    /// <summary>Danger/error color for destructive actions and error indicators.</summary>
    public Color Danger { get; init; } = Color.FromArgb(244, 71, 71);

    /// <summary>Warning color for caution indicators and styled message boxes.</summary>
    public Color Warning { get; init; } = Color.FromArgb(255, 180, 0);

    /// <summary>Semi-transparent gloss overlay for the subtle highlight sheen on buttons and
    /// checkboxes. White in the dark theme, black in the light theme so the "light hits the
    /// top edge" effect reads correctly instead of washing out the background.</summary>
    public Color GlossOverlay { get; init; } = Color.FromArgb(30, 255, 255, 255);

    /// <summary>Background color when hovering a row in a list view.</summary>
    public Color RowHover { get; init; } = Color.FromArgb(44, 48, 50);
    /// <summary>Color for splitter bars in their normal state.</summary>
    public Color SplitterNormal { get; init; } = Color.FromArgb(64, 64, 64);
    /// <summary>Color for splitter bars when hovered.</summary>
    public Color SplitterHover { get; init; } = Color.FromArgb(14, 99, 156);
    /// <summary>Color for focus rectangles and focus indicators.</summary>
    public Color FocusBorder { get; init; } = Color.FromArgb(14, 99, 156);
    /// <summary>Gradient start color for column header backgrounds in list views.</summary>
    public Color ColumnHeaderGradient { get; init; } = Color.FromArgb(68, 68, 68);

    /// <summary>Background color of the scrollbar track.</summary>
    public Color ScrollbarTrack { get; init; } = Color.FromArgb(30, 30, 30);
    /// <summary>Color of the scrollbar thumb (normal state).</summary>
    public Color ScrollbarThumb { get; init; } = Color.FromArgb(74, 74, 74);
    /// <summary>Color of the scrollbar thumb when hovered.</summary>
    public Color ScrollbarThumbHover { get; init; } = Color.FromArgb(102, 102, 102);
    /// <summary>Color of the scrollbar thumb when pressed.</summary>
    public Color ScrollbarThumbPressed { get; init; } = Color.FromArgb(135, 135, 135);
    /// <summary>Color of scrollbar arrow buttons (normal state).</summary>
    public Color ScrollbarArrow { get; init; } = Color.FromArgb(135, 135, 135);
    /// <summary>Color of scrollbar arrow buttons when hovered.</summary>
    public Color ScrollbarArrowHover { get; init; } = Color.FromArgb(180, 180, 180);
    /// <summary>Color of the scrollbar border.</summary>
    public Color ScrollbarBorder { get; init; } = Color.FromArgb(30, 30, 30);

    /// <summary>Syntax highlighting colors for <see cref="WinForms.CodeEditorCanvas"/>.</summary>
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

    /// <summary>Terminal colors for <see cref="WinForms.TerminalCanvas"/> - Campbell (Windows
    /// Terminal's default dark scheme).</summary>
    public TerminalPalette Terminal { get; init; } = new()
    {
        Black = Color.FromArgb(12, 12, 12),
        Red = Color.FromArgb(197, 15, 31),
        Green = Color.FromArgb(19, 161, 14),
        Yellow = Color.FromArgb(193, 156, 0),
        Blue = Color.FromArgb(0, 55, 218),
        Magenta = Color.FromArgb(136, 23, 152),
        Cyan = Color.FromArgb(58, 150, 221),
        White = Color.FromArgb(204, 204, 204),
        BrightBlack = Color.FromArgb(118, 118, 118),
        BrightRed = Color.FromArgb(231, 72, 86),
        BrightGreen = Color.FromArgb(22, 198, 12),
        BrightYellow = Color.FromArgb(249, 241, 165),
        BrightBlue = Color.FromArgb(59, 120, 255),
        BrightMagenta = Color.FromArgb(180, 0, 158),
        BrightCyan = Color.FromArgb(97, 214, 214),
        BrightWhite = Color.FromArgb(242, 242, 242),
        DefaultForeground = Color.FromArgb(204, 204, 204),
        DefaultBackground = Color.FromArgb(12, 12, 12),
        Cursor = Color.FromArgb(255, 255, 255),
        CursorText = Color.FromArgb(12, 12, 12),
        InactiveCursor = Color.FromArgb(128, 128, 128),
        SelectionBackground = Color.FromArgb(38, 79, 120),
        SelectionForeground = Color.FromArgb(255, 255, 255),
        SearchMatch = Color.FromArgb(96, 76, 21),
        SearchMatchCurrent = Color.FromArgb(199, 148, 22),
        LinkUnderline = Color.FromArgb(58, 150, 221)
    };

    /// <summary>Font used for file list grid cells.</summary>
    public Font GridFont { get; init; } = FontCache.Get("Segoe UI", 9F);
    /// <summary>Bold font for file list grid cells (e.g. highlighted rows).</summary>
    public Font GridFontBold { get; init; } = FontCache.Get("Segoe UI", 9F, FontStyle.Bold);
    /// <summary>Bold font for toolbar and menu headers.</summary>
    public Font HeaderFont { get; init; } = FontCache.Get("Segoe UI", 9F, FontStyle.Bold);
    /// <summary>Font for the status bar text.</summary>
    public Font StatusBarFont { get; init; } = FontCache.Get("Segoe UI", 8.5F);
    /// <summary>Font for panel captions and labels.</summary>
    public Font CaptionFont { get; init; } = FontCache.Get("Segoe UI", 9F);
    /// <summary>Monospace font for code editors and terminal panels.</summary>
    public Font MonoFont { get; init; } = FontCache.Get("Consolas", 9.5F);

    /// <summary>Bold title font for dialog chrome (15pt).</summary>
    public Font TitleFont { get; init; } = FontCache.Get("Segoe UI", 15F, FontStyle.Bold);
    /// <summary>Bold subtitle font for dialog chrome (13pt).</summary>
    public Font SubtitleFont { get; init; } = FontCache.Get("Segoe UI", 13F, FontStyle.Bold);
    /// <summary>Bold section header font for dialog chrome (10pt).</summary>
    public Font SectionFont { get; init; } = FontCache.Get("Segoe UI", 10F, FontStyle.Bold);
    /// <summary>Small font for hint text and secondary labels (8.5pt).</summary>
    public Font SmallFont { get; init; } = FontCache.Get("Segoe UI", 8.5F);
    /// <summary>Italic font for emphasis or placeholder text.</summary>
    public Font ItalicFont { get; init; } = FontCache.Get("Segoe UI", 9F, FontStyle.Italic);
    /// <summary>Large glyph font for icon placeholders in dialogs (24pt).</summary>
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
    /// <summary>Currently active theme palette. Updated by <see cref="ApplyTheme"/>.</summary>
    public static ThemePalette Current { get; private set; } = CreateDark();

    /// <summary>Creates a new instance of the VSCode Dark+ theme palette.</summary>
    public static ThemePalette CreateDark() => new();

    /// <summary>Creates a new instance of the VSCode Light+ theme palette.</summary>
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
        GitModifiedColor = Color.FromArgb(137, 85, 3),
        GitAddedColor = Color.FromArgb(41, 111, 15),
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
        },
        Terminal = new()
        {
            // One Half Light
            Black = Color.FromArgb(56, 58, 66),
            Red = Color.FromArgb(228, 86, 73),
            Green = Color.FromArgb(80, 161, 79),
            Yellow = Color.FromArgb(193, 131, 1),
            Blue = Color.FromArgb(1, 132, 188),
            Magenta = Color.FromArgb(166, 38, 164),
            Cyan = Color.FromArgb(9, 151, 179),
            White = Color.FromArgb(250, 250, 250),
            BrightBlack = Color.FromArgb(79, 82, 94),
            BrightRed = Color.FromArgb(223, 108, 117),
            BrightGreen = Color.FromArgb(152, 195, 121),
            BrightYellow = Color.FromArgb(229, 192, 123),
            BrightBlue = Color.FromArgb(97, 175, 239),
            BrightMagenta = Color.FromArgb(198, 120, 221),
            BrightCyan = Color.FromArgb(86, 182, 194),
            BrightWhite = Color.FromArgb(255, 255, 255),
            DefaultForeground = Color.FromArgb(56, 58, 66),
            DefaultBackground = Color.FromArgb(250, 250, 250),
            Cursor = Color.FromArgb(56, 58, 66),
            CursorText = Color.FromArgb(250, 250, 250),
            InactiveCursor = Color.FromArgb(160, 161, 167),
            SelectionBackground = Color.FromArgb(200, 222, 255),
            SelectionForeground = Color.FromArgb(56, 58, 66),
            SearchMatch = Color.FromArgb(255, 223, 128),
            SearchMatchCurrent = Color.FromArgb(255, 173, 51),
            LinkUnderline = Color.FromArgb(1, 132, 188)
        }
    };

    /// <summary>Raised after <see cref="ApplyTheme"/> swaps the active palette so that all
    /// controls can re-read <see cref="Current"/> and repaint.</summary>
    public static event EventHandler? ThemeChanged;

    /// <summary>
    /// Whether the active palette is a dark theme, by background luminance. Single source of
    /// truth for the "is this dark?" check that used to be duplicated at every dark-titlebar/
    /// dark-scrollbar call site.
    /// </summary>
    public static bool IsDark => Current.Background.GetBrightness() < 0.5f;

    /// <summary>Switches the active theme palette by name, clears icon caches, and raises
    /// <see cref="ThemeChanged"/>.</summary>
    /// <param name="themeName">Theme name: <c>"Dark"</c> or <c>"Light"</c>. Any other value
    /// falls back to Dark.</param>
    public static void ApplyTheme(string themeName)
    {
        Current = themeName == "Light" ? CreateLight() : CreateDark();
        WinForms.ToolbarIcons.ClearCache();
        WinForms.FileIcons.ClearCache();
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Applies current palette background, foreground, and font to a <see cref="Form"/>.</summary>
    /// <param name="form">The form to style.</param>
    public static void StyleForm(Form form)
    {
        var p = Current;
        form.BackColor = p.Background;
        form.ForeColor = p.Foreground;
        form.Font = p.GridFont;
    }

    /// <summary>Applies current palette colors and <see cref="ThemeRenderer"/> to a <see cref="ToolStrip"/>.</summary>
    /// <param name="ts">The tool strip to style.</param>
    public static void StyleToolStrip(ToolStrip ts)
    {
        var p = Current;
        ts.BackColor = p.ToolbarBackground;
        ts.ForeColor = p.HeaderForeground;
        ts.Renderer = new ThemeRenderer();
    }

    /// <summary>Applies current palette colors and <see cref="ThemeRenderer"/> to a <see cref="MenuStrip"/>.</summary>
    /// <param name="ms">The menu strip to style.</param>
    public static void StyleMenu(MenuStrip ms)
    {
        var p = Current;
        ms.BackColor = p.ToolbarBackground;
        ms.ForeColor = p.HeaderForeground;
        ms.Renderer = new ThemeRenderer();
        ms.Padding = new Padding(2, 3, 0, 3);
    }

    /// <summary>Applies current palette colors and <see cref="ThemeRenderer"/> to a <see cref="StatusStrip"/>.</summary>
    /// <param name="ss">The status strip to style.</param>
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
/// <summary>
/// ProfessionalColorTable driven by the active <see cref="ThemePalette"/>.
/// Provides dark/light-theme-aware colors for menu dropdowns, toolstrip buttons, and separators.
/// </summary>
public sealed class ThemeColorTable : ProfessionalColorTable
{
    /// <summary>Initializes a new instance using the current theme palette.</summary>
    public ThemeColorTable() : base() { }

    private static ThemePalette P => ThemeService.Current;

    /// <summary>Start gradient color of the menu strip background.</summary>
    public override Color MenuStripGradientBegin => P.ToolbarBackground;
    /// <summary>End gradient color of the menu strip background.</summary>
    public override Color MenuStripGradientEnd => P.ToolbarBackground;
    /// <summary>Color of the menu border.</summary>
    public override Color MenuBorder => P.GridLine;
    /// <summary>Border color around a selected menu item (transparent to avoid double-border effect).</summary>
    public override Color MenuItemBorder => Color.Transparent;
    /// <summary>Start gradient when a menu item is pressed.</summary>
    public override Color MenuItemPressedGradientBegin => P.ToolbarBackground;
    /// <summary>Middle gradient when a menu item is pressed.</summary>
    public override Color MenuItemPressedGradientMiddle => P.ToolbarBackground;
    /// <summary>End gradient when a menu item is pressed.</summary>
    public override Color MenuItemPressedGradientEnd => P.ToolbarBackground;
    /// <summary>Background color of a selected (highlighted) menu item.</summary>
    public override Color MenuItemSelected => P.ToolbarHover;
    /// <summary>Start gradient of a selected menu item.</summary>
    public override Color MenuItemSelectedGradientBegin => P.ToolbarHover;
    /// <summary>End gradient of a selected menu item.</summary>
    public override Color MenuItemSelectedGradientEnd => P.ToolbarHover;
    /// <summary>Start gradient of the image margin area in menu items.</summary>
    public override Color ImageMarginGradientBegin => P.PanelBackground;
    /// <summary>Middle gradient of the image margin area in menu items.</summary>
    public override Color ImageMarginGradientMiddle => P.PanelBackground;
    /// <summary>End gradient of the image margin area in menu items.</summary>
    public override Color ImageMarginGradientEnd => P.PanelBackground;
    /// <summary>Start gradient of the toolstrip background.</summary>
    public override Color ToolStripGradientBegin => P.ToolbarBackground;
    /// <summary>Middle gradient of the toolstrip background.</summary>
    public override Color ToolStripGradientMiddle => P.ToolbarBackground;
    /// <summary>End gradient of the toolstrip background.</summary>
    public override Color ToolStripGradientEnd => P.ToolbarBackground;
    /// <summary>Color of the toolstrip border.</summary>
    public override Color ToolStripBorder => P.ToolbarBackground;
    /// <summary>Background color of toolstrip dropdown menus.</summary>
    public override Color ToolStripDropDownBackground => P.PanelBackground;
    /// <summary>Start gradient of the toolstrip content panel.</summary>
    public override Color ToolStripContentPanelGradientBegin => P.Background;
    /// <summary>End gradient of the toolstrip content panel.</summary>
    public override Color ToolStripContentPanelGradientEnd => P.Background;
    /// <summary>Start gradient of the toolstrip panel.</summary>
    public override Color ToolStripPanelGradientBegin => P.ToolbarBackground;
    /// <summary>End gradient of the toolstrip panel.</summary>
    public override Color ToolStripPanelGradientEnd => P.ToolbarBackground;
    /// <summary>Border color of a selected toolbar button.</summary>
    public override Color ButtonSelectedBorder => P.Accent;
    /// <summary>Start gradient of a selected toolbar button.</summary>
    public override Color ButtonSelectedGradientBegin => P.ToolbarHover;
    /// <summary>Middle gradient of a selected toolbar button.</summary>
    public override Color ButtonSelectedGradientMiddle => P.ToolbarHover;
    /// <summary>End gradient of a selected toolbar button.</summary>
    public override Color ButtonSelectedGradientEnd => P.ToolbarHover;
    /// <summary>Border color of a pressed toolbar button.</summary>
    public override Color ButtonPressedBorder => P.Accent;
    /// <summary>Start gradient of a pressed toolbar button.</summary>
    public override Color ButtonPressedGradientBegin => P.ToolbarHover;
    /// <summary>Middle gradient of a pressed toolbar button.</summary>
    public override Color ButtonPressedGradientMiddle => P.ToolbarHover;
    /// <summary>End gradient of a pressed toolbar button.</summary>
    public override Color ButtonPressedGradientEnd => P.ToolbarHover;
    /// <summary>Start gradient of a checked toolbar button.</summary>
    public override Color ButtonCheckedGradientBegin => P.ToolbarHover;
    /// <summary>Middle gradient of a checked toolbar button.</summary>
    public override Color ButtonCheckedGradientMiddle => P.ToolbarHover;
    /// <summary>End gradient of a checked toolbar button.</summary>
    public override Color ButtonCheckedGradientEnd => P.ToolbarHover;
    /// <summary>Background color when a checked button is highlighted.</summary>
    public override Color ButtonCheckedHighlight => P.ToolbarHover;
    /// <summary>Border color when a checked button is highlighted.</summary>
    public override Color ButtonCheckedHighlightBorder => P.Accent;
    /// <summary>Dark color of a separator line.</summary>
    public override Color SeparatorDark => P.GridLine;
    /// <summary>Light color of a separator line.</summary>
    public override Color SeparatorLight => P.GridLine;
    /// <summary>Background of a check mark in a menu item.</summary>
    public override Color CheckBackground => P.Accent;
    /// <summary>Background of a check mark when pressed.</summary>
    public override Color CheckPressedBackground => P.AccentHover;
    /// <summary>Background of a check mark when the item is selected.</summary>
    public override Color CheckSelectedBackground => P.AccentHover;
    /// <summary>Start gradient of the overflow (dropdown) button.</summary>
    public override Color OverflowButtonGradientBegin => P.ToolbarBackground;
    /// <summary>Middle gradient of the overflow (dropdown) button.</summary>
    public override Color OverflowButtonGradientMiddle => P.ToolbarBackground;
    /// <summary>End gradient of the overflow (dropdown) button.</summary>
    public override Color OverflowButtonGradientEnd => P.ToolbarBackground;
    /// <summary>Start gradient of the status strip background.</summary>
    public override Color StatusStripGradientBegin => P.HeaderBackground;
    /// <summary>End gradient of the status strip background.</summary>
    public override Color StatusStripGradientEnd => P.HeaderBackground;
}

/// <summary>
/// Custom ToolStripProfessionalRenderer that applies theme colors to backgrounds
/// and provides flat hover/selected states for toolbar buttons (VSCode style).
/// </summary>
public sealed class ThemeRenderer : ToolStripProfessionalRenderer
{
    /// <summary>Initializes a new renderer using the active <see cref="ThemeColorTable"/>.</summary>
    public ThemeRenderer() : base(new ThemeColorTable()) { }

    /// <summary>Renders the toolbar/statusstrip background with theme colors and a subtle top separator.</summary>
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

    /// <summary>Suppresses the default toolstrip border rendering (theme handles it via backgrounds).</summary>
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

    /// <summary>Renders a rounded gradient background for toolbar buttons (VSCode style).</summary>
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

    /// <summary>Renders the hover background for menu items and top-level menu bar entries.</summary>
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

    /// <summary>Renders vertical and horizontal separators with theme-aware colors.</summary>
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
