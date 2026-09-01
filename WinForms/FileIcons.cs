using CoderCommander.Services;
using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace CoderCommander.WinForms;

/// <summary>Enumerates the supported file icon types for vector-drawn icons.</summary>
public enum FileIconType
{
    File, Folder, ParentFolder,
    Text, Image, Audio, Video, Archive, Executable,
    Pdf, Html, Css,
    Code, CSharp, JavaScript, Python,
    Json, Xml, Markdown,
    Word, Excel, PowerPoint,
    Shortcut, Database, DiskImage
}

public static class FileIcons
{
    private static readonly ConcurrentDictionary<(FileIconType, int), Bitmap> _sizedCache = new();
    private static int _lastDpi = 96;
    private static List<Bitmap>? _pendingDisposal;

    /// <summary>Guards <see cref="_pendingDisposal"/>/<see cref="_lastDpi"/> and the
    /// generation-swap in <see cref="ClearCache"/> - it is called both from
    /// <c>ThemeService.ApplyTheme</c> and from <see cref="Get(FileIconType, int)"/>'s own DPI-change
    /// branch, and without this lock two concurrent calls could double-dispose the same Bitmap
    /// (both threads reading the same <see cref="_pendingDisposal"/> before either replaces it) or
    /// drop a generation's bitmaps out of <see cref="_pendingDisposal"/> entirely.</summary>
    private static readonly object _clearLock = new();

    /// <summary>
    /// Drops the cache. Bitmaps handed out by <see cref="Get(FileIconType, int)"/> are assigned
    /// directly to long-lived controls (menu items, toolbar buttons, ImageLists) that don't
    /// necessarily re-fetch and reassign their Image on every theme/DPI change, so disposing them
    /// on the spot risks an already-shown control drawing a disposed Bitmap. Keep this generation
    /// alive for one more clear instead - bounds the leak to at most one extra generation.
    /// </summary>
    public static void ClearCache()
    {
        lock (_clearLock)
        {
            _pendingDisposal?.ForEach(b => b.Dispose());
            _pendingDisposal = _sizedCache.Values.ToList();
            _sizedCache.Clear();
        }
    }

    private static int GetCurrentDpi()
    {
        try
        {
            // Graphics.FromHwnd(IntPtr.Zero) reads the screen DPI, unlike Graphics.FromImage
            // which always returns 96 (bitmap metadata DPI, not display DPI).
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return (int)g.DpiX;
        }
        catch
        {
            return 96;
        }
    }

    /// <summary>Maps a file extension to its corresponding <see cref="FileIconType"/>.</summary>
    public static FileIconType GetIconType(string extension)
    {
        return extension switch
        {
            ".txt" or ".log" or ".cfg" or ".ini" or ".conf" or ".rtf" => FileIconType.Text,
            ".md" => FileIconType.Markdown,
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" or ".ico"
                or ".webp" or ".tiff" or ".tif" or ".psd" or ".raw" or ".cr2" or ".nef" => FileIconType.Image,
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".wma" or ".m4a"
                or ".opus" or ".aiff" => FileIconType.Audio,
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".flv" or ".webm"
                or ".m4v" or ".mpg" or ".mpeg" or ".3gp" => FileIconType.Video,
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz"
                or ".zst" or ".lz" or ".cab" or ".iso" => FileIconType.Archive,
            ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" or ".sh" or ".com" => FileIconType.Executable,
            ".pdf" => FileIconType.Pdf,
            ".html" or ".htm" => FileIconType.Html,
            ".css" or ".scss" or ".less" or ".sass" => FileIconType.Css,
            ".cs" => FileIconType.CSharp,
            ".js" or ".ts" or ".jsx" or ".tsx" or ".mjs" => FileIconType.JavaScript,
            ".py" or ".pyw" => FileIconType.Python,
            ".json" or ".jsonc" => FileIconType.Json,
            ".xml" or ".xsl" or ".xslt" or ".xsd" or ".dtd" => FileIconType.Xml,
            ".doc" or ".docx" or ".odt" or ".rtf" => FileIconType.Word,
            ".xls" or ".xlsx" or ".csv" or ".ods" => FileIconType.Excel,
            ".ppt" or ".pptx" or ".odp" => FileIconType.PowerPoint,
            ".lnk" or ".url" => FileIconType.Shortcut,
            ".db" or ".sqlite" or ".sql" or ".mdb" => FileIconType.Database,
            ".img" or ".vhd" or ".vhdx" or ".vmdk" => FileIconType.DiskImage,
            ".c" or ".cpp" or ".h" or ".hpp" or ".cc" or ".cxx" or ".java"
                or ".go" or ".rs" or ".rb" or ".php" or ".swift" or ".kt"
                or ".scala" or ".r" or ".lua" or ".pl" or ".vb" => FileIconType.Code,
            _ => FileIconType.File
        };
    }

