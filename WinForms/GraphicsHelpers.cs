using System.Drawing.Drawing2D;

namespace CoderCommander.WinForms;

/// <summary>
/// Small GDI+ drawing helpers shared by every owner-drawn control in the app. Used to be copied
/// four times (RoundedButton, ThemedCheckBox, ThemeRenderer, DriveBarRenderer) with identical
/// bodies - kept here once so a change to the corner-rounding math only has one place to make it.
/// </summary>
internal static class GraphicsHelpers
{
    public static GraphicsPath GetRoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0)
            return path;

        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
