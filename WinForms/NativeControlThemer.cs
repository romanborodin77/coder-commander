using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Win32 P/Invoke helpers that push the active theme into native chrome WinForms itself won't
/// touch: the window's immersive dark title bar, native scrollbars on controls that host their
/// own (TextBox, ListView, TreeView, ComboBox...), and a ListView's native column-header strip.
///
/// Centralizes what used to be duplicated between <see cref="ThemedForm"/> and
/// <see cref="Views.MainForm"/> (dark title bar, dark scrollbars) so both call the same code
/// instead of each re-implementing the DllImport calls.
/// </summary>
public static class NativeControlThemer
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int LVM_GETHEADER = 0x101F;
    private const int LVM_SETEXTENDEDLISTVIEWSTYLE = 0x1036;
    private const int LVS_EX_DOUBLEBUFFER = 0x00010000;

    /// <summary>Applies (or removes) the immersive dark title bar on a top-level window.</summary>
    public static void ApplyDarkTitleBar(IntPtr handle)
    {
        var value = ThemeService.IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    /// <summary>
    /// Switches a control's native scrollbars (and any other "explorer" visual-style chrome
    /// it draws itself, like ComboBox drop shadows) between dark and light. Defers until the
    /// handle exists if it doesn't yet.
    /// </summary>
    public static void ApplyDarkScrollbars(Control c)
    {
        if (c.IsHandleCreated)
        {
            SetWindowTheme(c.Handle, ThemeService.IsDark ? "DarkMode_Explorer" : "Explorer", null);

            // Some composite controls implement part of their native chrome as a separate child
            // Control with its own HWND that theming the parent's handle doesn't reach - e.g.
            // NumericUpDown's up/down spin buttons (UpDownBase.UpDownButtons), the same story as
            // ListView's column-header window (see ThemeListViewHeader). Recursing here themes
            // that child too; it's a harmless no-op for controls with no children of their own
            // (TextBox, ComboBox, ...).
            foreach (Control child in c.Controls)
                ApplyDarkScrollbars(child);
        }
        else
        {
            c.HandleCreated += OnControlHandleCreated;
        }
    }

    private static void OnControlHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control c)
        {
            c.HandleCreated -= OnControlHandleCreated;
            ApplyDarkScrollbars(c);
        }
    }

    /// <summary>
    /// Native dark-mode treatment for a <see cref="ListView"/>: dark scrollbars on the list
    /// itself, dark scrollbars/chrome on its native column-header child (which
    /// <see cref="ApplyDarkScrollbars(Control)"/> alone does not reach - the header is a
    /// separate native window, not a WinForms <see cref="Control"/>), and double-buffering to
    /// cut down on flicker. Safe to call on any ListView, including one that already fully
    /// owner-draws itself (like <see cref="Views.FilePanelUserControl"/>'s file list, which has
    /// called this since before <see cref="ThemeListViewHeader"/> existed) - it touches native
    /// chrome only and does not subscribe to any Draw* event.
    /// </summary>
    public static void ThemeListView(ListView lv)
    {
        if (lv.IsHandleCreated)
            ThemeListViewCore(lv);
        else
            lv.HandleCreated += OnListViewHandleCreated;
    }

    private static void OnListViewHandleCreated(object? sender, EventArgs e)
    {
        if (sender is ListView lv)
        {
            lv.HandleCreated -= OnListViewHandleCreated;
            ThemeListViewCore(lv);
        }
    }

    private static void ThemeListViewCore(ListView lv)
    {
        // wParam=0 mirrors the call FilePanelUserControl already made for the main file list
        // (LVM_SETEXTENDEDLISTVIEWSTYLE's wParam is a mask; 0 means "set exactly what's in
        // lParam") - kept identical here rather than "corrected" to avoid changing behavior
        // that's already shipping.
        SendMessage(lv.Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, (IntPtr)LVS_EX_DOUBLEBUFFER);
        var mode = ThemeService.IsDark ? "DarkMode_Explorer" : "Explorer";
        SetWindowTheme(lv.Handle, mode, null);

        var headerHandle = SendMessage(lv.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        if (headerHandle != IntPtr.Zero)
            SetWindowTheme(headerHandle, mode, null);
    }

    /// <summary>
    /// Owner-draws just the header row of a <b>plain</b> <see cref="ListView"/> from
    /// <see cref="ThemePalette"/> (row/subitem painting is left at <c>DrawDefault = true</c>,
    /// which already paints correctly from <see cref="ListView.BackColor"/>/
    /// <see cref="ListView.ForeColor"/>). This exists because
    /// <see cref="SetWindowTheme(IntPtr, string, string?)"/>(headerHandle, "DarkMode_Explorer",
    /// ...) alone does *not* reliably darken the header's background on every Windows build - it
    /// themes the sort-arrow glyph and some chrome but the header cells themselves can stay
    /// system-light. Used by <see cref="ControlThemer"/> for every dialog ListView (built via
    /// <see cref="UiHelpers.CreateListView"/> or by hand).
    ///
    /// <b>Do not call this on a ListView that already owner-draws its own rows</b> - i.e.
    /// <see cref="Views.FilePanelUserControl"/>'s file list, which has its own
    /// <c>DrawItem</c>/<c>DrawSubItem</c>/<c>DrawColumnHeader</c> handlers. This method's
    /// <see cref="DrawDefaultItem"/>/<see cref="DrawDefaultSubItem"/> handlers would run
    /// alongside those and flip <c>DrawDefault</c> back to <c>true</c> after the file list's own
    /// handlers set it to <c>false</c>, making the OS paint its own row/selection chrome on top
    /// of the already custom-painted row - visible as doubled/ghosted text on hover and selected
    /// rows. Call only <see cref="ThemeListView"/> for a ListView that owner-draws itself.
    /// </summary>
    public static void ThemeListViewHeader(ListView lv)
    {
        if (lv.IsHandleCreated)
            ThemeListViewHeaderCore(lv);
        else
            lv.HandleCreated += OnListViewHeaderHandleCreated;
    }

    private static void OnListViewHeaderHandleCreated(object? sender, EventArgs e)
    {
        if (sender is ListView lv)
        {
            lv.HandleCreated -= OnListViewHeaderHandleCreated;
            ThemeListViewHeaderCore(lv);
        }
    }

    private static void ThemeListViewHeaderCore(ListView lv)
    {
        // This runs again on every theme switch (called from ControlThemer.ThemeSingleControl),
        // so guard against piling up duplicate subscriptions the same way ControlThemer's own
        // TabControl case does.
        lv.OwnerDraw = true;
        lv.DrawColumnHeader -= DrawThemedColumnHeader;
        lv.DrawColumnHeader += DrawThemedColumnHeader;
        lv.DrawItem -= DrawDefaultItem;
        lv.DrawItem += DrawDefaultItem;
        lv.DrawSubItem -= DrawDefaultSubItem;
        lv.DrawSubItem += DrawDefaultSubItem;

        // DrawColumnHeader only fires once per actual ColumnHeader - the leftover strip to the
        // right of the last column (when the columns don't add up to the full width, which is
        // the common case for a dialog's fixed-width columns) is native header background that
        // no managed event ever repaints, so it stays system-light regardless of the above.
        // Stretching the last column to consume the remaining width - the same fix
        // Views.FilePanelUserControl already uses for its own file list - removes the dead strip
        // entirely instead of trying to paint over something we can't reach.
        //
        // Subscribed to BOTH Resize and ClientSizeChanged: toggling a native scrollbar changes
        // the client rect via SetWindowPos(SWP_FRAMECHANGED) without reliably raising Resize, so
        // Resize alone can miss the moment the last column actually needs to shrink/grow.
        lv.Resize -= FillLastColumnWidthHandler;
        lv.Resize += FillLastColumnWidthHandler;
        lv.ClientSizeChanged -= FillLastColumnWidthHandler;
        lv.ClientSizeChanged += FillLastColumnWidthHandler;
        RefitLastColumn(lv);

        lv.Invalidate();
    }

    private static void FillLastColumnWidthHandler(object? sender, EventArgs e)
    {
        if (sender is ListView lv) RefitLastColumn(lv);
    }

    /// <summary>
    /// Stretches a ListView's last column to consume the client width not already taken by the
    /// others. Public so <see cref="ListViewScrollbarOverlay.NativeMetricsChanged"/> subscribers
    /// (a dialog's overlay re-measuring the native scrollbar footprint) can trigger the same
    /// re-fit that <see cref="Views.FilePanelUserControl"/> does for its own file list.
    /// </summary>
    public static void RefitLastColumn(ListView lv)
    {
        // Never commit a pre-layout measurement (ClientSize.Width still at the WinForms default
        // before the parent's first layout pass, or the handle not created yet) - doing so used
        // to clamp the last column to the 45px floor and leave a wide native strip permanently
        // uncorrected once real Resize/ClientSizeChanged events stopped firing for it.
        if (!lv.IsHandleCreated || lv.Columns.Count == 0) return;
        var clientWidth = lv.ClientSize.Width;
        if (clientWidth <= 0) return;

        var totalWidth = 0;
        for (var i = 0; i < lv.Columns.Count - 1; i++)
            totalWidth += lv.Columns[i].Width;

        // -1: don't let the column sum land exactly on ClientSize.Width - that's what makes a
        // ListView decide it needs its own horizontal scrollbar even though the columns were
        // meant to fit exactly (same reasoning as Views.FilePanelUserControl.FillLastColumnWidth).
        var remainingWidth = clientWidth - totalWidth - 1;
        // Too little room to give the last column a sane width - leave columns alone rather than
        // clamp to a 45px floor that would itself misrepresent the available space.
        if (remainingWidth < 45) return;

        var lastColumn = lv.Columns[lv.Columns.Count - 1];
        if (lastColumn.Width != remainingWidth) lastColumn.Width = remainingWidth;
    }

    private static void DrawThemedColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        var p = ThemeService.Current;
        var rect = e.Bounds;

        using (var bg = new LinearGradientBrush(rect, p.ColumnHeaderGradient, p.HeaderBackground, 90f))
            e.Graphics.FillRectangle(bg, rect);

        using (var bottom = new Pen(p.GridLine))
            e.Graphics.DrawLine(bottom, rect.X, rect.Bottom - 1, rect.Right, rect.Bottom - 1);

        if (e.Header == null || e.ColumnIndex < 0)
            return;

        var flags = e.Header.TextAlign switch
        {
            HorizontalAlignment.Right => TextFormatFlags.Right,
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            _ => TextFormatFlags.Left,
        } | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

        var textRect = new Rectangle(rect.X + 6, rect.Y, rect.Width - 8, rect.Height);
        TextRenderer.DrawText(e.Graphics, e.Header.Text, p.GridFontBold, textRect, p.HeaderForeground, flags);

        using var sep = new Pen(p.GridLine);
        e.Graphics.DrawLine(sep, rect.Right - 1, rect.Y + 3, rect.Right - 1, rect.Bottom - 3);
    }

    // ListView.OwnerDraw requires all three Draw* events to be handled; DrawDefault = true falls
    // back to normal row/subitem painting, which already reads the right colors from
    // ListView.BackColor/ForeColor - only the header actually needed owner-drawing.
    private static void DrawDefaultItem(object? sender, DrawListViewItemEventArgs e) => e.DrawDefault = true;
    private static void DrawDefaultSubItem(object? sender, DrawListViewSubItemEventArgs e) => e.DrawDefault = true;
}
