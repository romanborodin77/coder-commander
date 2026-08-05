using CoderCommander.Services;
using System.Drawing.Drawing2D;

namespace CoderCommander.WinForms;

/// <summary>
/// Fully owner-drawn progress bar with theme support. Replaces the stock <see cref="ProgressBar"/>,
/// which under Windows visual styles ignores <see cref="Control.BackColor"/>/<see cref="Control.ForeColor"/>
/// entirely - <see cref="OperationDialogForm"/>'s copy/move progress bars stayed a native light-grey
/// trough with a native green fill on top of the dark theme regardless of those properties.
/// </summary>
public sealed class ThemedProgressBar : Control, ISelfThemedControl
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemedProgressBar"/> class with double buffering
    /// and theme change tracking.
    /// </summary>
    public ThemedProgressBar()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;

        ThemeService.ThemeChanged += OnThemeChanged;
    }

    /// <summary>Gets or sets the minimum value of the progress bar range.</summary>
    public int Minimum
    {
        get => _minimum;
        set { _minimum = value; Invalidate(); }
    }

    /// <summary>Gets or sets the maximum value of the progress bar range.</summary>
    public int Maximum
    {
        get => _maximum;
        set { _maximum = value; Invalidate(); }
    }

    /// <summary>Gets or sets the current progress value, clamped to the valid range.</summary>
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _minimum, Math.Max(_minimum, _maximum));
            if (_value == clamped) return;
            _value = clamped;
            Invalidate();
        }
    }

    /// <summary>Handles the <see cref="ThemeService.ThemeChanged"/> event by invalidating the control.</summary>
    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();

    /// <summary>Required by <see cref="ISelfThemedControl"/> - nothing to re-theme beyond a
    /// repaint, since every color is read live from <see cref="ThemeService.Current"/> in
    /// <see cref="OnPaint"/>.</summary>
    public void RefreshTheme() => Invalidate();

    /// <summary>Unsubscribes from the theme change event.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ThemeService.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    private int Scale(int px96) => (int)Math.Round(px96 * DeviceDpi / 96.0);

    /// <summary>Repaints the control when the parent DPI changes.</summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }

    /// <summary>Owner-draws the progress bar with a rounded trough and accent-colored fill.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var p = ThemeService.Current;
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var radius = Math.Max(1, Math.Min(rect.Height / 2, Scale(4)));

        using (var trackPath = GraphicsHelpers.GetRoundedRect(rect, radius))
        using (var trackBrush = new SolidBrush(p.PanelBackground))
            g.FillPath(trackBrush, trackPath);

        var range = _maximum - _minimum;
        var fraction = range <= 0 ? 0.0 : Math.Clamp((double)(_value - _minimum) / range, 0.0, 1.0);
        var fillWidth = (int)Math.Round(rect.Width * fraction);
        if (fillWidth <= 0) return;

        var fillRect = new Rectangle(rect.X, rect.Y, fillWidth, rect.Height);
        using var fillPath = GraphicsHelpers.GetRoundedRect(fillRect, radius);
        using var fillBrush = new SolidBrush(p.Accent);
        g.FillPath(fillBrush, fillPath);
    }
}
