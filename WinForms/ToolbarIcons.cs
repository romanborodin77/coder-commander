using CoderCommander.Services;
using System.Collections.Concurrent;
using System.Drawing.Drawing2D;

namespace CoderCommander.WinForms;

/// <summary>
/// Procedurally generates 16x16 toolbar/menu icons in VSCode flat style:
/// clean geometric shapes, thin strokes, no rounded corners.
/// No image assets required.
/// </summary>
public static class ToolbarIcons
{
    private static readonly ConcurrentDictionary<string, Bitmap> _cache = new();
    private static int _lastDpi = 96;
    private static List<Bitmap>? _pendingDisposal;

    public static Image? Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        // Проверяем, изменился ли DPI
        var currentDpi = GetCurrentDpi();
        if (currentDpi != _lastDpi)
        {
            // DPI изменился, очищаем кеш
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
            // Graphics.FromImage(new Bitmap(1,1)) always reports 96 — it reads the
            // bitmap's own resolution metadata, never the screen. FromHwnd(IntPtr.Zero)
            // reads the actual desktop DPI instead.
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

        // Вычисляем размер с учётом DPI
        var scale = _lastDpi / 96.0f;
        var size = (int)Math.Round(16 * scale);
        size = Math.Max(16, size); // Минимум 16x16

        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);

        // Настройки для чёткой пиксельной графики
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        // Масштабируем графику для рисования
        g.ScaleTransform(scale, scale);

        var fg = p.HeaderForeground;  // Use HeaderForeground for toolbar icons (better contrast on light theme)
        var accent = p.Accent;
        var dim = p.DimForeground;

