using CoderCommander.Services;
using System.Drawing.Drawing2D;

namespace CoderCommander.WinForms;

/// <summary>
/// Fully owner-drawn scrollbar with theme support. Supports vertical and horizontal orientations.
/// The track can be wider/taller than the visible thumb (see <see cref="ScaledThumbThickness"/>) -
/// <see cref="ListViewScrollbarOverlay"/> relies on this to size the track to a ListView's exact
/// native scrollbar footprint while keeping the thumb visually slim (VSCode style).
/// </summary>
public sealed class ThemedScrollBar : Control
{
    private Orientation _orientation = Orientation.Vertical;
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private int _largeChange = 10;
    private int _smallChange = 1;

    private bool _thumbHover;
    private bool _thumbPressed;
    private Point _dragStart;
    private int _dragValue;

    private const int ThumbMinSize = 30;
    private const int ThumbThickness = 10;
    private readonly System.Windows.Forms.Timer _scrollTimer;
    private bool _scrollUp;
    private bool _scrollDown;
    private bool _repeatPrimed;
    private Point _trackClickPoint;

    /// <summary>Raised when the <see cref="Value"/> property changes.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Gets or sets the scroll orientation (vertical or horizontal).</summary>
    public Orientation Orientation
    {
        get => _orientation;
        set { _orientation = value; Invalidate(); }
    }

    /// <summary>Gets or sets the minimum scroll value.</summary>
    public int Minimum
    {
        get => _minimum;
        set { _minimum = value; Invalidate(); }
    }

    /// <summary>Gets or sets the maximum scroll value.</summary>
    public int Maximum
    {
        get => _maximum;
        set { _maximum = value; Invalidate(); }
    }

