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
/// <para>
/// <b>Threading</b>: <see cref="TerminalScreen"/> is mutated on the pty reader thread and read here
/// on the UI thread - every method that touches <c>_session.Screen</c>'s buffers/cursor/modes
/// takes <c>_session.Screen.SyncRoot</c> for the duration of that read (see
/// <see cref="TerminalScreen.SyncRoot"/>'s doc comment).
/// </para>
/// <para>
/// <b>Scrollback coordinate space</b>: selection and find both anchor to a "combined" line index -
/// scrollback rows (oldest first) followed by the active screen's rows - rather than
/// viewport-relative rows, so they stay valid as the user scrolls. <see cref="_scrollOffset"/> is
/// the number of lines the viewport is scrolled back from live (0 = live, showing the active
/// screen). Both the scroll offset and any active selection are re-anchored in
/// <see cref="ReanchorForScrollbackGrowth"/> whenever new rows are pushed into scrollback while
/// scrolled back, using <see cref="TerminalScreen.ScrollbackTotalPushed"/> (which - unlike
/// <see cref="TerminalScreen.ScrollbackCount"/> - keeps counting after the ring fills up) so a
/// fixed index keeps pointing at the same text instead of silently drifting.
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

    private int _scrollOffset;
    private long _lastKnownScrollbackTotalPushed;

    private readonly TerminalSelection _selection = new();
    private bool _selectionDragActive;

    private IReadOnlyList<(int Line, int Col, int Length)>? _findMatches;
    private int _findCurrentIndex = -1;

    /// <summary>Raised for a <see cref="TerminalAction"/> this canvas doesn't fully own itself
    /// (tab management, clear/reset) - the owning <c>EmbeddedTerminalPanel</c>/<see cref="TerminalTabView"/> handles these.</summary>
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
        _repaintTimer.Tick += (_, _) => { ReanchorForScrollbackGrowth(); FlushDirtyRows(); };
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

    // -- Scrollback --

    /// <summary>Number of lines the viewport is scrolled back from live (0 = live).</summary>
    private int MaxScrollOffset(TerminalScreen screen) => screen.IsAltScreenActive ? 0 : screen.ScrollbackCount;

    /// <summary>Combined-space line index of the viewport's TOP row. Must be called under
    /// <c>_session.Screen.SyncRoot</c>.</summary>
    private static int ViewportTopLine(TerminalScreen screen, int scrollOffset) => screen.ScrollbackCount - scrollOffset;

    /// <summary>Resolves a combined-space line index to its row. Must be called under
    /// <c>_session.Screen.SyncRoot</c>.</summary>
    private static TerminalRow GetCombinedRow(TerminalScreen screen, int combinedLine) =>
        combinedLine < screen.ScrollbackCount
            ? screen.GetScrollbackRow(combinedLine)
            : screen.GetRow(combinedLine - screen.ScrollbackCount);

    /// <summary>Keeps a scrolled-back viewport and any active selection anchored to the same text
    /// as new rows are pushed into scrollback (which would otherwise silently shift what a fixed
    /// index points at) - see the class doc comment.</summary>
    private void ReanchorForScrollbackGrowth()
    {
        long delta;
        lock (_session.Screen.SyncRoot)
        {
            delta = _session.Screen.ScrollbackTotalPushed - _lastKnownScrollbackTotalPushed;
            _lastKnownScrollbackTotalPushed = _session.Screen.ScrollbackTotalPushed;
        }
        if (delta <= 0) return;

        if (_scrollOffset > 0)
        {
            _scrollOffset = (int)Math.Min(_scrollOffset + delta, int.MaxValue);
            lock (_session.Screen.SyncRoot)
                _scrollOffset = Math.Min(_scrollOffset, MaxScrollOffset(_session.Screen));
        }
        if (_selection.IsActive)
            _selection.ShiftLines((int)Math.Min(delta, int.MaxValue));
        if (_findMatches != null)
            _findMatches = _findMatches.Select(m => (m.Line + (int)delta, m.Col, m.Length)).ToList();
    }

    private int VisibleRows()
    {
        lock (_session.Screen.SyncRoot)
            return _session.Screen.Rows;
    }

    private void ScrollBy(int linesUp)
    {
        lock (_session.Screen.SyncRoot)
            _scrollOffset = Math.Clamp(_scrollOffset + linesUp, 0, MaxScrollOffset(_session.Screen));
        Invalidate();
    }

    private void ScrollToTop()
    {
        lock (_session.Screen.SyncRoot)
            _scrollOffset = MaxScrollOffset(_session.Screen);
        Invalidate();
    }

    private void ScrollToLive()
    {
        _scrollOffset = 0;
        Invalidate();
    }

    /// <summary>Scrolls so <paramref name="combinedLine"/> is visible, a couple of lines below the
    /// viewport top for a little context - used by the find bar to bring a match into view.</summary>
    public void ScrollToCombinedLine(int combinedLine)
    {
        lock (_session.Screen.SyncRoot)
        {
            var screen = _session.Screen;
            var target = screen.ScrollbackCount - combinedLine + 2;
            _scrollOffset = Math.Clamp(target, 0, MaxScrollOffset(screen));
        }
        Invalidate();
    }

    // -- Find (used by TerminalFindBar) --

    /// <summary>Total addressable lines (scrollback + active screen) at this moment.</summary>
    public int CombinedLineCount()
    {
        lock (_session.Screen.SyncRoot)
            return _session.Screen.ScrollbackCount + _session.Screen.Rows;
    }

    /// <summary>Plain-text content of one combined-space line, for the find bar to search against.</summary>
    public string GetCombinedLineText(int combinedLine)
    {
        lock (_session.Screen.SyncRoot)
        {
            var row = GetCombinedRow(_session.Screen, combinedLine);
            return RowToPlainText(row, 0, row.Cells.Length - 1);
        }
    }

    /// <summary>Sets (or clears, with a null/empty list) the highlighted find matches and which
    /// one is "current" (drawn in a brighter color).</summary>
    public void SetFindHighlights(IReadOnlyList<(int Line, int Col, int Length)>? matches, int currentIndex)
    {
        _findMatches = matches;
        _findCurrentIndex = currentIndex;
        Invalidate();
    }

    // -- Repaint --

    private void FlushDirtyRows()
    {
        if (IsDisposed || !IsHandleCreated) return;

        bool fullRepaint;
        lock (_session.Screen.SyncRoot)
        {
            var dirty = _session.Screen.Dirty;
            fullRepaint = dirty.FullRepaint;
            if (!fullRepaint)
            {
                // Only the live viewport's dirty rows matter for a partial repaint - if scrolled
                // back, the visible content is scrollback (never dirty) plus possibly the tail of
                // the active screen, which FullRepaint would have caught on a resize/ED anyway.
                for (var r = 0; r < _session.Screen.Rows; r++)
                {
                    if (!dirty.IsDirty(r)) continue;
                    if (_scrollOffset == 0)
                        Invalidate(RowRect(r));
                    else
                        fullRepaint = true; // simplest correct fallback while scrolled back
                }
            }
            dirty.Clear();
        }
        if (fullRepaint) Invalidate();
    }

    private Rectangle RowRect(int viewportRow) => new(0, viewportRow * _lineHeight, ClientSize.Width, _lineHeight);

    private void InvalidateCursorCell()
    {
        if (_scrollOffset != 0) return; // cursor isn't in the visible viewport while scrolled back
        int col, row;
        lock (_session.Screen.SyncRoot)
        {
            col = _session.Screen.CursorCol;
            row = _session.Screen.CursorRow;
        }
        Invalidate(new Rectangle(col * _charWidth, row * _lineHeight, _charWidth * 2, _lineHeight));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = ThemeService.Current.Terminal;
        using (var bg = new SolidBrush(p.DefaultBackground))
            e.Graphics.FillRectangle(bg, e.ClipRectangle);

        lock (_session.Screen.SyncRoot)
        {
            var screen = _session.Screen;
            var top = ViewportTopLine(screen, _scrollOffset);
            var firstViewportRow = Math.Max(0, e.ClipRectangle.Top / _lineHeight);
            var lastViewportRow = Math.Min(screen.Rows - 1, e.ClipRectangle.Bottom / _lineHeight);

            for (var r = firstViewportRow; r <= lastViewportRow; r++)
            {
                var combinedLine = top + r;
                if (combinedLine < 0) continue;
                var row = GetCombinedRow(screen, combinedLine);
                DrawRow(e.Graphics, row, r * _lineHeight);
            }

            DrawSelectionOverlay(e.Graphics, screen, top);
            DrawFindHighlightOverlay(e.Graphics, screen, top);

            if (_scrollOffset == 0)
                DrawCursor(e.Graphics, screen);
        }
    }

    private void DrawRow(Graphics g, TerminalRow row, int y)
    {
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

    private void DrawCursor(Graphics g, TerminalScreen screen)
    {
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

    private void DrawSelectionOverlay(Graphics g, TerminalScreen screen, int viewportTopLine)
    {
        if (!_selection.HasSelection) return;
        var p = ThemeService.Current.Terminal;
        using var brush = new SolidBrush(Color.FromArgb(110, p.SelectionBackground));
        var lastViewportRow = screen.Rows - 1;

        if (_selection.IsBlock)
        {
            var (top, left, bottom, right) = _selection.NormalizedBlock();
            for (var line = Math.Max(top, viewportTopLine); line <= bottom; line++)
            {
                var viewportRow = line - viewportTopLine;
                if (viewportRow < 0 || viewportRow > lastViewportRow) break;
                var x = left * _charWidth;
                var width = (right - left + 1) * _charWidth;
                g.FillRectangle(brush, x, viewportRow * _lineHeight, width, _lineHeight);
            }
        }
        else
        {
            var (l1, c1, l2, c2) = _selection.NormalizedRange();
            for (var line = Math.Max(l1, viewportTopLine); line <= l2; line++)
            {
                var viewportRow = line - viewportTopLine;
                if (viewportRow < 0 || viewportRow > lastViewportRow) break;
                var row = GetCombinedRow(screen, line);
                var fromCol = line == l1 ? c1 : 0;
                var toCol = line == l2 ? Math.Min(c2, row.Cells.Length - 1) : row.Cells.Length - 1;
                var x = fromCol * _charWidth;
                var width = Math.Max(_charWidth, (toCol - fromCol + 1) * _charWidth);
                g.FillRectangle(brush, x, viewportRow * _lineHeight, width, _lineHeight);
            }
        }
    }

    private void DrawFindHighlightOverlay(Graphics g, TerminalScreen screen, int viewportTopLine)
    {
        if (_findMatches is not { Count: > 0 }) return;
        var p = ThemeService.Current.Terminal;
        var lastViewportRow = screen.Rows - 1;

        for (var i = 0; i < _findMatches.Count; i++)
        {
            var m = _findMatches[i];
            var viewportRow = m.Line - viewportTopLine;
            if (viewportRow < 0 || viewportRow > lastViewportRow) continue;
            var color = i == _findCurrentIndex ? p.SearchMatchCurrent : p.SearchMatch;
            using var brush = new SolidBrush(Color.FromArgb(150, color));
            g.FillRectangle(brush, m.Col * _charWidth, viewportRow * _lineHeight, m.Length * _charWidth, _lineHeight);
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

    // -- Mouse (selection) --

    private (int Line, int Col) PixelToCombinedPosition(int x, int y)
    {
        var col = Math.Max(0, x / _charWidth);
        var viewportRow = Math.Clamp(y / Math.Max(1, _lineHeight), 0, int.MaxValue);
        lock (_session.Screen.SyncRoot)
        {
            var screen = _session.Screen;
            viewportRow = Math.Min(viewportRow, screen.Rows - 1);
            col = Math.Min(col, screen.Cols - 1);
            return (ViewportTopLine(screen, _scrollOffset) + viewportRow, col);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Focused) Focus();
        if (e.Button != MouseButtons.Left) return;

        var (line, col) = PixelToCombinedPosition(e.X, e.Y);
        _selection.Start(line, col, block: ModifierKeys.HasFlag(Keys.Alt));
        _selectionDragActive = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_selectionDragActive || e.Button != MouseButtons.Left) return;

        var (line, col) = PixelToCombinedPosition(e.X, e.Y);
        _selection.Extend(line, col);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _selectionDragActive = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        bool altScreen;
        lock (_session.Screen.SyncRoot)
            altScreen = _session.Screen.IsAltScreenActive;
        // Forwarding the wheel as arrow keys while the alt-screen has no scrollback of its own
        // (so e.g. `less`/`man` still scroll) is phase 5 (mouse) scope - for now the wheel simply
        // does nothing there rather than scrolling non-existent history.
        if (altScreen) return;

        var notches = e.Delta / 120;
        ScrollBy(-notches * 3);
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

        byte[]? bytes;
        lock (_session.Screen.SyncRoot)
            bytes = VtKeyEncoder.TryEncodeSpecialKey(keyCode, shift, control, alt, _session.Screen.Modes);

        if (bytes != null)
        {
            ScrollToLiveOnInput();
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

        ScrollToLiveOnInput();
        var altPressed = ModifierKeys.HasFlag(Keys.Alt) && !VtKeyEncoder.IsAltGrPressed();
        _session.SendInput(VtKeyEncoder.EncodePrintableChar(e.KeyChar, altPressed));
    }

    /// <summary>Typing while scrolled back into history jumps to live, matching every other
    /// terminal - the user is about to see the shell echo/react to what they typed, which only
    /// happens at the bottom.</summary>
    private void ScrollToLiveOnInput()
    {
        if (_scrollOffset != 0) ScrollToLive();
    }

    private void DispatchAction(TerminalAction action)
    {
        switch (action)
        {
            case TerminalAction.Copy:
                CopySelection();
                break;
            case TerminalAction.CopyOrInterrupt:
                if (_selection.HasSelection) CopySelection();
                else _session.SendInput([0x03]);
                break;
            case TerminalAction.Paste:
                PasteFromClipboard();
                break;
            case TerminalAction.SelectAll:
                SelectAll();
                break;
            case TerminalAction.IncreaseFont: Zoom(1); break;
            case TerminalAction.DecreaseFont: Zoom(-1); break;
            case TerminalAction.ResetFont: ResetZoom(); break;
            case TerminalAction.ScrollLineUp: ScrollBy(1); break;
            case TerminalAction.ScrollLineDown: ScrollBy(-1); break;
            case TerminalAction.ScrollPageUp: ScrollBy(Math.Max(1, VisibleRows() - 1)); break;
            case TerminalAction.ScrollPageDown: ScrollBy(-Math.Max(1, VisibleRows() - 1)); break;
            case TerminalAction.ScrollToTop: ScrollToTop(); break;
            case TerminalAction.ScrollToBottom: ScrollToLive(); break;
            default:
                // Tab management, find, clear/reset - owned by the panel/view hosting this canvas.
                ActionRequested?.Invoke(this, action);
                break;
        }
    }

    private void SelectAll()
    {
        lock (_session.Screen.SyncRoot)
        {
            var screen = _session.Screen;
            var total = screen.ScrollbackCount + screen.Rows;
            if (total == 0) return;
            var lastRow = GetCombinedRow(screen, total - 1);
            _selection.Start(0, 0, block: false);
            _selection.Extend(total - 1, Math.Max(0, lastRow.Cells.Length - 1));
        }
        Invalidate();
    }

    private void CopySelection()
    {
        var text = ExtractSelectionText();
        if (text.Length > 0)
            ClipboardHelper.TrySetClipboard(text);
    }

    private string ExtractSelectionText()
    {
        if (!_selection.HasSelection) return "";
        var sb = new StringBuilder();

        lock (_session.Screen.SyncRoot)
        {
            var screen = _session.Screen;
            if (_selection.IsBlock)
            {
                var (top, left, bottom, right) = _selection.NormalizedBlock();
                for (var line = top; line <= bottom; line++)
                {
                    var row = GetCombinedRow(screen, line);
                    sb.Append(RowToPlainText(row, left, Math.Min(right, row.Cells.Length - 1)).TrimEnd());
                    if (line != bottom) sb.Append('\n');
                }
            }
            else
            {
                var (l1, c1, l2, c2) = _selection.NormalizedRange();
                for (var line = l1; line <= l2; line++)
                {
                    var row = GetCombinedRow(screen, line);
                    var fromCol = line == l1 ? c1 : 0;
                    var toCol = line == l2 ? Math.Min(c2, row.Cells.Length - 1) : row.Cells.Length - 1;
                    sb.Append(RowToPlainText(row, fromCol, toCol).TrimEnd());
                    // A wrapped row's continuation is the same logical line - no line break.
                    if (line != l2 && !row.Wrapped) sb.Append('\n');
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>Cell range [fromCol, toColInclusive] as plain text - a WideTrail cell contributes
    /// nothing (its glyph lives on the paired WideLead cell), everything else always has a real
    /// rune (blank cells are U+0020, not 0 - see <see cref="TerminalCell.Blank"/>).</summary>
    private static string RowToPlainText(TerminalRow row, int fromCol, int toColInclusive)
    {
        var sb = new StringBuilder();
        var end = Math.Min(toColInclusive, row.Cells.Length - 1);
        for (var c = Math.Max(0, fromCol); c <= end; c++)
        {
            var cell = row.Cells[c];
            if (cell.Flags.HasFlag(CellFlags.WideTrail)) continue;
            sb.Append(char.ConvertFromUtf32(cell.Rune));
            if (cell.Flags.HasFlag(CellFlags.HasCombining) && row.Combining != null && row.Combining.TryGetValue(c, out var tail))
                sb.Append(tail);
        }
        return sb.ToString();
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

        bool bracketedPaste;
        lock (_session.Screen.SyncRoot)
            bracketedPaste = _session.Screen.Modes.BracketedPaste;

        ScrollToLiveOnInput();
        if (bracketedPaste)
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
