using CoderCommander.Services;
using System.Drawing.Drawing2D;

namespace CoderCommander.WinForms;

/// <summary>
/// Fully owner-drawn button with rounded corners, gradient fill, focus ring, and shadow support.
/// Supports primary/secondary/danger color roles via <see cref="Role"/>.
/// </summary>
public class RoundedButton : Button
{
    private bool _hover;
    private bool _pressed;
    private bool _focused;
    private EventHandler? _themeChangedHandler;

    /// <summary>Gets or sets the hover highlight color. Falls back to <see cref="ThemePalette.ToolbarHover"/> if empty.</summary>
    public Color HoverColor { get; set; } = Color.Empty;
    /// <summary>Gets or sets the pressed state color. Falls back to <see cref="ThemePalette.ToolbarHover"/> if empty.</summary>
    public Color PressedColor { get; set; } = Color.Empty;
    /// <summary>Gets or sets the border color. No border is drawn if empty.</summary>
    public Color BorderColor { get; set; } = Color.Empty;
    /// <summary>Gets or sets the border width in pixels. No border is drawn if zero.</summary>
    public int BorderWidth { get; set; } = 0;
    /// <summary>Gets or sets the corner radius for the rounded rectangle shape.</summary>
    public int CornerRadius { get; set; } = 4;
    /// <summary>Gets or sets whether a vertical gradient is applied to the background.</summary>
    public bool UseGradient { get; set; } = true;
    /// <summary>Gets or sets the custom gradient top color. Auto-generated if empty.</summary>
    public Color GradientTopColor { get; set; } = Color.Empty;
    /// <summary>Gets or sets the custom gradient bottom color. Auto-generated if empty.</summary>
    public Color GradientBottomColor { get; set; } = Color.Empty;
    /// <summary>Gets or sets whether a drop shadow is drawn beneath the button.</summary>
    public bool DrawShadow { get; set; } = false;
    /// <summary>Gets or sets the shadow color (semi-transparent black by default).</summary>
    public Color ShadowColor { get; set; } = Color.FromArgb(48, 0, 0, 0);
    /// <summary>Gets or sets the shadow offset in pixels.</summary>
    public int ShadowOffset { get; set; } = 2;
    /// <summary>Gets or sets the shadow blur radius.</summary>
    public int ShadowBlur { get; set; } = 4;

    /// <summary>
    /// Which button color scheme <see cref="ControlThemer"/> should apply on every theme
    /// switch (Primary/Secondary/Danger). Null means "use the plain secondary scheme" - the
    /// same as before this existed, so hand-rolled RoundedButtons keep working unchanged.
    /// Set by <see cref="ThemedForm.CreateThemedButton"/>; assign directly for a button built
    /// by hand.
    /// </summary>
    public ThemeRole? Role { get; set; }

    /// <summary>Fired on right mouse button down. Standard Click only fires for left button.</summary>
    public event MouseEventHandler? RightClick;