    /// <summary>
    /// Gets or sets the current scroll position. The value is clamped to
    /// [<see cref="Minimum"/>, <see cref="Maximum"/> - <see cref="LargeChange"/>].
    /// </summary>
    public int Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, _minimum, Math.Max(_minimum, _maximum - _largeChange));
            if (_value == value) return;
            _value = value;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    /// <summary>Gets or sets the large change (page) increment for scroll operations.</summary>
    public int LargeChange
    {
        get => _largeChange;
        set { _largeChange = value; Invalidate(); }
    }

    /// <summary>Gets or sets the small change (arrow click) increment.</summary>
    public int SmallChange
    {
        get => _smallChange;
        set { _smallChange = value; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemedScrollBar"/> class with double buffering,
    /// non-selectable style, and an auto-repeat timer for track clicks.
    /// </summary>
    public ThemedScrollBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        // Not selectable - a scrollbar for a ListView/editor must never steal keyboard focus
        // away from the control it scrolls. Control.WmMouseDown calls Focus() on any selectable
        // control on mouse-down, so leaving this true meant dragging the thumb silently killed
        // arrow-key/Enter/F-key routing to the file list until the user clicked back into it.
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;

        _scrollTimer = new System.Windows.Forms.Timer { Interval = 60 };
        _scrollTimer.Tick += OnScrollTimerTick;

        ThemeService.ThemeChanged += OnThemeChanged;
    }

    /// <summary>Stops the auto-repeat timer and unsubscribes from the theme change event.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scrollTimer.Stop();
            _scrollTimer.Dispose();
            ThemeService.ThemeChanged -= OnThemeChanged;
        }
        base.Dispose(disposing);
    }

    /// <summary>Invalidates the scrollbar when the theme changes.</summary>
    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();

    /// <summary>Repaints the scrollbar when the parent DPI changes.</summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }

    /// <summary>Scales a 96-DPI design pixel value to this control's current DPI. Read fresh on
    /// every call (never cached) - the app is PerMonitorV2-aware and a control can move between
    /// differently-scaled monitors during its lifetime.</summary>
    private int Scale(int px96) => (int)Math.Round(px96 * DeviceDpi / 96.0);

    private int ScaledThumbMin => Scale(ThumbMinSize);
    private int ScaledThumbThickness => Scale(ThumbThickness);
    private int ScaledRadius => Math.Max(2, Scale(4));

    /// <summary>Owner-draws the scrollbar track and rounded thumb with hover/pressed states.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var p = ThemeService.Current;
        var rect = ClientRectangle;

        // Track background (no border — VSCode style)
        using (var bg = new SolidBrush(p.ScrollbarTrack))
            g.FillRectangle(bg, rect);

        if (_maximum <= _minimum || Width <= 0 || Height <= 0) return;

        var thumbRect = GetThumbRect();
        var thumbColor = _thumbPressed ? p.ScrollbarThumbPressed
            : _thumbHover ? p.ScrollbarThumbHover
            : p.ScrollbarThumb;

        if (thumbRect.Width > 0 && thumbRect.Height > 0)
        {
            using var thumbPath = GraphicsHelpers.GetRoundedRect(thumbRect, ScaledRadius);
            using var thumbBrush = new SolidBrush(thumbColor);
            g.FillPath(thumbBrush, thumbPath);
        }
    }

    /// <summary>
    /// Value's setter clamps to [minimum, maximum - largeChange], so that clamped span — not the
    /// raw maximum-minimum range — is what the thumb's travel must be normalized against for it
    /// to reach the end of the track exactly when Value reaches its clamped maximum.
    /// </summary>
    private int UsableRange => Math.Max(1, _maximum - _minimum - _largeChange);

    /// <summary>
    /// The track (<see cref="Width"/>/<see cref="Height"/> across the scroll axis) can be wider
    /// than the visually slim thumb - <see cref="ListViewScrollbarOverlay"/> sizes the track to a
    /// ListView's exact native scrollbar footprint (17px+ at higher DPI) while this keeps the
    /// thumb itself at a constant, centered <see cref="ScaledThumbThickness"/>.
    /// </summary>
    private Rectangle GetThumbRect()
    {
        var vertical = _orientation == Orientation.Vertical;
        var trackSize = vertical ? Height : Width;
        var barThickness = vertical ? Width : Height;
        if (trackSize <= 0 || barThickness <= 0) return Rectangle.Empty;

        var range = _maximum - _minimum;
        if (range <= 0) return Rectangle.Empty;

        // Clamp thumbSize into [min(ScaledThumbMin, trackSize), trackSize] BEFORE deriving
        // trackLength, so trackLength = trackSize - thumbSize can never go negative (that used to
        // make Math.Clamp(pos, 0, trackLength) throw ArgumentException whenever the bar was
        // shorter than the old fixed ThumbMinSize).
        var minThumb = Math.Min(ScaledThumbMin, trackSize);
        var rawThumb = (int)((double)_largeChange / range * trackSize);
        var thumbSize = Math.Clamp(rawThumb, minThumb, trackSize);

        var trackLength = trackSize - thumbSize;
        var pos = trackLength <= 0
            ? 0
            : Math.Clamp((int)((double)(_value - _minimum) / UsableRange * trackLength), 0, trackLength);

        var thumbThickness = Math.Clamp(ScaledThumbThickness, 1, barThickness);
        var inset = Math.Max(0, (barThickness - thumbThickness) / 2);

        return vertical
            ? new Rectangle(inset, pos, thumbThickness, thumbSize)
            : new Rectangle(pos, inset, thumbSize, thumbThickness);
    }

    /// <summary>Handles thumb dragging or track click (page up/down) with auto-repeat.</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        var thumbRect = GetThumbRect();
        if (thumbRect.Contains(e.Location))
        {
            _thumbPressed = true;
            _dragStart = e.Location;
            _dragValue = _value;
            Invalidate();
        }
        else
        {
            var pos = _orientation == Orientation.Vertical ? e.Y : e.X;
            var thumbSize = _orientation == Orientation.Vertical ? thumbRect.Height : thumbRect.Width;
            var thumbPos = _orientation == Orientation.Vertical ? thumbRect.Y : thumbRect.X;

            if (pos < thumbPos)
            {
                Value -= _largeChange;
                _scrollUp = true;
                _scrollDown = false;
            }
            else if (pos > thumbPos + thumbSize)
            {
                Value += _largeChange;
                _scrollDown = true;
                _scrollUp = false;
            }
            else
            {
                return;
            }

            // Standard scrollbar auto-repeat: a longer initial delay before the fast repeat
            // kicks in, and it stops on its own once the thumb reaches the click point or hits
            // an end - this used to run forever (OnMouseUp only stopped the timer
            // "if (_thumbPressed)", which a track click never sets).
            _trackClickPoint = e.Location;
            _repeatPrimed = false;
            _scrollTimer.Interval = 300;
            _scrollTimer.Start();
        }
    }

    /// <summary>Updates thumb position during drag and hover state when not dragging.</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_thumbPressed)
        {
            var delta = _orientation == Orientation.Vertical ? e.Y - _dragStart.Y : e.X - _dragStart.X;
            var trackSize = (_orientation == Orientation.Vertical ? Height : Width);
            var thumbSize = _orientation == Orientation.Vertical ? GetThumbRect().Height : GetThumbRect().Width;
            var trackLength = trackSize - thumbSize;
            var range = _maximum - _minimum;

            if (trackLength > 0 && range > 0)
            {
                var newValue = _dragValue + (int)((double)delta / trackLength * UsableRange);
                Value = newValue;
            }
        }
        else
        {
            var thumbRect = GetThumbRect();
            var wasHover = _thumbHover;
            _thumbHover = thumbRect.Contains(e.Location);
            if (wasHover != _thumbHover) Invalidate();
        }
    }

    /// <summary>Ends thumb drag and stops auto-repeat on mouse button release.</summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        StopAutoRepeat();
        if (_thumbPressed)
        {
            _thumbPressed = false;
            _thumbHover = GetThumbRect().Contains(e.Location);
            Invalidate();
        }
    }

    /// <summary>Handles lost mouse capture by releasing the pressed state and stopping auto-repeat.</summary>
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture) return;

        // Mouse capture can be lost mid-drag (Alt-Tab, another window stealing it, ...) without
        // OnMouseUp ever firing - without this, _thumbPressed/auto-repeat stayed stuck "on".
        StopAutoRepeat();
        if (_thumbPressed)
        {
            _thumbPressed = false;
            Invalidate();
        }
    }

    /// <summary>Clears the thumb hover state and repaints.</summary>
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _thumbHover = false;
        Invalidate();
    }

    /// <summary>Handles mouse wheel scrolling using system scroll line/page settings.</summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var steps = e.Delta / SystemInformation.MouseWheelScrollDelta;
        var lines = SystemInformation.MouseWheelScrollLines;
        // -1 means "one page per wheel notch" (the user's Windows setting); otherwise scroll by
        // that many of the bar's own SmallChange units. The old fixed "* 3" made a pixel-ranged
        // horizontal bar (SmallChange = 1 pixel) barely move at all.
        var step = lines < 0 ? Math.Max(1, _largeChange) : Math.Max(1, _smallChange * lines);
        Value -= steps * step;
    }

    /// <summary>Handles auto-repeat timer ticks for track clicks, with initial delay and repeat acceleration.</summary>
    private void OnScrollTimerTick(object? sender, EventArgs e)
    {
        if (!_repeatPrimed)
        {
            _repeatPrimed = true;
            _scrollTimer.Interval = 60;
        }

        // Standard scrollbar behavior: stop the repeat once the thumb has reached/passed the
        // point the user originally clicked at, instead of scrolling past it forever.
        var thumbRect = GetThumbRect();
        if (!thumbRect.IsEmpty && thumbRect.Contains(_trackClickPoint))
        {
            StopAutoRepeat();
            return;
        }

        var before = _value;
        if (_scrollUp) Value -= _largeChange;
        else if (_scrollDown) Value += _largeChange;
        if (_value == before) StopAutoRepeat(); // hit an end - nothing left to repeat
    }

    /// <summary>Stops the auto-repeat timer and resets all track-click state.</summary>
    private void StopAutoRepeat()
    {
        _scrollTimer.Stop();
        _scrollUp = false;
        _scrollDown = false;
        _repeatPrimed = false;
    }

}
