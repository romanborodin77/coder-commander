using System.Text;
using CoderCommander.Services;
using CoderCommander.Terminal.Input;
using CoderCommander.Terminal.Screen;
using CoderCommander.WinForms;

namespace CoderCommander.Terminal.Ui;

/// <summary>
/// Owner-drawn rendering + input surface for one <see cref="TerminalSession"/> - one canvas per
/// tab, matching <see cref="WinForms.CodeEditorCanvas"/>'s pattern (<c>UserPaint</c> +
/// <c>OptimizedDoubleBuffer</c>, integer cell metrics, DPI-aware rescaling) but with a fixed-width
/// grid instead of free-flowing text, since every VT escape sequence addresses cells by (row, col).
/// <para>
/// <b>Keyboard routing</b>: <see cref="ProcessCmdKey"/> is the primary gate - it runs before the
/// message is ever translated to WM_CHAR, so it reliably wins regardless of what a focused sibling
/// control might otherwise do with the same chord (mirrors the reasoning the old
/// <c>EmbeddedTerminalPanel.ProcessCmdKey</c> already documented for Ctrl+T/Ctrl+W). Terminal-local
/// chords (<see cref="TerminalKeyBindings"/>) are resolved first, then non-printable keys via
/// <see cref="VtKeyEncoder.TryEncodeSpecialKey"/>; anything neither claims (plain letters/digits/
/// punctuation, Shift-modified glyphs, AltGr-composed characters) is deliberately left unclaimed so
/// it flows through to <see cref="OnKeyPress"/> instead - that is what makes dead keys, AltGr, and
/// IME composition work for free instead of needing to be reimplemented here. F9 is the one
/// explicit exception (falls through so <c>MainForm</c>'s ToggleTerminal hotkey still works while
/// focused) - F10 is deliberately NOT exempted, since real console apps (mc, htop) use it as an
/// ordinary key and this app's own F10=Exit binding must not steal it while typing.
/// </para>
/// <para>
/// <b>Repaint model</b>: does NOT invalidate synchronously from <see cref="TerminalSession.OutputArrived"/> -
/// that event fires on the pty reader thread for every chunk, and a busy command (e.g. "yes")
/// could flood the UI thread with invalidate calls. Instead a throttled timer polls
/// <see cref="TerminalScreen.Dirty"/> (a bitset the parser already writes to synchronously) and
/// repaints at a fixed cap, which is also what keeps backpressure free - the parser blocking the
/// pty read loop is itself what prevents unbounded output from outrunning the screen model.
/// </para>
/// </summary>
internal sealed class TerminalCanvas : Control, IKeyboardGreedyControl
{
    private readonly TerminalSession _session;
    private readonly TerminalKeyBindings _keyBindings;
    private TerminalColorCache _colors;

    private Font _font = null!;
    private Font? _ownedFont;
    private int _charWidth;
    private int _lineHeight;
    private float _zoomFactor = 1f;
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 3f;

    private readonly Dictionary<FontStyle, Font> _styledFontCache = new();

    private readonly System.Windows.Forms.Timer _repaintTimer;
    private readonly System.Windows.Forms.Timer _caretTimer;
    private bool _caretVisible = true;

    /// <summary>Raised for a <see cref="TerminalAction"/> this canvas doesn't fully own itself
    /// (tab management, find, scrollback navigation, clear/reset) - the owning
    /// <c>EmbeddedTerminalPanel</c> handles these.</summary>
    public event EventHandler<TerminalAction>? ActionRequested;

    public TerminalSession Session => _session;

    public TerminalCanvas(TerminalSession session, TerminalKeyBindings keyBindings)
    {
        _session = session;
        _keyBindings = keyBindings;
        _colors = new TerminalColorCache(ThemeService.Current.Terminal);

        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.IBeam;

        RescaleMetrics();

        _repaintTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _repaintTimer.Tick += (_, _) => FlushDirtyRows();
        _repaintTimer.Start();

        _caretTimer = new System.Windows.Forms.Timer { Interval = 530 };
        _caretTimer.Tick += (_, _) =>
        {
            _caretVisible = !_caretVisible;
            InvalidateCursorCell();
        };
        _caretTimer.Start();

        ThemeService.ThemeChanged += OnThemeChanged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeService.ThemeChanged -= OnThemeChanged;
            _repaintTimer.Stop();
            _repaintTimer.Dispose();
            _caretTimer.Stop();
            _caretTimer.Dispose();
            foreach (var font in _styledFontCache.Values) font.Dispose();
            _styledFontCache.Clear();
            _ownedFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    // -- Theming / metrics --

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        _colors = new TerminalColorCache(ThemeService.Current.Terminal);
        RescaleMetrics();
        Invalidate();
    }

