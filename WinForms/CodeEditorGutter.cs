using CoderCommander.Services;
using System.Globalization;

namespace CoderCommander.WinForms;

/// <summary>
/// Line-number gutter for <see cref="CodeEditorControl"/>. Reads scroll/caret state directly off
/// the <see cref="CodeEditorCanvas"/> it's paired with — no duplicated state, single source of
/// truth for where the viewport currently is.
/// </summary>
internal sealed class CodeEditorGutter : Control
{
    private readonly CodeEditorCanvas _canvas;
    // CA2213: _font is shared from ThemeService — not owned, not disposed here.
#pragma warning disable CA2213
    private Font _font = null!;
#pragma warning restore CA2213

    private const int HorizontalPadding = 10;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeEditorGutter"/> class, pairing it with the
    /// specified canvas for scroll/caret synchronization.
    /// </summary>
    /// <param name="canvas">The editor canvas whose viewport state drives line-number rendering.</param>
    public CodeEditorGutter(CodeEditorCanvas canvas)
    {
        _canvas = canvas;

        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Left;
        Cursor = Cursors.Default;
        TabStop = false;

        _canvas.ScrollChanged += (_, _) => Invalidate();
        _canvas.CaretMoved += (_, _) => Invalidate();
        _canvas.ContentChanged += (_, _) =>
        {
            RecalculateWidth();
            Invalidate();
        };

        ApplyTheme();
    }

    /// <summary>Applies the current theme colors and font to the gutter.</summary>
    public void ApplyTheme()
    {
        var p = ThemeService.Current;
        BackColor = p.PanelBackground;
        _font = p.MonoFont;
        RecalculateWidth();
        Invalidate();
    }

    private void RecalculateWidth()
    {
        var digits = Math.Max(2, _canvas.Buffer.LineCount.ToString(CultureInfo.InvariantCulture).Length);
        var textWidth = TextRenderer.MeasureText(new string('9', digits), _font, Size.Empty, TextFormatFlags.NoPadding).Width;
        var newWidth = textWidth + HorizontalPadding * 2;
        if (Width != newWidth) Width = newWidth;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // _font is shared from ThemeService — not owned, must not be disposed here.
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = ThemeService.Current;
        g.Clear(p.PanelBackground);

        var lineHeight = _canvas.LineHeight;
        if (lineHeight <= 0) return;

        var scrollY = _canvas.ScrollY;
        var firstLine = Math.Max(0, scrollY / lineHeight);
        var visibleRows = ClientSize.Height / lineHeight + 2;
        var lastLine = Math.Min(_canvas.Buffer.LineCount - 1, firstLine + visibleRows);
        var caretLine = _canvas.Caret.Line;
        var textRect = new Rectangle(0, 0, ClientSize.Width - HorizontalPadding, lineHeight);

        for (var line = firstLine; line <= lastLine; line++)
        {
            var y = line * lineHeight - scrollY;
            textRect.Y = y;
            var color = line == caretLine ? p.Foreground : p.DimForeground;
            TextRenderer.DrawText(g, (line + 1).ToString(CultureInfo.InvariantCulture), _font, textRect, color,
                TextFormatFlags.NoPadding | TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
