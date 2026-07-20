using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Owner-drawn text editing surface backing <see cref="CodeEditorControl"/>. Handles painting,
/// caret, selection, clipboard, undo/redo, syntax highlighting, and keyboard/mouse input directly
/// against a <see cref="TextBuffer"/> — no RichTextBox, no RTF. Word wrap is not implemented
/// (WordWrap is stored but not applied to layout — out of scope for this rewrite).
/// </summary>
internal sealed class CodeEditorCanvas : Control
{
    private readonly TextBuffer _buffer;
    private readonly UndoStack _undoStack = new();
    private TextPosition _caret;
    private TextPosition? _selectionAnchor;
    private int? _desiredColumn;
    private int _scrollY;
    private int _scrollX;

    private Font _font = null!;
    private float _charWidth;
    private int _lineHeight;

    private readonly System.Windows.Forms.Timer _caretTimer;
    private bool _caretVisible = true;

    private bool _isDragging;
    private Point _dragMouseLocation;
    private readonly System.Windows.Forms.Timer _dragScrollTimer;

    private LanguageId _language = LanguageId.PlainText;
    private readonly System.Windows.Forms.Timer _highlightTimer;
    private CancellationTokenSource? _highlightCts;
    private bool _tokenizeInFlight;
    private bool _tokenizePending;
    private List<SyntaxToken>? _lastTokens;
    private Dictionary<TokenType, Color> _colorMap = BuildColorMap();
    private List<ColorRun>?[] _lineRuns = [];
    private bool _skipLiveHighlight;

    private IReadOnlyList<FindMatch>? _findMatches;
    private int _findCurrentIndex = -1;

    private float _zoomFactor = 1f;
    private Font? _ownedFont;
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 3f;

    private (TextPosition Open, TextPosition Close)? _bracketMatch;
    public bool ShowWhitespace { get; set; }

    private static readonly Dictionary<char, char> BracketPairs = new() { ['('] = ')', ['['] = ']', ['{'] = '}' };
    private static readonly Dictionary<char, char> BracketPairsReverse = new() { [')'] = '(', [']'] = '[', ['}'] = '{' };

    private const int SyncTokenizeLineThreshold = 2000;
    private const int SyncTokenizeCharThreshold = 200_000;
    private const int SlowIntervalLineThreshold = 20_000;
    private const int SkipLiveHighlightLineThreshold = 100_000;
    private const long SkipLiveHighlightByteThreshold = 10 * 1024 * 1024;

    private readonly struct ColorRun
    {
        public int StartCol { get; init; }
        public int Length { get; init; }
        public TokenType Type { get; init; }
    }

    private DateTime _lastClickTime;
    private Point _lastClickPos;
    private int _clickStreak;

    public event EventHandler? CaretMoved;
    public event EventHandler? ContentChanged;
    public event EventHandler? ScrollChanged;

    public TextBuffer Buffer => _buffer;
    public TextPosition Caret => _caret;
    internal int LineHeight => _lineHeight;
    internal float CharWidth => _charWidth;

    public bool HasSelection => _selectionAnchor.HasValue && _selectionAnchor.Value != _caret;
    public bool CanUndo => _undoStack.CanUndo;
    public bool CanRedo => _undoStack.CanRedo;
    internal UndoStack UndoStack => _undoStack;

    public (TextPosition Start, TextPosition End)? SelectionRange
    {
        get
        {
            if (!HasSelection) return null;
            var a = _selectionAnchor!.Value;
            return a <= _caret ? (a, _caret) : (_caret, a);
        }
    }