    private void RescaleMetrics()
    {
        var p = ThemeService.Current;
        var oldOwned = _ownedFont;
        if (Math.Abs(_zoomFactor - 1f) < 0.01f)
        {
            _font = p.MonoFont;
            _ownedFont = null;
        }
        else
        {
            _font = _ownedFont = new Font(p.MonoFont.FontFamily, p.MonoFont.Size * _zoomFactor, p.MonoFont.Style);
        }
        oldOwned?.Dispose();

        foreach (var font in _styledFontCache.Values) font.Dispose();
        _styledFontCache.Clear();

        // Integer cell width, deliberately not float (unlike CodeEditorCanvas's _charWidth) -
        // fractional accumulation across a wide terminal (200+ columns) visibly breaks box-drawing
        // pseudographics, where every cell boundary must land on an exact pixel.
        var wide = TextRenderer.MeasureText(new string('M', 32), _font, Size.Empty, TextFormatFlags.NoPadding);
        _charWidth = Math.Max(1, (int)Math.Round(wide.Width / 32.0));
        _lineHeight = Math.Max(1, TextRenderer.MeasureText("Mg", _font, Size.Empty, TextFormatFlags.NoPadding).Height);

        Invalidate();
    }

    private Font StyledFont(CellFlags flags)
    {
        var style = FontStyle.Regular;
        if (flags.HasFlag(CellFlags.Bold)) style |= FontStyle.Bold;
        if (flags.HasFlag(CellFlags.Italic)) style |= FontStyle.Italic;
        if (flags.HasFlag(CellFlags.Underline) || flags.HasFlag(CellFlags.DoubleUnderline)) style |= FontStyle.Underline;
        if (flags.HasFlag(CellFlags.Strike)) style |= FontStyle.Strikeout;

        if (style == FontStyle.Regular) return _font;
        if (_styledFontCache.TryGetValue(style, out var cached)) return cached;

        var font = new Font(_font.FontFamily, _font.Size, style);
        _styledFontCache[style] = font;
        return font;
    }