        switch (key)
        {
            case "view": DrawView(g, fg); break;
            case "edit": DrawEdit(g, fg); break;
            case "editnew": DrawEditNew(g, accent); break;
            case "copy": DrawCopy(g, fg); break;
            case "move": DrawMove(g, fg); break;
            case "rename": DrawRename(g, fg); break;
            case "delete": DrawDelete(g, p.Danger); break;
            case "undo": DrawUndo(g, fg); break;
            case "redo": DrawRedo(g, fg); break;
            case "cut": DrawCut(g, fg); break;
            case "paste": DrawPaste(g, fg); break;
            case "newdir": DrawNewDir(g, accent); break;
            case "wipe": DrawWipe(g, p.Danger); break;
            case "refresh": DrawRefresh(g, accent); break;
            case "search": DrawSearch(g, fg); break;
            case "settings": DrawSettings(g, fg); break;
            case "bookmarks": DrawBookmark(g, accent); break;
            case "terminal": DrawTerminal(g, fg); break;
            case "up": DrawUp(g, fg); break;
            case "back": DrawBack(g, fg); break;
            case "forward": DrawForward(g, fg); break;
            case "root": DrawRoot(g, fg); break;
            case "home": DrawHome(g, accent); break;
            case "selectall": DrawSelectAll(g, accent); break;
            case "deselectall": DrawDeselectAll(g, dim); break;
            case "invert": DrawInvert(g, fg); break;
            case "pack": DrawPack(g, fg); break;
            case "extract": DrawExtract(g, fg); break;
            case "syncdirs": DrawSyncDirs(g, accent); break;
            case "multirename": DrawMultiRename(g, accent); break;
            case "exit": DrawExit(g, fg); break;
            case "properties": DrawProperties(g, fg); break;
            case "drive": DrawDrive(g, accent); break;
            case "drive_fixed": DrawDriveFixed(g, accent); break;
            case "drive_removable": DrawDriveRemovable(g, accent); break;
            case "drive_cdrom": DrawDriveCdrom(g, accent); break;
            case "drive_network": DrawDriveNetwork(g, accent); break;
            case "drive_ram": DrawDriveRam(g, accent); break;
            default: DrawGeneric(g, fg); break;
        }
        return bmp;
    }

    private static void DrawView(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 2, 3, 12, 10);
        g.DrawLine(pen, 2, 6, 14, 6);
    }

    private static void DrawEdit(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        var pts = new Point[] { new(11, 3), new(13, 5), new(5, 13), new(3, 13), new(3, 11) };
        g.DrawLines(pen, pts);
        g.DrawLine(pen, 3, 13, 5, 13);
        g.DrawLine(pen, 9, 5, 11, 3);
    }

    private static void DrawEditNew(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 5, 2, 8, 11);
        g.DrawLine(pen, 8, 8, 11, 8);
        g.DrawLine(pen, 9.5f, 6.5f, 9.5f, 9.5f);
        g.DrawLine(pen, 8, 8, 11, 8);
    }

    private static void DrawCopy(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 5, 2, 8, 10);
        g.DrawRectangle(pen, 2, 5, 8, 9);
    }

    private static void DrawMove(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawLine(pen, 2, 8, 12, 8);
        g.DrawLine(pen, 12, 8, 9, 5);
        g.DrawLine(pen, 12, 8, 9, 11);
    }

    private static void DrawRename(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 2, 6, 12, 5);
        g.DrawLine(pen, 5, 5, 5, 12);
    }

    private static void DrawDelete(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawLine(pen, 4, 4, 12, 12);
        g.DrawLine(pen, 12, 4, 4, 12);
    }

    private static void DrawUndo(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, 4, 4, 8, 8, -10, -200);
        var arrow = new Point[] { new(3, 6), new(6, 3), new(7, 7) };
        using var brush = new SolidBrush(c);
        g.FillPolygon(brush, arrow);
    }

    private static void DrawRedo(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, 4, 4, 8, 8, 190, 200);
        var arrow = new Point[] { new(13, 6), new(10, 3), new(9, 7) };
        using var brush = new SolidBrush(c);
        g.FillPolygon(brush, arrow);
    }

    private static void DrawCut(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1.2f);
        g.DrawEllipse(pen, 2, 2, 4, 4);
        g.DrawEllipse(pen, 2, 10, 4, 4);
        g.DrawLine(pen, 5, 5, 13, 8);
        g.DrawLine(pen, 5, 11, 13, 8);
    }

    private static void DrawPaste(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 3, 3, 10, 11);
        g.DrawRectangle(pen, 6, 2, 4, 2);
        g.DrawLine(pen, 5, 7, 11, 7);
        g.DrawLine(pen, 5, 9, 11, 9);
        g.DrawLine(pen, 5, 11, 9, 11);
    }

    private static void DrawNewDir(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        var pts = new Point[] { new(2, 5), new(2, 13), new(14, 13), new(14, 7), new(8, 7), new(6, 5) };
        g.DrawLines(pen, pts);
        g.DrawLine(pen, 2, 5, 6, 5);
        g.DrawLine(pen, 8, 9, 8, 12);
        g.DrawLine(pen, 6, 10, 10, 10);
    }

    private static void DrawWipe(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 3, 3, 10, 10);
        g.DrawLine(pen, 3, 3, 13, 13);
        g.DrawLine(pen, 13, 3, 3, 13);
    }

    private static void DrawRefresh(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawArc(pen, 3, 3, 10, 10, 30, 280);
        g.FillPolygon(new SolidBrush(c), new Point[] { new(12, 3), new(14, 6), new(10, 6) });
    }

    private static void DrawSearch(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawEllipse(pen, 2, 2, 8, 8);
        g.DrawLine(pen, 9, 9, 14, 14);
    }

    private static void DrawSettings(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawEllipse(pen, 5, 5, 6, 6);
        for (int i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            g.DrawLine(pen,
                (int)(8 + 3.5f * (float)Math.Cos(a)), (int)(8 + 3.5f * (float)Math.Sin(a)),
                (int)(8 + 5.5f * (float)Math.Cos(a)), (int)(8 + 5.5f * (float)Math.Sin(a)));
        }
    }

    private static void DrawBookmark(Graphics g, Color c)
    {
        using var brush = new SolidBrush(c);
        g.FillPolygon(brush, new Point[] { new(3, 2), new(13, 2), new(13, 14), new(8, 10), new(3, 14) });
    }

    private static void DrawTerminal(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 1, 3, 14, 10);
        g.DrawLine(pen, 3, 7, 6, 7);
        g.DrawLine(pen, 6, 7, 3, 10);
        g.DrawLine(pen, 7, 10, 10, 10);
    }

    private static void DrawUp(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawLine(pen, 8, 13, 8, 4);
        g.DrawLine(pen, 8, 4, 4, 8);
        g.DrawLine(pen, 8, 4, 12, 8);
    }

    private static void DrawBack(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawLine(pen, 3, 8, 13, 8);
        g.DrawLine(pen, 3, 8, 7, 4);
        g.DrawLine(pen, 3, 8, 7, 12);
    }

    private static void DrawForward(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawLine(pen, 3, 8, 13, 8);
        g.DrawLine(pen, 13, 8, 9, 4);
        g.DrawLine(pen, 13, 8, 9, 12);
    }

    private static void DrawRoot(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 2, 5, 12, 8);
        g.DrawLine(pen, 2, 5, 5, 2);
        g.DrawLine(pen, 5, 2, 14, 2);
        g.DrawLine(pen, 14, 2, 14, 5);
    }

    private static void DrawHome(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawPolygon(pen, new Point[] { new(2, 8), new(8, 2), new(14, 8), new(14, 14), new(2, 14) });
        g.DrawLine(pen, 6, 14, 6, 10);
        g.DrawLine(pen, 10, 14, 10, 10);
        g.DrawLine(pen, 6, 10, 10, 10);
    }

    private static void DrawSelectAll(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 2, 2, 12, 12);
        using var b = new SolidBrush(Color.FromArgb(120, c));
        g.FillRectangle(b, 5, 5, 6, 6);
    }

    private static void DrawDeselectAll(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 2, 2, 12, 12);
    }

    private static void DrawInvert(Graphics g, Color c)
    {
        using var brush = new SolidBrush(c);
        g.FillRectangle(brush, 2, 2, 6, 6);
        g.FillRectangle(brush, 8, 8, 6, 6);
    }

    private static void DrawPack(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 3, 5, 10, 8);
        g.DrawRectangle(pen, 5, 3, 6, 2);
    }

    private static void DrawExtract(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 3, 5, 10, 8);
        g.DrawLine(pen, 8, 5, 8, 2);
        g.DrawLine(pen, 8, 2, 6, 4);
        g.DrawLine(pen, 8, 2, 10, 4);
    }

    private static void DrawSyncDirs(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawLine(pen, 3, 5, 13, 5);
        g.DrawLine(pen, 13, 5, 10, 2);
        g.DrawLine(pen, 13, 5, 10, 8);
        g.DrawLine(pen, 13, 11, 3, 11);
        g.DrawLine(pen, 3, 11, 6, 8);
        g.DrawLine(pen, 3, 11, 6, 14);
    }

    private static void DrawMultiRename(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 2, 3, 12, 3);
        g.DrawRectangle(pen, 2, 9, 12, 3);
        using var b = new SolidBrush(c);
        g.FillRectangle(b, 2, 3, 3, 3);
        g.FillRectangle(b, 2, 9, 3, 3);
    }

    private static void DrawExit(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 2, 3, 9, 10);
        g.DrawLine(pen, 12, 8, 15, 8);
        g.DrawLine(pen, 15, 8, 13, 6);
        g.DrawLine(pen, 15, 8, 13, 10);
    }

    private static void DrawProperties(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawEllipse(pen, 2, 2, 12, 12);
        g.DrawLine(pen, 8, 7, 8, 11);
        g.FillEllipse(new SolidBrush(c), 7, 5, 2, 2);
    }

    private static void DrawDrive(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 2, 5, 12, 7);
        using var b = new SolidBrush(c);
        g.FillEllipse(b, 10, 8, 2, 2);
        g.DrawLine(pen, 4, 8, 8, 8);
    }

    private static void DrawDriveFixed(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 2, 4, 12, 8);
        using var b = new SolidBrush(c);
        g.FillEllipse(b, 11, 7, 2, 2);
        g.DrawLine(pen, 4, 7, 9, 7);
        g.DrawLine(pen, 4, 10, 9, 10);
    }

    private static void DrawDriveRemovable(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 4, 2, 8, 12);
        using var b = new SolidBrush(c);
        g.FillRectangle(b, 5, 13, 6, 1);
        g.DrawLine(pen, 6, 5, 10, 5);
        g.DrawLine(pen, 6, 7, 10, 7);
    }

    private static void DrawDriveCdrom(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawEllipse(pen, 2, 2, 12, 12);
        g.DrawEllipse(pen, 6, 6, 4, 4);
        using var b = new SolidBrush(c);
        g.FillEllipse(b, 7, 7, 2, 2);
    }

    private static void DrawDriveNetwork(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawEllipse(pen, 2, 2, 12, 12);
        g.DrawLine(pen, 2, 8, 14, 8);
        g.DrawLine(pen, 8, 2, 8, 14);
    }

    private static void DrawDriveRam(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 2, 5, 12, 6);
        using var b = new SolidBrush(c);
        for (int i = 0; i < 3; i++)
            g.FillRectangle(b, 4 + i * 4, 3, 2, 2);
        g.DrawLine(pen, 4, 8, 12, 8);
    }

    private static void DrawGeneric(Graphics g, Color c)
    {
        using var pen = new Pen(c, 1f);
        g.DrawRectangle(pen, 2, 2, 12, 12);
    }
}