    internal int ScrollY
    {
        get => _scrollY;
        set
        {
            var maxScroll = Math.Max(0, _buffer.LineCount * _lineHeight - ClientSize.Height);
            var clamped = Math.Clamp(value, 0, maxScroll);
            if (clamped == _scrollY) return;
            _scrollY = clamped;
            Invalidate();
            ScrollChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal int ScrollX
    {
        get => _scrollX;
        set
        {
            var clamped = Math.Max(0, value);
            if (clamped == _scrollX) return;
            _scrollX = clamped;
            Invalidate();
            ScrollChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public CodeEditorCanvas(TextBuffer buffer)
    {
        _buffer = buffer;
        _buffer.Changed += OnBufferChanged;

        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.IBeam;

        RescaleMetrics();

        _caretTimer = new System.Windows.Forms.Timer();
        _caretTimer.Tick += (_, _) =>
        {
            _caretVisible = !_caretVisible;
            InvalidateCaretLine();
        };

        _dragScrollTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _dragScrollTimer.Tick += OnDragScrollTimerTick;

        _highlightTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _highlightTimer.Tick += OnHighlightTimerTick;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer.Changed -= OnBufferChanged;
            _caretTimer.Stop();
            _caretTimer.Dispose();
            _dragScrollTimer.Stop();
            _dragScrollTimer.Dispose();
            _highlightTimer.Stop();
            _highlightTimer.Dispose();
            _highlightCts?.Cancel();
            _ownedFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    // -- Setup / theming --

    internal void RescaleMetrics()
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

        var wide = TextRenderer.MeasureText("MMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM", _font, Size.Empty, TextFormatFlags.NoPadding);
        _charWidth = wide.Width / 32f;
        _lineHeight = Math.Max(1, TextRenderer.MeasureText("Mg", _font, Size.Empty, TextFormatFlags.NoPadding).Height);
        Invalidate();
    }

    internal void Zoom(int steps)
    {
        var newZoom = Math.Clamp(_zoomFactor + steps * 0.1f, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - _zoomFactor) < 0.001f) return;
        _zoomFactor = newZoom;
        RescaleMetrics();
        EnsureCaretVisible();
    }

    internal void ResetZoom()
    {
        if (Math.Abs(_zoomFactor - 1f) < 0.001f) return;
        _zoomFactor = 1f;
        RescaleMetrics();
        EnsureCaretVisible();
    }

    public void ApplyTheme()
    {
        BackColor = ThemeService.Current.PanelBackground;
        RescaleMetrics();
        // Re-map cached tokens to new colors without re-tokenizing — O(runs), not O(document).
        _colorMap = BuildColorMap();
        Invalidate();
    }

    internal void ResetCaretToStart()
    {
        _caret = new TextPosition(0, 0);
        _selectionAnchor = null;
        _desiredColumn = null;
        _scrollY = 0;
        _scrollX = 0;
        _undoStack.Clear();
        Invalidate();
    }

    private void OnBufferChanged(object? sender, TextChangeEventArgs e)
    {
        Invalidate();
        RestartHighlightTimer();
        UpdateBracketMatch();
    }

    // -- Syntax highlighting --

    internal LanguageId Language => _language;

    internal void SetLanguage(LanguageId language)
    {
        _language = language;
        _skipLiveHighlight = false;
        _lastTokens = null;
        _lineRuns = [];
        _highlightCts?.Cancel();
        _highlightTimer.Stop();
        // Any in-flight background tokenize is now for a stale language and will no-op when it
        // completes (see the cts check in OnHighlightTimerTick) - clear the flags too so the
        // immediate re-tokenize below actually runs instead of just being coalesced behind it.
        _tokenizeInFlight = false;
        _tokenizePending = false;

        if (language == LanguageId.PlainText)
        {
            Invalidate();
            return;
        }

        // Apply immediately on load/language-change rather than waiting out the typing debounce.
        OnHighlightTimerTick(this, EventArgs.Empty);
    }

    private void RestartHighlightTimer()
    {
        if (_language == LanguageId.PlainText || _skipLiveHighlight) return;
        _highlightTimer.Interval = _buffer.LineCount > SlowIntervalLineThreshold ? 1000 : 100;
        _highlightTimer.Stop();
        _highlightTimer.Start();
    }

    private void OnHighlightTimerTick(object? sender, EventArgs e)
    {
        _highlightTimer.Stop();
        if (_language == LanguageId.PlainText) return;

        var text = _buffer.GetTextForTokenizing();
        var lineCount = _buffer.LineCount;

        if (lineCount > SkipLiveHighlightLineThreshold || text.Length > SkipLiveHighlightByteThreshold)
            _skipLiveHighlight = true; // this run still applies; future edits won't re-trigger

        if (lineCount <= SyncTokenizeLineThreshold && text.Length <= SyncTokenizeCharThreshold)
        {
            _highlightCts?.Cancel();
            _highlightCts = null;
            List<SyntaxToken> tokens;
            try { tokens = SyntaxHighlighter.Tokenize(text, _language); }
            catch (Exception ex) { LogService.Error($"Tokenize failed: {ex.Message}"); return; }
            ApplyTokens(tokens);
            return;
        }

        if (_tokenizeInFlight)
        {
            // SyntaxHighlighter.Tokenize has no cancellation checks inside its scan loops, so a
            // background pass already running on the thread pool can't actually be interrupted -
            // piling another one on top would just burn more CPU for a result we're about to
            // replace anyway. Note it and run once more, with the freshest text, once this finishes.
            _tokenizePending = true;
            return;
        }

        _highlightCts?.Cancel();
        var cts = new CancellationTokenSource();
        _highlightCts = cts;
        _tokenizeInFlight = true;
        var language = _language;
        Task.Run(() =>
        {
            List<SyntaxToken>? tokens = null;
            try { tokens = SyntaxHighlighter.Tokenize(text, language); }
            catch (Exception ex) { LogService.Error($"Tokenize failed: {ex.Message}"); }

            if (cts.Token.IsCancellationRequested || !IsHandleCreated)
            {
                _tokenizeInFlight = false;
                return;
            }
            try
            {
                BeginInvoke(() =>
                {
                    _tokenizeInFlight = false;
                    if (!cts.Token.IsCancellationRequested && tokens != null)
                        ApplyTokens(tokens);
                    if (_tokenizePending)
                    {
                        _tokenizePending = false;
                        RestartHighlightTimer();
                    }
                });
            }
            catch (ObjectDisposedException) { _tokenizeInFlight = false; /* canvas closed while tokenizing */ }
        }, cts.Token);
    }

    private void ApplyTokens(List<SyntaxToken> tokens)
    {
        _lastTokens = tokens;
        RebuildColorRuns();
        Invalidate();
    }

    private void RebuildColorRuns()
    {
        var lineCount = _buffer.LineCount;
        var lineRuns = new List<ColorRun>?[lineCount];

        if (_lastTokens == null || _lastTokens.Count == 0 || lineCount == 0)
        {
            _lineRuns = lineRuns;
            return;
        }

        var lineStarts = new int[lineCount];
        var offset = 0;
        for (var i = 0; i < lineCount; i++)
        {
            lineStarts[i] = offset;
            offset += _buffer.LineLength(i) + 1; // +1 for the '\n' joiner (see GetTextForTokenizing)
        }

        var lineIndex = 0;
        foreach (var token in _lastTokens)
        {
            if (token.Length <= 0) continue;
            var tokenEnd = token.Start + token.Length;

            while (lineIndex < lineCount - 1 && lineStarts[lineIndex + 1] <= token.Start) lineIndex++;

            var pos = token.Start;
            var curLine = lineIndex;
            while (pos < tokenEnd && curLine < lineCount)
            {
                var lineStart = lineStarts[curLine];
                var lineEndExclusive = curLine < lineCount - 1 ? lineStarts[curLine + 1] - 1 : offset;
                var segEnd = Math.Min(tokenEnd, lineEndExclusive);
                if (segEnd > pos)
                {
                    var run = new ColorRun { StartCol = pos - lineStart, Length = segEnd - pos, Type = token.Type };
                    (lineRuns[curLine] ??= []).Add(run);
                }
                pos = segEnd;
                if (pos < tokenEnd) { pos++; curLine++; }
            }
        }

        _lineRuns = lineRuns;
    }

    private static Dictionary<TokenType, Color> BuildColorMap()
    {
        var p = ThemeService.Current;
        return new Dictionary<TokenType, Color>
        {
            [TokenType.Plain] = p.Foreground,
            [TokenType.Keyword] = p.Accent,
            [TokenType.String] = p.ArchiveColor,
            [TokenType.Comment] = p.Syntax.Comment,
            [TokenType.Number] = p.Syntax.Number,
            [TokenType.Operator] = p.Foreground,
            [TokenType.Type] = p.DirectoryColor,
            [TokenType.Function] = p.Syntax.Function,
            [TokenType.Preprocessor] = p.ExecutableColor,
            [TokenType.Attribute] = p.Syntax.Attribute,
            [TokenType.Tag] = p.Accent,
            [TokenType.TagName] = p.Syntax.TagName,
            [TokenType.TagAttribute] = p.Syntax.TagAttribute,
            [TokenType.PropertyValue] = p.ArchiveColor,
            [TokenType.Selector] = p.Syntax.Selector,
            [TokenType.JsonKey] = p.Syntax.JsonKey,
            [TokenType.JsonValue] = p.ArchiveColor,
            [TokenType.SqlKeyword] = p.Accent,
            [TokenType.SqlFunction] = p.Syntax.SqlFunction,
            [TokenType.MarkdownHeader] = p.Accent,
            [TokenType.MarkdownBold] = p.Foreground,
            [TokenType.MarkdownItalic] = p.ExecutableColor,
            [TokenType.MarkdownCode] = p.ArchiveColor,
            [TokenType.MarkdownLink] = p.DirectoryColor,
            [TokenType.MarkdownList] = p.Foreground
        };
    }

    // -- Painting --

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = ThemeService.Current;
        g.Clear(p.PanelBackground);

        if (_lineHeight <= 0) return;

        var firstLine = Math.Max(0, _scrollY / _lineHeight);
        var visibleRows = ClientSize.Height / _lineHeight + 2;
        var lastLine = Math.Min(_buffer.LineCount - 1, firstLine + visibleRows);
        var selection = SelectionRange;

        // Current-line band. Skipped while there's an active selection — most editors mute it then.
        if (selection == null)
        {
            var caretY = _caret.Line * _lineHeight - _scrollY;
            using var band = new SolidBrush(p.RowHover);
            g.FillRectangle(band, 0, caretY, ClientSize.Width, _lineHeight);
        }

        for (var line = firstLine; line <= lastLine; line++)
        {
            var y = line * _lineHeight - _scrollY;

            if (selection is { } sel && line >= sel.Start.Line && line <= sel.End.Line)
            {
                var selStartCol = line == sel.Start.Line ? sel.Start.Column : 0;
                var lineLen = _buffer.LineLength(line);
                var selEndCol = line == sel.End.Line ? sel.End.Column : lineLen;
                var continuesToNextLine = line < sel.End.Line;
                var x1 = selStartCol * _charWidth - _scrollX;
                var x2 = selEndCol * _charWidth - _scrollX + (continuesToNextLine ? _charWidth : 0);
                var selColor = Focused ? p.Selection : p.InactiveSelection;
                using var selBrush = new SolidBrush(selColor);
                g.FillRectangle(selBrush, x1, y, Math.Max(2f, x2 - x1), _lineHeight);
            }

            if (_findMatches != null)
            {
                for (var mi = 0; mi < _findMatches.Count; mi++)
                {
                    var m = _findMatches[mi];
                    if (line < m.Start.Line || line > m.End.Line) continue;
                    var startCol = line == m.Start.Line ? m.Start.Column : 0;
                    var endCol = line == m.End.Line ? m.End.Column : _buffer.LineLength(line);
                    var mx1 = startCol * _charWidth - _scrollX;
                    var mx2 = endCol * _charWidth - _scrollX;
                    var matchColor = mi == _findCurrentIndex ? p.Selection : ThemeService.BlendColors(p.PanelBackground, p.Accent, 0.3f);
                    using var matchBrush = new SolidBrush(matchColor);
                    g.FillRectangle(matchBrush, mx1, y, Math.Max(2f, mx2 - mx1), _lineHeight);
                }
            }

            var text = _buffer.GetLine(line);
            if (text.Length == 0) continue;
            DrawLineText(g, line, text, y, p);
            if (ShowWhitespace) DrawWhitespaceGlyphs(g, text, y, p.DimForeground);
        }

        if (_bracketMatch is { } bm)
        {
            DrawBracketOutline(g, bm.Open, p);
            DrawBracketOutline(g, bm.Close, p);
        }

        if (_caretVisible && Focused)
        {
            var cx = (int)(_caret.Column * _charWidth) - _scrollX;
            var cy = _caret.Line * _lineHeight - _scrollY;
            using var pen = new Pen(p.Foreground, 2f);
            g.DrawLine(pen, cx, cy, cx, cy + _lineHeight - 1);
        }
    }

    private void DrawLineText(Graphics g, int lineIndex, string text, int y, ThemePalette p)
    {
        var runs = lineIndex < _lineRuns.Length ? _lineRuns[lineIndex] : null;
        if (runs == null || runs.Count == 0)
        {
            DrawRun(g, text, 0, y, p.Foreground);
            return;
        }

        var pos = 0;
        foreach (var run in runs)
        {
            if (run.StartCol > pos)
                DrawRun(g, text[pos..Math.Min(run.StartCol, text.Length)], pos, y, p.Foreground);

            var runEnd = Math.Min(run.StartCol + run.Length, text.Length);
            if (runEnd > run.StartCol)
            {
                var color = _colorMap.TryGetValue(run.Type, out var c) ? c : p.Foreground;
                DrawRun(g, text[run.StartCol..runEnd], run.StartCol, y, color);
            }
            pos = Math.Max(pos, runEnd);
        }
        if (pos < text.Length)
            DrawRun(g, text[pos..], pos, y, p.Foreground);
    }

    private void DrawRun(Graphics g, string runText, int startCol, int y, Color color)
    {
        if (runText.Length == 0) return;
        var x = (int)(startCol * _charWidth) - _scrollX;

        // Clip to the visible width ourselves - NoClipping below means TextRenderer will happily
        // measure and draw the whole run otherwise, which is wasted work on every repaint for a
        // very long single line (e.g. minified JS/CSS) where only a fraction is ever on screen.
        if (x >= ClientSize.Width || x + runText.Length * _charWidth < 0) return;

        if (x < 0)
        {
            var hiddenChars = Math.Min(runText.Length, (int)(-x / _charWidth) + 1);
            runText = runText[hiddenChars..];
            x += (int)(hiddenChars * _charWidth);
            if (runText.Length == 0) return;
        }

        var maxVisibleChars = (int)((ClientSize.Width - x) / _charWidth) + 2;
        if (maxVisibleChars < runText.Length)
            runText = runText[..Math.Max(0, maxVisibleChars)];
        if (runText.Length == 0) return;

        TextRenderer.DrawText(g, runText, _font, new Point(x, y), color,
            TextFormatFlags.NoPadding | TextFormatFlags.NoClipping | TextFormatFlags.Left | TextFormatFlags.Top);
    }

    private void DrawWhitespaceGlyphs(Graphics g, string text, int y, Color color)
    {
        using var pen = new Pen(color, 1f);
        using var dotBrush = new SolidBrush(color);
        var cw = (int)_charWidth;
        for (var i = 0; i < text.Length; i++)
        {
            var x = (int)(i * _charWidth) - _scrollX;
            if (x + cw < 0 || x > ClientSize.Width) continue;

            if (text[i] == ' ')
            {
                var cx = x + cw / 2;
                var cy = y + _lineHeight / 2;
                g.FillEllipse(dotBrush, cx - 1, cy - 1, 2, 2);
            }
            else if (text[i] == '\t')
            {
                var cy = y + _lineHeight / 2;
                g.DrawLine(pen, x + 2, cy, x + cw - 2, cy);
                g.DrawLine(pen, x + cw - 5, cy - 3, x + cw - 2, cy);
                g.DrawLine(pen, x + cw - 5, cy + 3, x + cw - 2, cy);
            }
        }
    }

    private void DrawBracketOutline(Graphics g, TextPosition pos, ThemePalette p)
    {
        var y = pos.Line * _lineHeight - _scrollY;
        if (y + _lineHeight < 0 || y > ClientSize.Height) return;
        var x = (int)(pos.Column * _charWidth) - _scrollX;
        using var pen = new Pen(p.Accent, 1.5f);
        g.DrawRectangle(pen, x, y, Math.Max(1, (int)_charWidth - 1), _lineHeight - 1);
    }

    private void InvalidateCaretLine()
    {
        var y = _caret.Line * _lineHeight - _scrollY;
        Invalidate(new Rectangle(0, y, ClientSize.Width, _lineHeight));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ScrollY = _scrollY; // re-clamp against the new viewport height
        Invalidate();
    }

    /// <summary>Re-measures glyph metrics if the editor window is dragged to a monitor with a different DPI.</summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RescaleMetrics();
    }

    // -- Caret / selection movement --

    private void MoveCaret(TextPosition pos, bool extendSelection = false, bool ensureVisible = true, bool keepDesiredColumn = false)
    {
        if (extendSelection) _selectionAnchor ??= _caret;
        else _selectionAnchor = null;
        _caret = _buffer.ClampPosition(pos);
        if (!keepDesiredColumn) _desiredColumn = null;
        ResetCaretBlink();
        if (ensureVisible) EnsureCaretVisible();
        UpdateBracketMatch();
        Invalidate();
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets the caret directly without touching selection state — used after edits, which own their own selection handling.</summary>
    private void SetCaret(TextPosition pos, bool ensureVisible = true)
    {
        _caret = _buffer.ClampPosition(pos);
        _desiredColumn = null;
        ResetCaretBlink();
        if (ensureVisible) EnsureCaretVisible();
        UpdateBracketMatch();
        Invalidate();
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateBracketMatch() => _bracketMatch = FindMatchingBrackets();

    /// <summary>
    /// Finds the bracket adjacent to the caret and its match, scanning by raw character (no
    /// awareness of strings/comments — a reasonable v1 simplification). Bounded to a few thousand
    /// lines so a stray unmatched bracket in a huge file can't make this scan the whole document
    /// on every caret move.
    /// </summary>
    private (TextPosition, TextPosition)? FindMatchingBrackets()
    {
        // OnBufferChanged fires synchronously from inside DeleteRange/InsertText, i.e. before the
        // caller updates _caret to match the new buffer contents (see HandleBackspace: it deletes,
        // which re-enters here via the Changed event, then only afterward calls SetCaret). _caret
        // can briefly point past the end of the now-shorter line, so it must be clamped here rather
        // than trusted as-is.
        var caret = _buffer.ClampPosition(_caret);
        var line = _buffer.GetLine(caret.Line);

        char ch;
        TextPosition pos;
        if (caret.Column < line.Length && (BracketPairs.ContainsKey(line[caret.Column]) || BracketPairsReverse.ContainsKey(line[caret.Column])))
        {
            ch = line[caret.Column];
            pos = caret;
        }
        else if (caret.Column > 0 && (BracketPairs.ContainsKey(line[caret.Column - 1]) || BracketPairsReverse.ContainsKey(line[caret.Column - 1])))
        {
            ch = line[caret.Column - 1];
            pos = new TextPosition(caret.Line, caret.Column - 1);
        }
        else
        {
            return null;
        }

        const int maxLinesToScan = 5000;
        var isOpen = BracketPairs.TryGetValue(ch, out var closeChar);
        var openChar = isOpen ? ch : BracketPairsReverse[ch];
        if (!isOpen) closeChar = ch;
        var depth = 0;

        if (isOpen)
        {
            for (var l = pos.Line; l < Math.Min(_buffer.LineCount, pos.Line + maxLinesToScan); l++)
            {
                var text = _buffer.GetLine(l);
                var start = l == pos.Line ? pos.Column + 1 : 0;
                for (var c = start; c < text.Length; c++)
                {
                    if (text[c] == openChar) depth++;
                    else if (text[c] == closeChar)
                    {
                        if (depth == 0) return (pos, new TextPosition(l, c));
                        depth--;
                    }
                }
            }
        }
        else
        {
            for (var l = pos.Line; l >= Math.Max(0, pos.Line - maxLinesToScan); l--)
            {
                var text = _buffer.GetLine(l);
                var start = l == pos.Line ? pos.Column - 1 : text.Length - 1;
                for (var c = start; c >= 0; c--)
                {
                    if (text[c] == closeChar) depth++;
                    else if (text[c] == openChar)
                    {
                        if (depth == 0) return (new TextPosition(l, c), pos);
                        depth--;
                    }
                }
            }
        }

        return null;
    }

    private void EnsureCaretVisible()
    {
        var caretTop = _caret.Line * _lineHeight;
        var caretBottom = caretTop + _lineHeight;
        if (caretTop < _scrollY) ScrollY = caretTop;
        else if (caretBottom > _scrollY + ClientSize.Height) ScrollY = caretBottom - ClientSize.Height;

        var caretX = (int)(_caret.Column * _charWidth);
        if (caretX < _scrollX) ScrollX = caretX;
        else if (caretX + (int)_charWidth > _scrollX + ClientSize.Width) ScrollX = caretX + (int)_charWidth - ClientSize.Width;
    }

    private TextPosition GetLeftPosition(TextPosition pos)
    {
        pos = _buffer.ClampPosition(pos);
        if (pos.Column > 0)
        {
            var line = _buffer.GetLine(pos.Line);
            var newCol = pos.Column - 1;
            if (newCol > 0 && char.IsLowSurrogate(line[newCol]) && char.IsHighSurrogate(line[newCol - 1]))
                newCol--;
            return new TextPosition(pos.Line, newCol);
        }
        if (pos.Line > 0) return new TextPosition(pos.Line - 1, _buffer.LineLength(pos.Line - 1));
        return pos;
    }

    private TextPosition GetRightPosition(TextPosition pos)
    {
        pos = _buffer.ClampPosition(pos);
        var line = _buffer.GetLine(pos.Line);
        if (pos.Column < line.Length)
        {
            var newCol = pos.Column + 1;
            if (newCol < line.Length && char.IsLowSurrogate(line[newCol]) && char.IsHighSurrogate(line[pos.Column]))
                newCol++;
            return new TextPosition(pos.Line, newCol);
        }
        if (pos.Line < _buffer.LineCount - 1) return new TextPosition(pos.Line + 1, 0);
        return pos;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private TextPosition GetWordLeft(TextPosition pos)
    {
        pos = _buffer.ClampPosition(pos);
        if (pos.Column == 0) return GetLeftPosition(pos);
        var line = _buffer.GetLine(pos.Line);
        var i = pos.Column;
        while (i > 0 && char.IsWhiteSpace(line[i - 1])) i--;
        if (i > 0)
        {
            var isWord = IsWordChar(line[i - 1]);
            while (i > 0 && !char.IsWhiteSpace(line[i - 1]) && IsWordChar(line[i - 1]) == isWord) i--;
        }
        return new TextPosition(pos.Line, i);
    }

    private TextPosition GetWordRight(TextPosition pos)
    {
        pos = _buffer.ClampPosition(pos);
        var line = _buffer.GetLine(pos.Line);
        if (pos.Column >= line.Length) return GetRightPosition(pos);
        var i = pos.Column;
        while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
        if (i < line.Length)
        {
            var isWord = IsWordChar(line[i]);
            while (i < line.Length && !char.IsWhiteSpace(line[i]) && IsWordChar(line[i]) == isWord) i++;
        }
        return new TextPosition(pos.Line, i);
    }

    // -- Keyboard input --

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData & ~Keys.Shift & ~Keys.Control)
        {
            case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
            case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown:
            case Keys.Tab:
                return true;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Control && !e.Alt)
        {
            switch (e.KeyCode)
            {
                case Keys.A: SelectAll(); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.C: Copy(); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.X: Cut(); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.V: Paste(); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.Z: if (e.Shift) Redo(); else Undo(); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.Y: Redo(); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.Oemplus: case Keys.Add: Zoom(1); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.OemMinus: case Keys.Subtract: Zoom(-1); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.D0: case Keys.NumPad0: ResetZoom(); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.Left: MoveCaret(GetWordLeft(_caret), e.Shift); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.Right: MoveCaret(GetWordRight(_caret), e.Shift); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.Home: MoveCaret(new TextPosition(0, 0), e.Shift); e.Handled = e.SuppressKeyPress = true; return;
                case Keys.End:
                {
                    var last = _buffer.LineCount - 1;
                    MoveCaret(new TextPosition(last, _buffer.LineLength(last)), e.Shift);
                    e.Handled = e.SuppressKeyPress = true;
                    return;
                }
            }
        }

        var handled = true;
        switch (e.KeyCode)
        {
            case Keys.Left: MoveCaret(GetLeftPosition(_caret), e.Shift); break;
            case Keys.Right: MoveCaret(GetRightPosition(_caret), e.Shift); break;
            case Keys.Up:
            {
                var col = _desiredColumn ?? _caret.Column;
                MoveCaret(new TextPosition(Math.Max(0, _caret.Line - 1), col), e.Shift, keepDesiredColumn: true);
                _desiredColumn = col;
                break;
            }
            case Keys.Down:
            {
                var col = _desiredColumn ?? _caret.Column;
                MoveCaret(new TextPosition(Math.Min(_buffer.LineCount - 1, _caret.Line + 1), col), e.Shift, keepDesiredColumn: true);
                _desiredColumn = col;
                break;
            }
            case Keys.Home: MoveCaret(new TextPosition(_caret.Line, 0), e.Shift); break;
            case Keys.End: MoveCaret(new TextPosition(_caret.Line, _buffer.LineLength(_caret.Line)), e.Shift); break;
            case Keys.PageUp:
            {
                var col = _desiredColumn ?? _caret.Column;
                MoveCaret(new TextPosition(Math.Max(0, _caret.Line - LinesPerPage()), col), e.Shift, keepDesiredColumn: true);
                _desiredColumn = col;
                break;
            }
            case Keys.PageDown:
            {
                var col = _desiredColumn ?? _caret.Column;
                MoveCaret(new TextPosition(Math.Min(_buffer.LineCount - 1, _caret.Line + LinesPerPage()), col), e.Shift, keepDesiredColumn: true);
                _desiredColumn = col;
                break;
            }
            case Keys.Back: HandleBackspace(); break;
            case Keys.Delete: HandleDelete(); break;
            case Keys.Enter: InsertTextAtCaret("\n"); break;
            case Keys.Tab: InsertTextAtCaret("\t"); break;
            default: handled = false; break;
        }

        if (handled)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private int LinesPerPage() => Math.Max(1, ClientSize.Height / Math.Max(1, _lineHeight));

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (char.IsControl(e.KeyChar))
        {
            e.Handled = true;
            return;
        }
        InsertTextAtCaret(e.KeyChar.ToString());
        e.Handled = true;
    }

    private void InsertTextAtCaret(string text, bool allowCoalesce = true)
    {
        var caretBefore = _caret;
        var insertPos = _caret;
        var oldText = "";
        if (HasSelection)
        {
            var (start, end) = SelectionRange!.Value;
            oldText = _buffer.GetTextInRange(start, end);
            _buffer.DeleteRange(start, end);
            insertPos = start;
            _selectionAnchor = null;
        }
        var newPos = _buffer.InsertText(insertPos, text);
        SetCaret(newPos);

        var coalesce = allowCoalesce && oldText.Length == 0 && text.Length == 1 && text != "\n";
        _undoStack.Record(insertPos, oldText, text, caretBefore, _caret, coalesce);

        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleBackspace()
    {
        if (HasSelection)
        {
            DeleteSelection();
            return;
        }
        if (_caret.Column == 0 && _caret.Line == 0) return;
        var caretBefore = _caret;
        var deleteStart = GetLeftPosition(_caret);
        var oldText = _buffer.GetTextInRange(deleteStart, _caret);
        _buffer.DeleteRange(deleteStart, _caret);
        SetCaret(deleteStart);
        _undoStack.Record(deleteStart, oldText, "", caretBefore, _caret, coalesceWithPrevious: true);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleDelete()
    {
        if (HasSelection)
        {
            DeleteSelection();
            return;
        }
        var deleteEnd = GetRightPosition(_caret);
        if (deleteEnd == _caret) return;
        var caretBefore = _caret;
        var oldText = _buffer.GetTextInRange(_caret, deleteEnd);
        _buffer.DeleteRange(_caret, deleteEnd);
        Invalidate();
        _undoStack.Record(_caret, oldText, "", caretBefore, _caret, coalesceWithPrevious: true);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteSelection()
    {
        var caretBefore = _caret;
        var (start, end) = SelectionRange!.Value;
        var oldText = _buffer.GetTextInRange(start, end);
        _buffer.DeleteRange(start, end);
        _selectionAnchor = null;
        SetCaret(start);
        _undoStack.Record(start, oldText, "", caretBefore, _caret, coalesceWithPrevious: false);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        var group = _undoStack.Undo();
        if (group == null) return;
        ApplyInverse(group, forward: false);
    }

    public void Redo()
    {
        var group = _undoStack.Redo();
        if (group == null) return;
        ApplyInverse(group, forward: true);
    }

    private void ApplyInverse(EditGroup group, bool forward)
    {
        // Edits are stored in the order they were originally, chronologically applied. Redo
        // replays that order; undo reverses it — see EditGroup's doc comment for why this matters
        // once a group holds more than one edit (Replace All).
        if (forward)
        {
            foreach (var edit in group.Edits)
                ApplyOneEdit(edit.Start, edit.OldText, edit.NewText);
        }
        else
        {
            for (var i = group.Edits.Count - 1; i >= 0; i--)
            {
                var edit = group.Edits[i];
                ApplyOneEdit(edit.Start, edit.NewText, edit.OldText);
            }
        }

        _selectionAnchor = null;
        SetCaret(forward ? group.CaretAfter : group.CaretBefore);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyOneEdit(TextPosition start, string removeText, string insertText)
    {
        if (removeText.Length > 0)
        {
            var end = TextBuffer.ComputeEndPosition(start, removeText);
            _buffer.DeleteRange(start, end);
        }
        if (insertText.Length > 0)
        {
            _buffer.InsertText(start, insertText);
        }
    }

    // -- Selection commands / clipboard --

    /// <summary>Programmatically selects [start, end) — used by the find bar to land on a match. start==end just moves the caret.</summary>
    internal void SelectRange(TextPosition start, TextPosition end)
    {
        _selectionAnchor = start;
        _caret = _buffer.ClampPosition(end);
        _desiredColumn = null;
        ResetCaretBlink();
        EnsureCaretVisible();
        UpdateBracketMatch();
        Invalidate();
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets the find-match highlight list the find bar wants painted (null clears it).</summary>
    internal void SetFindHighlights(IReadOnlyList<FindMatch>? matches, int currentIndex)
    {
        _findMatches = matches;
        _findCurrentIndex = currentIndex;
        Invalidate();
    }

    /// <summary>1-based line number, clamped to the document — matches the status bar's display convention.</summary>
    internal void GoToLine(int line)
    {
        var targetLine = Math.Clamp(line - 1, 0, _buffer.LineCount - 1);
        MoveCaret(new TextPosition(targetLine, 0));
    }

    public void SelectAll()
    {
        if (_buffer.LineCount == 0) return;
        _selectionAnchor = new TextPosition(0, 0);
        var lastLine = _buffer.LineCount - 1;
        MoveCaret(new TextPosition(lastLine, _buffer.LineLength(lastLine)), extendSelection: true, ensureVisible: false);
    }

    public void Copy()
    {
        if (!HasSelection) return;
        var (start, end) = SelectionRange!.Value;
        TrySetClipboard(_buffer.GetTextInRange(start, end));
    }

    public void Cut()
    {
        if (!HasSelection) return;
        Copy();
        DeleteSelection();
    }

    public void Paste()
    {
        string? text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (Exception ex)
        {
            LogService.Error($"Clipboard paste failed: {ex.Message}");
            return;
        }
        if (string.IsNullOrEmpty(text)) return;
        InsertTextAtCaret(text, allowCoalesce: false);
    }

    private static void TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            LogService.Error($"Clipboard copy failed: {ex.Message}");
        }
    }

    // -- Mouse input --

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;

        var now = DateTime.UtcNow;
        var withinTime = (now - _lastClickTime).TotalMilliseconds <= SystemInformation.DoubleClickTime;
        var withinDist = Math.Abs(e.X - _lastClickPos.X) <= SystemInformation.DoubleClickSize.Width &&
                          Math.Abs(e.Y - _lastClickPos.Y) <= SystemInformation.DoubleClickSize.Height;
        _clickStreak = withinTime && withinDist ? _clickStreak + 1 : 1;
        _lastClickTime = now;
        _lastClickPos = e.Location;

        var pos = PointToPosition(e.Location);

        if (_clickStreak >= 3)
        {
            SelectLine(pos.Line);
        }
        else if (_clickStreak == 2)
        {
            SelectWordAt(pos);
        }
        else
        {
            var shiftHeld = (ModifierKeys & Keys.Shift) == Keys.Shift;
            MoveCaret(pos, extendSelection: shiftHeld, ensureVisible: false);
            _isDragging = true;
        }
    }

    private void SelectWordAt(TextPosition pos)
    {
        var line = _buffer.GetLine(pos.Line);
        if (line.Length == 0) { MoveCaret(pos, ensureVisible: false); return; }
        var col = Math.Min(pos.Column, line.Length - 1);
        var isWord = IsWordChar(line[col]);
        var start = col;
        while (start > 0 && IsWordChar(line[start - 1]) == isWord) start--;
        var end = col;
        while (end < line.Length && IsWordChar(line[end]) == isWord) end++;
        _selectionAnchor = new TextPosition(pos.Line, start);
        MoveCaret(new TextPosition(pos.Line, end), extendSelection: true, ensureVisible: false);
    }

    private void SelectLine(int line)
    {
        _selectionAnchor = new TextPosition(line, 0);
        var nextLine = Math.Min(_buffer.LineCount - 1, line + 1);
        var target = nextLine > line ? new TextPosition(nextLine, 0) : new TextPosition(line, _buffer.LineLength(line));
        MoveCaret(target, extendSelection: true, ensureVisible: false);
    }

    private TextPosition PointToPosition(Point pt)
    {
        if (_lineHeight <= 0 || _charWidth <= 0) return _caret;
        var line = Math.Clamp((pt.Y + _scrollY) / _lineHeight, 0, _buffer.LineCount - 1);
        var column = (int)Math.Round((pt.X + _scrollX) / _charWidth);
        var lineText = _buffer.GetLine(line);
        column = Math.Clamp(column, 0, lineText.Length);
        // Never let the caret land inside a surrogate pair (e.g. an emoji) - snap back to its start.
        if (column > 0 && column < lineText.Length &&
            char.IsLowSurrogate(lineText[column]) && char.IsHighSurrogate(lineText[column - 1]))
            column--;
        return new TextPosition(line, column);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging || e.Button != MouseButtons.Left) return;

        _dragMouseLocation = e.Location;
        MoveCaret(PointToPosition(e.Location), extendSelection: true, ensureVisible: true);

        const int edge = 24;
        var nearEdge = e.Location.Y < edge || e.Location.Y > ClientSize.Height - edge;
        if (nearEdge && !_dragScrollTimer.Enabled) _dragScrollTimer.Start();
        else if (!nearEdge && _dragScrollTimer.Enabled) _dragScrollTimer.Stop();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _isDragging = false;
        _dragScrollTimer.Stop();
    }

    private void OnDragScrollTimerTick(object? sender, EventArgs e)
    {
        const int edge = 24;
        if (_dragMouseLocation.Y < edge) ScrollY -= _lineHeight;
        else if (_dragMouseLocation.Y > ClientSize.Height - edge) ScrollY += _lineHeight;
        else return;
        MoveCaret(PointToPosition(_dragMouseLocation), extendSelection: true, ensureVisible: false);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var steps = e.Delta / SystemInformation.MouseWheelScrollDelta;
        if ((ModifierKeys & Keys.Control) == Keys.Control)
        {
            Zoom(steps);
            return;
        }
        ScrollY -= steps * _lineHeight * 3;
    }

    // -- Caret blink --

    private void ResetCaretBlink()
    {
        _caretVisible = true;
        if (_caretTimer.Enabled)
        {
            _caretTimer.Stop();
            _caretTimer.Start();
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _caretVisible = true;
        var blinkMs = SystemInformation.CaretBlinkTime;
        if (blinkMs > 0)
        {
            _caretTimer.Interval = blinkMs;
            _caretTimer.Start();
        }
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _caretTimer.Stop();
        Invalidate();
    }
}