    /// <summary>Initializes a new <see cref="RoundedButton"/> with flat style, transparent hover colors, and theme tracking.</summary>
    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);

        _themeChangedHandler = (s, e) => Invalidate();
        ThemeService.ThemeChanged += _themeChangedHandler;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
            RightClick?.Invoke(this, e);
    }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { _focused = true; Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _focused = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnter(EventArgs e) { _focused = true; Invalidate(); base.OnEnter(e); }
    protected override void OnLeave(EventArgs e) { _focused = false; Invalidate(); base.OnLeave(e); }

    protected override bool ShowFocusCues => false;

    /// <summary>Unsubscribes from the theme change event on disposal.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _themeChangedHandler != null)
            ThemeService.ThemeChanged -= _themeChangedHandler;
        base.Dispose(disposing);
    }

    /// <summary>Owner-draws the button with rounded rectangle, gradient, border, highlight, focus ring, and text/image layout.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        var radius = CornerRadius;

        // When BackColor is left at its default (Color.Empty → transparent), fall back to the
        // parent surface so we never paint a hole through layered control hierarchies.
        var clearColor = BackColor.IsEmpty || BackColor == Color.Transparent
            ? (Parent?.BackColor is Color pcol && !pcol.IsEmpty && pcol != Color.Transparent
                ? pcol
                : ThemeService.Current.PanelBackground)
            : BackColor;

        // Clear entire area first to avoid transparent-corner artifacts
        using (var clearBrush = new SolidBrush(clearColor))
            g.FillRectangle(clearBrush, ClientRectangle);

        // A disabled button must look disabled. This control used to paint identically whether
        // Enabled was true or false, so six places across the app that disable a button - the
        // operation dialog's Pause, the About dialog's Copy-info during its confirmation, the
        // Connections dialog's Add when no provider can serve one - all offered a button that
        // looked perfectly clickable and silently did nothing.
        var enabled = Enabled;

        Color baseColor;
        if (!enabled)
            baseColor = BackColor;
        else if (_pressed)
            baseColor = PressedColor != Color.Empty ? PressedColor : ThemeService.Current.ToolbarHover;
        else if (_hover)
            baseColor = HoverColor != Color.Empty ? HoverColor : ThemeService.Current.ToolbarHover;
        else
            baseColor = BackColor;

        var topColor = GradientTopColor != Color.Empty ? GradientTopColor : ControlPaint.Light(baseColor, 0.08f);
        var bottomColor = GradientBottomColor != Color.Empty ? GradientBottomColor : ControlPaint.Dark(baseColor, 0.04f);

        // Background
        var path = GraphicsHelpers.GetRoundedRect(rect, radius);
        if (UseGradient)
        {
            using var gradBrush = new LinearGradientBrush(rect, topColor, bottomColor, 90f);
            g.FillPath(gradBrush, path);
        }
        else
        {
            using var bgBrush = new SolidBrush(baseColor);
            g.FillPath(bgBrush, path);
        }

        // Border
        if (BorderWidth > 0 && BorderColor != Color.Empty)
        {
            using var borderPen = new Pen(enabled ? BorderColor : ThemeService.Current.GridLine, BorderWidth);
            g.DrawPath(borderPen, path);
        }

        // Highlight top edge (subtle 3D effect). GlossOverlay is white-on-dark / black-on-light
        // so this reads as a highlight in both themes instead of a hardcoded white wash that
        // looks wrong against a light BackColor.
        if (UseGradient && !_pressed)
        {
            var highlightRect = new Rectangle(rect.X + radius / 2, rect.Y + 1, rect.Width - radius, rect.Height / 2 - 1);
            using var highlightBrush = new SolidBrush(ThemeService.Current.GlossOverlay);
            var highlightPath = GraphicsHelpers.GetRoundedRect(highlightRect, Math.Max(0, radius - 1));
            g.FillPath(highlightBrush, highlightPath);
            highlightPath.Dispose();
        }

        // Focus ring
        if (_focused && BorderWidth > 0)
        {
            using var focusPen = new Pen(ThemeService.Current.Accent, 1f);
            var focusRect = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
            var focusPath = GraphicsHelpers.GetRoundedRect(focusRect, Math.Max(0, radius - 2));
            g.DrawPath(focusPen, focusPath);
            focusPath.Dispose();
        }

        path.Dispose();

        // Grey text is the conventional disabled cue, and the only one available here -
        // the background stays the button's own colour so the shape doesn't jump.
        var textColor = enabled ? ForeColor : ThemeService.Current.DimForeground;

        var textRect = new Rectangle(
            Padding.Left,
            Padding.Top,
            Width - Padding.Left - Padding.Right,
            Height - Padding.Top - Padding.Bottom);

        if (Image != null && !string.IsNullOrEmpty(Text))
        {
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var textW = TextRenderer.MeasureText(g, Text, Font, new Size(200, Height), flags).Width;
            var totalW = Image.Width + 6 + textW;

            if (totalW <= textRect.Width)
            {
                var startX = textRect.X + (textRect.Width - totalW) / 2;
                var imgY = textRect.Y + (textRect.Height - Image.Height) / 2;
                g.DrawImage(Image, startX, imgY, Image.Width, Image.Height);
                var tRect = new Rectangle(startX + Image.Width + 6, textRect.Y, textW + 2, textRect.Height);
                TextRenderer.DrawText(g, Text, Font, tRect, textColor, flags);
            }
            else
            {
                var imgY = textRect.Y + (textRect.Height - Image.Height) / 2;
                g.DrawImage(Image, textRect.X + 4, imgY, Image.Width, Image.Height);
                var tRect = new Rectangle(textRect.X + Image.Width + 8, textRect.Y, textRect.Width - Image.Width - 12, textRect.Height);
                TextRenderer.DrawText(g, Text, Font, tRect, textColor, flags);
            }
        }
        else if (Image != null)
        {
            var imgX = textRect.X + (textRect.Width - Image.Width) / 2;
            var imgY = textRect.Y + (textRect.Height - Image.Height) / 2;
            g.DrawImage(Image, imgX, imgY, Image.Width, Image.Height);
        }
        else
        {
            var centerFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, Text, Font, textRect, textColor, centerFlags);
        }
    }

}