    internal void Zoom(int steps)
    {
        var newZoom = Math.Clamp(_zoomFactor + steps * 0.1f, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - _zoomFactor) < 0.001f) return;
        _zoomFactor = newZoom;
        RescaleMetrics();
        ResizeSessionToClientArea();
    }

    internal void ResetZoom()
    {
        if (Math.Abs(_zoomFactor - 1f) < 0.001f) return;
        _zoomFactor = 1f;
        RescaleMetrics();
        ResizeSessionToClientArea();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RescaleMetrics();
        ResizeSessionToClientArea();
    }

    // -- Layout --

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ResizeSessionToClientArea();
    }

    private void ResizeSessionToClientArea()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        var cols = Math.Max(2, ClientSize.Width / _charWidth);
        var rows = Math.Max(1, ClientSize.Height / _lineHeight);
        _session.Resize(cols, rows);
        Invalidate();
    }

    // -- Repaint --

    private void FlushDirtyRows()
    {
        if (IsDisposed || !IsHandleCreated) return;

        var dirty = _session.Screen.Dirty;
        if (dirty.FullRepaint)
        {
            Invalidate();
        }
        else
        {
            for (var r = 0; r < _session.Screen.Rows; r++)
                if (dirty.IsDirty(r))
                    Invalidate(RowRect(r));
        }
        dirty.Clear();
    }

    private Rectangle RowRect(int row) => new(0, row * _lineHeight, ClientSize.Width, _lineHeight);

    private void InvalidateCursorCell()
    {
        var screen = _session.Screen;
        Invalidate(new Rectangle(screen.CursorCol * _charWidth, screen.CursorRow * _lineHeight, _charWidth * 2, _lineHeight));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = ThemeService.Current.Terminal;
        using (var bg = new SolidBrush(p.DefaultBackground))
            e.Graphics.FillRectangle(bg, e.ClipRectangle);

        var screen = _session.Screen;
        var firstRow = Math.Max(0, e.ClipRectangle.Top / _lineHeight);
        var lastRow = Math.Min(screen.Rows - 1, e.ClipRectangle.Bottom / _lineHeight);

        for (var r = firstRow; r <= lastRow; r++)
            DrawRow(e.Graphics, r, r * _lineHeight);

        DrawCursor(e.Graphics);
    }

    private void DrawRow(Graphics g, int rowIndex, int y)
    {
        var row = _session.Screen.GetRow(rowIndex);
        var cells = row.Cells;
        var col = 0;
        while (col < cells.Length)
        {
            if (cells[col].Flags.HasFlag(CellFlags.WideTrail))
            {
                col++; // orphaned trail (shouldn't normally happen) - skip, nothing to draw
                continue;
            }

            if (cells[col].Flags.HasFlag(CellFlags.WideLead))
            {
                // Wide glyphs are always their own run - never merged with neighbors. A run built
                // from concatenated characters relies on the font having uniform per-character
                // advance width; CJK/emoji glyphs pull in a substitute font via GDI font-linking
                // whose metrics won't match, which would misalign every character after it in a
                // shared run.
                DrawCellRun(g, row, col, 1, wide: true, y);
                col += 2;
                continue;
            }

            var runStart = col;
            col++;
            while (col < cells.Length &&
                   !cells[col].Flags.HasFlag(CellFlags.WideLead) && !cells[col].Flags.HasFlag(CellFlags.WideTrail) &&
                   SameRunAttributes(cells[runStart], cells[col]))
            {
                col++;
            }
            DrawCellRun(g, row, runStart, col - runStart, wide: false, y);
        }
    }

    private static bool SameRunAttributes(in TerminalCell a, in TerminalCell b) =>
        a.Fg == b.Fg && a.Bg == b.Bg && a.Flags == b.Flags;

    private void DrawCellRun(Graphics g, TerminalRow row, int startCol, int cellCount, bool wide, int y)
    {
        var first = row.Cells[startCol];
        var reverse = first.Flags.HasFlag(CellFlags.Reverse) ^ _session.Screen.Modes.ScreenReverse;
        var fg = _colors.Foreground(first.Fg);
        var bg = _colors.Background(first.Bg);
        if (reverse) (fg, bg) = (bg, fg);
        if (first.Flags.HasFlag(CellFlags.Invisible)) fg = bg;
        else if (first.Flags.HasFlag(CellFlags.Dim)) fg = Blend(fg, bg);

        var pixelWidth = (wide ? 2 : cellCount) * _charWidth;
        var x = startCol * _charWidth;

        using (var brush = new SolidBrush(bg))
            g.FillRectangle(brush, x, y, pixelWidth, _lineHeight);

        var sb = new StringBuilder(cellCount);
        for (var i = 0; i < cellCount; i++)
        {
            var c = row.Cells[startCol + i];
            if (c.Rune == 0) continue;
            sb.Append(char.ConvertFromUtf32(c.Rune));
            if (c.Flags.HasFlag(CellFlags.HasCombining) && row.Combining != null && row.Combining.TryGetValue(startCol + i, out var tail))
                sb.Append(tail);
        }
        if (sb.Length == 0) return;

        TextRenderer.DrawText(g, sb.ToString(), StyledFont(first.Flags), new Point(x, y), fg,
            TextFormatFlags.NoPadding | TextFormatFlags.NoClipping | TextFormatFlags.Left | TextFormatFlags.Top);
    }

    private static Color Blend(Color fg, Color bg) => Color.FromArgb(
        (fg.R + bg.R) / 2, (fg.G + bg.G) / 2, (fg.B + bg.B) / 2);

    private void DrawCursor(Graphics g)
    {
        var screen = _session.Screen;
        if (!screen.Modes.CursorVisible) return;

        var p = ThemeService.Current.Terminal;
        var x = screen.CursorCol * _charWidth;
        var y = screen.CursorRow * _lineHeight;
        var width = _charWidth; // reserved: widen for a wide-cell cursor in a later phase

        if (!Focused)
        {
            using var pen = new Pen(p.InactiveCursor);
            g.DrawRectangle(pen, x, y, width - 1, _lineHeight - 1);
            return;
        }

        if (!_caretVisible && screen.Modes.CursorBlink) return;

        using (var brush = new SolidBrush(p.Cursor))
            g.FillRectangle(brush, x, y, width, _lineHeight);

        var row = screen.GetRow(screen.CursorRow);
        if (screen.CursorCol < row.Cells.Length && row.Cells[screen.CursorCol].Rune != 0)
        {
            var ch = char.ConvertFromUtf32(row.Cells[screen.CursorCol].Rune);
            TextRenderer.DrawText(g, ch, _font, new Point(x, y), p.CursorText,
                TextFormatFlags.NoPadding | TextFormatFlags.NoClipping | TextFormatFlags.Left | TextFormatFlags.Top);
        }
    }

    // -- Focus --

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _caretVisible = true;
        InvalidateCursorCell();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateCursorCell();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Focused) Focus();
    }

    // -- Keyboard --

    /// <summary>Only F9 (bare, no modifiers) is allowed to reach app-level hotkeys while this
    /// canvas has focus - see the class doc comment.</summary>
    public bool AllowsAppHotkey(Keys keyCode) => keyCode == Keys.F9;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F9)
            return false;

        var keyCode = keyData & Keys.KeyCode;
        var action = _keyBindings.Resolve(keyData);
        if (action != TerminalAction.None)
        {
            DispatchAction(action);
            return true;
        }

        var shift = keyData.HasFlag(Keys.Shift);
        var control = keyData.HasFlag(Keys.Control);
        var alt = keyData.HasFlag(Keys.Alt);

        var bytes = VtKeyEncoder.TryEncodeSpecialKey(keyCode, shift, control, alt, _session.Screen.Modes);
        if (bytes != null)
        {
            _session.SendInput(bytes);
            return true;
        }

        // Unclaimed: plain letters/digits/punctuation, Shift-modified glyphs, and AltGr-composed
        // characters all fall through here on purpose, so WM_CHAR/OnKeyPress delivers them with
        // dead-key/IME/layout composition already resolved by Windows.
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        e.Handled = true;

        // Defensive only - in the normal pipeline a control character that ProcessCmdKey wanted to
        // handle (Enter, Tab, Backspace, Escape, Ctrl+letter) never reaches WM_CHAR at all, since
        // consuming it at the ProcessCmdKey stage prevents the translation entirely.
        if (char.IsControl(e.KeyChar)) return;

        var altPressed = ModifierKeys.HasFlag(Keys.Alt) && !VtKeyEncoder.IsAltGrPressed();
        _session.SendInput(VtKeyEncoder.EncodePrintableChar(e.KeyChar, altPressed));
    }

    private void DispatchAction(TerminalAction action)
    {
        switch (action)
        {
            case TerminalAction.Copy:
                // No selection model yet (lands in a later phase) - nothing to copy.
                break;
            case TerminalAction.CopyOrInterrupt:
                // Until selection exists, this always interrupts - still the correct behavior for
                // "Ctrl+C with nothing selected".
                _session.SendInput([0x03]);
                break;
            case TerminalAction.Paste:
                PasteFromClipboard();
                break;
            case TerminalAction.IncreaseFont: Zoom(1); break;
            case TerminalAction.DecreaseFont: Zoom(-1); break;
            case TerminalAction.ResetFont: ResetZoom(); break;
            default:
                // Tab management, find, scrollback navigation, clear/reset - owned by the panel
                // hosting this canvas (tab lifecycle) or a later phase (scrollback/find).
                ActionRequested?.Invoke(this, action);
                break;
        }
    }

    private void PasteFromClipboard()
    {
        string? text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (Exception ex)
        {
            LogService.Error($"Terminal paste failed: {ex.Message}");
            return;
        }
        if (string.IsNullOrEmpty(text)) return;

        // Strip any literal bracketed-paste markers from the payload itself - otherwise clipboard
        // content containing a forged "ESC[201~" could end the paste envelope early and make the
        // rest of the payload land on the shell's command line as if typed.
        text = text.Replace("\x1b[200~", "").Replace("\x1b[201~", "");
        var bytes = Encoding.UTF8.GetBytes(text);

        if (_session.Screen.Modes.BracketedPaste)
        {
            _session.SendInput("\x1b[200~"u8);
            _session.SendInput(bytes);
            _session.SendInput("\x1b[201~"u8);
        }
        else
        {
            _session.SendInput(bytes);
        }
    }
}