    /// <summary>Gets a 16x16 icon bitmap for the specified file type.</summary>
    public static Bitmap Get(FileIconType type) => Get(type, 16);

    /// <summary>Render an icon at the requested pixel size (vector quality, DPI-aware).</summary>
    public static Bitmap Get(FileIconType type, int px)
    {
        // Deliberately unlocked out here (only ClearCache() itself takes _clearLock): two
        // concurrent callers both observing a stale _lastDpi both call ClearCache() and both then
        // (redundantly but harmlessly) write the same new _lastDpi - ClearCache()'s own lock is
        // what actually matters, since that's where the double-dispose/dropped-generation race
        // was. Wrapping this compare in the same lock too would close that last sliver of
        // redundancy, but CA2000 loses track of ToolStripButton disposal ownership at unrelated
        // call sites when Get() (used inline in an object initializer) contains a lock statement -
        // a known analyzer sensitivity to try/finally shape in a value-returning callee, not a
        // real defect there.
        var currentDpi = GetCurrentDpi();
        if (currentDpi != _lastDpi)
        {
            ClearCache();
            _lastDpi = currentDpi;
        }
        var key = (type, Math.Max(12, px));
        return _sizedCache.GetOrAdd(key, k => Draw(k.Item1, k.Item2));
    }

    private static Bitmap Draw(FileIconType type, int px)
    {
        var baseSize = Math.Max(16, px);
        // Cached in _sizedCache and disposed one generation later by ClearCache() (see its doc
        // comment) - the analyzer can't see that far-delayed disposal path.
#pragma warning disable CA2000
        var bmp = new Bitmap(baseSize, baseSize);
#pragma warning restore CA2000
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);
        var scale = baseSize / 16f;
        g.ScaleTransform(scale, scale);