/// <summary>
/// Fully owner-drawn check box with vector checkmark and 3D effect.
/// </summary>
public sealed class ThemedCheckBox : Control
{
    private bool _hover;
    private bool _pressed;
    private CheckState _state = CheckState.Unchecked;

    public enum CheckState { Unchecked, Checked, Indeterminate }

    /// <summary>Initializes a new <see cref="ThemedCheckBox"/> with double buffering and theme tracking.</summary>
    public ThemedCheckBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Opaque,
            true);
        Cursor = Cursors.Hand;
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    /// <summary>Handles the <see cref="ThemeService.ThemeChanged"/> event by invalidating the control.</summary>
    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();

    /// <summary>Unsubscribes from the theme change event.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ThemeService.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    /// <summary>Enables a third (indeterminate) state; click cycles through all three.</summary>
    public bool ThreeState { get; set; }

    /// <summary>Gets or sets whether the checkbox is checked (convenience wrapper around <see cref="State"/>).</summary>
    public bool Checked
    {
        get => _state == CheckState.Checked;
        set => SetState(value ? CheckState.Checked : CheckState.Unchecked);
    }

    /// <summary>Gets or sets the check state (Unchecked, Checked, or Indeterminate).</summary>
    public CheckState State
    {
        get => _state;
        set => SetState(value);
    }

    /// <summary>Sets the check state, invalidates, and raises <see cref="CheckedChanged"/>.</summary>
    private void SetState(CheckState value)
    {
        if (_state == value) return;
        _state = value;
        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the check state changes.</summary>
    public event EventHandler? CheckedChanged;

    /// <summary>
    /// Lets this control size itself correctly when placed with <c>AutoSize = true</c> in a
    /// FlowLayoutPanel/TableLayoutPanel, instead of falling back to Control's generic default
    /// size (which has no relationship to the actual checkbox+label content and either clips
    /// the text or leaves excess empty space). Mirrors the box/gap geometry OnPaint uses below.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var textSize = TextRenderer.MeasureText(Text, Font);
        const int boxSize = 16, boxX = 2, textGap = 8, rightMargin = 10;
        var width = boxX + boxSize + textGap + textSize.Width + rightMargin;
        var height = Math.Max(boxSize + 8, textSize.Height + 8);
        return new Size(width, height);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (ThreeState)
        {
            _state = _state switch
            {
                CheckState.Unchecked => CheckState.Checked,
                CheckState.Checked => CheckState.Indeterminate,
                _ => CheckState.Unchecked
            };
        }
        else
        {
            _state = _state == CheckState.Checked ? CheckState.Unchecked : CheckState.Checked;
        }
        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var p = ThemeService.Current;
        var rect = ClientRectangle;

        // Solid background
        using (var bgBrush = new SolidBrush(BackColor))
            g.FillRectangle(bgBrush, rect);

        const int boxSize = 16;
        const int radius = 3;
        var boxRect = new Rectangle(2, (rect.Height - boxSize) / 2, boxSize, boxSize);

        // Same hover/pressed color role as RoundedButton's default (ToolbarHover for both) -
        // previously this used HeaderBackground for hover, a different role than every other
        // themed button in the app.
        var baseColor = _pressed || _hover ? p.ToolbarHover : p.PanelBackground;
        var topColor = ControlPaint.Light(baseColor, 0.06f);
        var bottomColor = ControlPaint.Dark(baseColor, 0.03f);

        // Box shadow - always dark regardless of theme, like a real drop shadow.
        using (var shadowPath = GraphicsHelpers.GetRoundedRect(new Rectangle(boxRect.X + 1, boxRect.Y + 1, boxRect.Width, boxRect.Height), radius))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
            g.FillPath(shadowBrush, shadowPath);

        // Box background
        using (var boxPath = GraphicsHelpers.GetRoundedRect(boxRect, radius))
        using (var boxGrad = new LinearGradientBrush(boxRect, topColor, bottomColor, 90f))
            g.FillPath(boxGrad, boxPath);

        // Box border
        using (var boxPath2 = GraphicsHelpers.GetRoundedRect(boxRect, radius))
        using (var boxBorderPen = new Pen(_hover ? p.Accent : p.GridLine, _hover ? 2 : 1))
            g.DrawPath(boxBorderPen, boxPath2);

        // Checkmark (Checked) or indeterminate marker
        if (_state == CheckState.Checked)
        {
            var cx = boxRect.X + boxRect.Width / 2f;
            var cy = boxRect.Y + boxRect.Height / 2f;
            using var checkPath = new GraphicsPath();
            checkPath.AddLines(new[]
            {
                new PointF(cx - 5, cy - 1),
                new PointF(cx - 2, cy + 4),
                new PointF(cx + 5, cy - 4)
            });
            using var checkPen = new Pen(p.Accent, 2.5f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };
            g.DrawPath(checkPen, checkPath);
        }
        else if (_state == CheckState.Indeterminate)
        {
            var inset = new RectangleF(boxRect.X + 4, boxRect.Y + 4, boxRect.Width - 8, boxRect.Height - 8);
            using var indBrush = new SolidBrush(p.Accent);
            g.FillRectangle(indBrush, inset);
        }

        // Text
        var textRect = new Rectangle(boxRect.Right + 8, 0, Math.Max(0, rect.Width - boxRect.Right - 10), rect.Height);
        TextRenderer.DrawText(g, Text, Font, textRect, p.Foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

}

/// <summary>
/// Static UI helpers for creating themed controls.
/// </summary>
public static class UiHelpers
{
    /// <summary>Creates a themed button via <see cref="ThemedForm.CreateThemedButton"/>.</summary>
    public static Button CreateButton(string text, bool accent = false, string? name = null)
    {
        // Delegate to the canonical themed button factory to keep theming consistent.
        // (Used by legacy call sites — prefer ThemedForm.CreateThemedButton in new code.)
        return ThemedForm.CreateThemedButton(text, accent, name);
    }

    /// <summary>No-op kept for backward compatibility; <see cref="RoundedButton"/> handles its own painting.</summary>
    internal static void ApplyRoundedRegion(Control c, int? radius = null)
    {
        // Kept for backward compat — RoundedButton handles its own painting
    }

    /// <summary>Creates a themed label with optional bold font via <see cref="ThemeRole"/> tag.</summary>
    public static Label CreateLabel(string text, bool bold = false, string? name = null)
    {
        var p = ThemeService.Current;
        return new Label
        {
            Name = name ?? "",
            Text = text,
            Font = bold ? p.HeaderFont : p.GridFont,
            ForeColor = p.Foreground,
            BackColor = Color.Transparent,
            AutoSize = true,
            // Without this, ControlThemer's untagged-Label default would flip a bold label's
            // ForeColor to HeaderForeground on the next theme switch - every caller of
            // CreateLabel(bold: true) actually wants it to stay Foreground, matching what's set
            // above.
            Tag = bold ? ThemeRole.Emphasis : ThemeRole.Body
        };
    }

    /// <summary>Creates a themed text box with the current theme font and colors.</summary>
    public static TextBox CreateTextBox(string? value = null, string? name = null)
    {
        var p = ThemeService.Current;
        return new TextBox
        {
            Name = name ?? "",
            Text = value ?? "",
            Font = p.GridFont,
            BackColor = p.PanelBackground,
            ForeColor = p.Foreground,
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    /// <summary>Creates a themed detail-view ListView with the specified columns.</summary>
    /// <remarks><c>params</c> must be the last parameter, so unlike the other <c>Create*</c>
    /// factories there is no leading optional <c>name</c> here - set <see cref="Control.Name"/> on
    /// the returned instance directly when a call site needs a stable <c>AutomationId</c>.</remarks>
    public static ListView CreateListView(params (string name, int width)[] columns)
    {
        var p = ThemeService.Current;
        var lv = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            Font = p.GridFont,
            BackColor = p.PanelBackground,
            ForeColor = p.Foreground
        };
        foreach (var (name, width) in columns)
            lv.Columns.Add(name, width);
        return lv;
    }

    /// <summary>Formats a byte count into a human-readable string (e.g. "1.5 MB").</summary>
    public static string FormatSize(long bytes) => CoderCommander.Utils.FormatUtils.FormatSize(bytes);

    /// <summary>Creates a themed checkbox with the specified text and initial state.</summary>
    public static ThemedCheckBox CreateCheckBox(string text, bool checked_ = false, string? name = null)
    {
        var p = ThemeService.Current;
        return new ThemedCheckBox
        {
            Name = name ?? "",
            Text = text,
            Font = p.GridFont,
            ForeColor = p.Foreground,
            BackColor = p.Background,
            Checked = checked_,
            Height = 32
        };
    }

    /// <summary>
    /// Wires a <see cref="ContextMenuStrip"/> built fresh for a single show (never stored in a
    /// field) to dispose itself once closed - the safe version of <c>menu.Closed += (_, _) =>
    /// menu.Dispose();</c>. Disposing directly inside the <c>Closed</c> handler is NOT safe:
    /// <c>Closed</c> fires partway through <c>ToolStripDropDown.SetVisibleCore(false)</c>, which
    /// still touches <c>Handle</c> afterward (to finish tearing down the dropdown window) -
    /// disposing first makes that access throw <see cref="ObjectDisposedException"/> straight out
    /// of the message loop. Hit twice with two different stack traces: dismissing via a menu item
    /// click (<c>ToolStripDropDown.OnItemClicked</c>) and dismissing by clicking elsewhere
    /// (<c>ToolStripManager.ModalMenuFilter.CloseActiveDropDown</c>) - both call
    /// <c>SetVisibleCore(false)</c> the same way, so both need this deferral, not just one.
    /// Posting the actual <c>Dispose()</c> through <paramref name="host"/>'s <c>BeginInvoke</c>
    /// lets <c>SetVisibleCore</c> finish unwinding first; <paramref name="host"/> must be a
    /// control that stays alive at least as long as the menu (the panel/canvas that owns it, not
    /// the menu itself - its own handle is mid-teardown at the point <c>Closed</c> fires).
    /// </summary>
    public static void AutoDisposeOnClose(ContextMenuStrip menu, Control host)
    {
        menu.Closed += (_, _) =>
        {
            if (host.IsDisposed || !host.IsHandleCreated)
                return;
            host.BeginInvoke(new Action(() =>
            {
                if (!menu.IsDisposed) menu.Dispose();
            }));
        };
    }
}

/// <summary>
/// Message box result enumeration.
/// </summary>
public enum MsgBoxResult { None, OK, Cancel, Yes, No }

/// <summary>
/// Message box icon enumeration.
/// </summary>
public enum MsgBoxIcon { None, Information, Warning, Error, Question }

/// <summary>
/// Message box buttons enumeration.
/// </summary>
public enum MsgBoxButtons { OK, OKCancel, YesNo, YesNoCancel }

/// <summary>
/// Themed message box replacement for standard MessageBox.
/// </summary>
public static class StyledMessageBox
{
    /// <summary>Displays a themed message box with the specified text, caption, buttons, and icon.</summary>
    public static MsgBoxResult Show(string text, string caption, MsgBoxButtons buttons = MsgBoxButtons.OK, MsgBoxIcon icon = MsgBoxIcon.None, Form? owner = null)
    {
        using var form = new StyledMessageBoxForm(text, caption, buttons, icon, owner != null);
        var result = form.ShowDialog(owner);
        return result switch
        {
            DialogResult.OK => MsgBoxResult.OK,
            DialogResult.Cancel => MsgBoxResult.Cancel,
            DialogResult.Yes => MsgBoxResult.Yes,
            DialogResult.No => MsgBoxResult.No,
            _ => MsgBoxResult.None
        };
    }
}

/// <summary>
/// Backing form for <see cref="StyledMessageBox"/>. A real <see cref="ThemedForm"/> instead of a
/// bare <see cref="Form"/> built by hand - the previous implementation never got the immersive
/// dark title bar or live re-theming that every other dialog in the app gets, so it showed a
/// light Windows title bar over a dark body in dark mode.
/// </summary>
internal sealed class StyledMessageBoxForm : ThemedForm
{
    private const int IconColumnWidth = 56;
    private const int MessageWrapWidth = 400;

    private readonly Label _iconLabel;
    private readonly MsgBoxIcon _icon;

    /// <summary>
    /// Initializes the message box form with icon, message, and button layout based on the
    /// specified <see cref="MsgBoxButtons"/> and <see cref="MsgBoxIcon"/>.
    /// </summary>
    public StyledMessageBoxForm(string text, string caption, MsgBoxButtons buttons, MsgBoxIcon icon, bool hasOwner)
    {
        var p = ThemeService.Current;
        var L = LocalizationService.Current;
        _icon = icon;

        Text = caption;
        StartPosition = hasOwner ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
        Width = 480;
        Height = 220;

        // Icon + message laid out with explicit TableLayoutPanel columns rather than
        // Dock.Left + Dock.Fill - keeps the layout correct regardless of Controls.Add order
        // (see the "docks from HIGHEST index down to 0" rule documented on MainForm) instead of
        // relying on getting that order exactly right.
        var contentGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = p.Background
        };
        contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IconColumnWidth));
        contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        contentGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // ForeColor was never set here before - the glyph fell back to Label's plain default
        // (SystemColors.ControlText), rendering as a flat, unthemed gray regardless of icon
        // severity instead of the Warning/Danger/Accent color a message box icon normally
        // conveys. Actual coloring happens in ApplyTheme() below, not here - see its doc comment
        // for why.
        _iconLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Text = icon switch
            {
                MsgBoxIcon.Information => "\u2139",
                MsgBoxIcon.Warning => "\u26A0",
                MsgBoxIcon.Error => "\u2715",
                MsgBoxIcon.Question => "?",
                _ => ""
            }
        };

        var msgLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            Font = p.GridFont,
            ForeColor = p.Foreground,
            BackColor = Color.Transparent,
            AutoSize = false,
            Padding = new Padding(12, 24, 20, 24)
        };

        contentGrid.Controls.Add(_iconLabel, 0, 0);
        contentGrid.Controls.Add(msgLabel, 1, 0);

        RoundedButton MakeButton(string key, bool accent, DialogResult result)
        {
            var btn = (RoundedButton)CreateThemedButton(L.GetString(key), accent);
            btn.DialogResult = result;
            return btn;
        }

        var okBtn = MakeButton("Common.OK", true, DialogResult.OK);
        var cancelBtn = MakeButton("Common.Cancel", false, DialogResult.Cancel);
        var yesBtn = MakeButton("Common.Yes", true, DialogResult.Yes);
        var noBtn = MakeButton("Common.No", false, DialogResult.No);

        Panel btnPanel;
        switch (buttons)
        {
            case MsgBoxButtons.OK:
                btnPanel = BuildButtonBar(okBtn);
                AcceptButton = okBtn;
                CancelButton = okBtn;
                cancelBtn.Dispose();
                yesBtn.Dispose();
                noBtn.Dispose();
                break;
            case MsgBoxButtons.OKCancel:
                btnPanel = BuildButtonBar(cancelBtn, okBtn);
                AcceptButton = okBtn;
                CancelButton = cancelBtn;
                yesBtn.Dispose();
                noBtn.Dispose();
                break;
            case MsgBoxButtons.YesNo:
                btnPanel = BuildButtonBar(noBtn, yesBtn);
                AcceptButton = yesBtn;
                CancelButton = noBtn;
                okBtn.Dispose();
                cancelBtn.Dispose();
                break;
            case MsgBoxButtons.YesNoCancel:
            default:
                btnPanel = BuildButtonBar(cancelBtn, noBtn, yesBtn);
                CancelButton = cancelBtn;
                okBtn.Dispose();
                break;
        }

        // Fill added first, Bottom bar second - the order that actually renders correctly.
        Controls.Add(contentGrid);
        Controls.Add(btnPanel);

        using (var g = CreateGraphics())
        {
            var size = g.MeasureString(text, p.GridFont, MessageWrapWidth);
            // The wrap width fed to MeasureString has to match what msgLabel's own column will
            // actually be, or long text wraps one line more than this sizing budgeted for -
            // IconColumnWidth + msgLabel's left/right Padding (12+20) + a rough allowance for
            // the FixedDialog frame (16).
            Width = Math.Max(480, IconColumnWidth + 12 + 20 + 16 + (int)size.Width);
            Height = Math.Max(200, (int)size.Height + 140);
        }
    }

    /// <summary>Right-aligned button bar; visual left-to-right order matches the order passed in.</summary>
    private static Panel BuildButtonBar(params RoundedButton[] leftToRight)
    {
        var p = ThemeService.Current;
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(16, 8, 16, 8)
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        for (var i = 0; i < leftToRight.Length; i++)
        {
            leftToRight[i].Margin = i == leftToRight.Length - 1 ? new Padding(0) : new Padding(0, 0, 8, 0);
            flow.Controls.Add(leftToRight[i]);
        }
        panel.Controls.Add(flow);
        return panel;
    }

    /// <summary>Applies the icon label's font and color based on the <see cref="MsgBoxIcon"/> kind.</summary>
    protected override void ApplyTheme()
    {
        base.ApplyTheme();
        // _iconLabel's font/color depend on the icon kind chosen at construction time, which
        // ThemeSingleControl's generic Label handling has no way to know about - same pattern
        // AboutForm uses for its logo-area labels.
        var p = ThemeService.Current;
        _iconLabel.Font = p.IconGlyphFont;
        _iconLabel.ForeColor = _icon switch
        {
            MsgBoxIcon.Information => p.Accent,
            MsgBoxIcon.Warning => p.Warning,
            MsgBoxIcon.Error => p.Danger,
            // Question fell into the DimForeground default below before this case existed -
            // check_layout() found it (as missing_theme_role on the icon Label) and a live
            // get_pixel() sample confirmed the "?" glyph rendered notably duller than every
            // other icon kind, e.g. the Delete-confirmation dialog's icon. Grouped with
            // Information (both are neutral/informational, not a severity signal like
            // Warning/Error) rather than left in the DimForeground fallback below.
            MsgBoxIcon.Question => p.Accent,
            _ => p.DimForeground
        };
    }
}