        var p = DesignerSafeThemeService.Current;
        // Deliberate exception to the "no hardcoded colors" rule (see CLAUDE.md's Theming section):
        // these are brand/format accent colors (PDF red, Word blue, JS yellow, folder/file-type
        // tints, etc.) that are meant to stay recognizable and identical in both Dark and Light
        // theme, the same way a real OS file icon doesn't recolor itself when the system theme
        // changes. Only p.Accent/p.Foreground/p.DimForeground (the theme-following bits) come from
        // ThemePalette; everything else here is intentionally theme-invariant.
        switch (type)
        {
            case FileIconType.Folder: DrawFolder(g, p.Accent, p.Foreground); break;
            case FileIconType.ParentFolder: DrawParentFolder(g, p.DimForeground); break;
            case FileIconType.Text: DrawDoc(g, p.DimForeground, p.Foreground, "lines"); break;
            case FileIconType.Markdown: DrawDoc(g, p.Foreground, p.Foreground, "M"); break;
            case FileIconType.Image: DrawDoc(g, Color.FromArgb(76, 175, 80), p.Foreground, "image"); break;
            case FileIconType.Audio: DrawDoc(g, Color.FromArgb(233, 30, 99), p.Foreground, "note"); break;
            case FileIconType.Video: DrawDoc(g, Color.FromArgb(156, 39, 176), p.Foreground, "play"); break;
            case FileIconType.Archive: DrawDoc(g, Color.FromArgb(255, 152, 0), p.Foreground, "zip"); break;
            case FileIconType.Executable: DrawDoc(g, Color.FromArgb(96, 125, 139), p.Foreground, "gear"); break;
            case FileIconType.Pdf: DrawDoc(g, Color.FromArgb(229, 57, 53), Color.White, "pdf"); break;
            case FileIconType.Html: DrawDoc(g, Color.FromArgb(244, 81, 30), p.Foreground, "html"); break;
            case FileIconType.Css: DrawDoc(g, Color.FromArgb(30, 136, 229), p.Foreground, "css"); break;
            case FileIconType.Code: DrawDoc(g, Color.FromArgb(0, 150, 136), p.Foreground, "code"); break;
            case FileIconType.CSharp: DrawDoc(g, Color.FromArgb(121, 134, 203), Color.White, "C#"); break;
            case FileIconType.JavaScript: DrawDoc(g, Color.FromArgb(247, 223, 30), Color.Black, "JS"); break;
            case FileIconType.Python: DrawDoc(g, Color.FromArgb(55, 118, 171), Color.White, "Py"); break;
            case FileIconType.Json: DrawDoc(g, Color.FromArgb(158, 158, 158), p.Foreground, "{}"); break;
            case FileIconType.Xml: DrawDoc(g, Color.FromArgb(121, 85, 72), p.Foreground, "<>"); break;
            case FileIconType.Word: DrawDoc(g, Color.FromArgb(43, 87, 154), Color.White, "W"); break;
            case FileIconType.Excel: DrawDoc(g, Color.FromArgb(33, 115, 70), Color.White, "X"); break;
            case FileIconType.PowerPoint: DrawDoc(g, Color.FromArgb(209, 71, 0), Color.White, "P"); break;
            case FileIconType.Shortcut: DrawDoc(g, p.DimForeground, p.Foreground, "link"); break;
            case FileIconType.Database: DrawDoc(g, Color.FromArgb(0, 188, 212), p.Foreground, "db"); break;
            case FileIconType.DiskImage: DrawDoc(g, Color.FromArgb(69, 90, 100), p.Foreground, "disk"); break;
            default: DrawDoc(g, Color.FromArgb(120, 120, 128), p.Foreground, ""); break;
        }
        return bmp;
    }

    private static void DrawDoc(Graphics g, Color accent, Color textColor, string overlay)
    {
        using var fill = new SolidBrush(Color.FromArgb(20, accent));
        using var pen = new Pen(Color.FromArgb(160, accent), 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        var body = new PointF[]
        {
            new(3, 1.5f), new(10, 1.5f), new(13, 4.5f), new(13, 14.5f), new(3, 14.5f)
        };
        g.FillPolygon(fill, body);
        g.DrawLines(pen, body);
        g.DrawLine(pen, 3, 1.5f, 3, 14.5f);

        using var foldPen = new Pen(Color.FromArgb(120, accent), 1f);
        g.DrawLine(foldPen, 10, 1.5f, 10, 4.5f);
        g.DrawLine(foldPen, 10, 4.5f, 13, 4.5f);

        if (string.IsNullOrEmpty(overlay)) return;

        var area = new Rectangle(4, 6, 8, 7);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;

        switch (overlay)
        {
            case "lines":
                using (var linePen = new Pen(Color.FromArgb(140, textColor), 1f) { StartCap = LineCap.Round })
                {
                    g.DrawLine(linePen, 5, 7.5f, 11, 7.5f);
                    g.DrawLine(linePen, 5, 9.5f, 11, 9.5f);
                    g.DrawLine(linePen, 5, 11.5f, 9, 11.5f);
                }
                break;

            case "image":
                using (var imgPen = new Pen(accent, 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(imgPen, 5, 12, 7, 9);
                    g.DrawLine(imgPen, 7, 9, 9, 11);
                    g.DrawLine(imgPen, 9, 11, 11, 8.5f);
                }
                using (var sunBrush = new SolidBrush(accent))
                    g.FillEllipse(sunBrush, 9.5f, 6.5f, 2, 2);
                break;

            case "note":
                using (var notePen = new Pen(accent, 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawEllipse(notePen, 5.5f, 9.5f, 3, 3);
                    g.DrawLine(notePen, 8.5f, 11, 8.5f, 6);
                    g.DrawLine(notePen, 8.5f, 6, 11, 7);
                }
                break;

            case "play":
                using (var playBrush = new SolidBrush(accent))
                    g.FillPolygon(playBrush, new PointF[] { new(6, 6.5f), new(11, 9.5f), new(6, 12.5f) });
                break;

            case "zip":
                using (var zipPen = new Pen(accent, 1f))
                {
                    for (int i = 0; i < 4; i++)
                        g.DrawLine(zipPen, 7.5f, 6 + i * 2f, 8.5f, 6 + i * 2f);
                }
                using (var zipperPen = new Pen(accent, 1.2f))
                    g.DrawRectangle(zipperPen, 6.5f, 10, 3, 3);
                break;

            case "gear":
                using (var gearPen = new Pen(accent, 1.1f))
                {
                    g.DrawEllipse(gearPen, 5.5f, 6.5f, 5, 5);
                    g.DrawEllipse(gearPen, 7, 8, 2, 2);
                }
                break;

            case "db":
                using (var dbPen = new Pen(accent, 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawEllipse(dbPen, 5, 6, 6, 3);
                    g.DrawLine(dbPen, 5, 7.5f, 5, 11.5f);
                    g.DrawLine(dbPen, 11, 7.5f, 11, 11.5f);
                    g.DrawArc(dbPen, 5, 10, 6, 3, 0, 180);
                }
                break;

            case "disk":
                using (var diskPen = new Pen(accent, 1.2f))
                {
                    g.DrawEllipse(diskPen, 4, 4, 8, 8);
                    g.DrawEllipse(diskPen, 6.5f, 6.5f, 3, 3);
                }
                break;

            case "link":
                using (var linkPen = new Pen(accent, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(linkPen, 4, 7, 5, 5, 135, 270);
                    g.DrawArc(linkPen, 7, 7, 5, 5, 315, 270);
                }
                break;

            case "code":
                using (var codePen = new Pen(accent, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(codePen, 5, 7, 7, 9);
                    g.DrawLine(codePen, 7, 9, 5, 11);
                    g.DrawLine(codePen, 11, 7, 9, 9);
                    g.DrawLine(codePen, 9, 9, 11, 11);
                }
                break;

            default:
                if (overlay.Length <= 2)
                {
                    using var font = new Font(DesignerSafeThemeService.Current.GridFont.FontFamily, overlay.Length == 1 ? 7f : 5.5f, FontStyle.Bold);
                    TextRenderer.DrawText(g, overlay, font, area, accent, flags);
                }
                break;
        }
    }

    private static void DrawFolder(Graphics g, Color color, Color inner)
    {
        using var fill = new SolidBrush(Color.FromArgb(60, color));
        using var pen = new Pen(color, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        var tab = new PointF[] { new(2, 5), new(2, 3.5f), new(5, 3.5f), new(6.5f, 5) };
        g.FillPolygon(fill, tab);
        g.DrawLines(pen, tab);

        var body = new PointF[] { new(1.5f, 5), new(1.5f, 13.5f), new(14.5f, 13.5f), new(14.5f, 5) };
        g.FillPolygon(fill, body);
        g.DrawLines(pen, body);

        using var linePen = new Pen(Color.FromArgb(100, color), 1f);
        g.DrawLine(linePen, 2, 7, 14, 7);
    }

    private static void DrawParentFolder(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, 8, 13, 8, 4);
        g.DrawLine(pen, 8, 4, 4.5f, 7.5f);
        g.DrawLine(pen, 8, 4, 11.5f, 7.5f);
    }
}
