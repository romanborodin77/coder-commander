using CoderCommander.Terminal.Vt;

namespace CoderCommander.Terminal.Screen;

/// <summary>
/// Owns the main + alt screen buffers, cursor, modes, and dirty tracking for one terminal
/// session, and is the <see cref="IVtSink"/> that <see cref="VtParser"/> dispatches into. Mutated
/// from the pty's reader thread (see <c>Terminal.Native.PtySession</c>) via
/// <c>Terminal.TerminalSession</c>, which takes <see cref="SyncRoot"/> around every dispatch call
/// and around <see cref="Resize"/> - the UI layer must take the same lock around every read
/// (painting, cursor position, scrollback, <see cref="Dirty"/>).
/// </summary>
internal sealed class TerminalScreen : IVtSink
{
    private readonly TerminalBuffer _main;
    private readonly TerminalBuffer _alt;
    private TerminalBuffer _active;
    private bool _usingAlt;

    private CursorState _cursor;
    private CursorState _savedCursorMain;
    private CursorState _savedCursorAlt;
    private CursorState _cursorBeforeAlt1049;

    private int _lastPrintedRune = -1;
    private readonly VtResponder _responder;

    // OSC 8 hyperlinks - id 0 means "no link". Deduplicated by URI (not just id: real-world output
    // - e.g. ls with a file:// URI on every line - reopens the "same" link constantly) and capped
    // so a pathological amount of distinct URIs can't grow this unboundedly for the life of a
    // long-running session; once full, new distinct URIs simply aren't clickable.
    private const int MaxHyperlinks = 4096;
    private ushort _currentLinkId;
    private ushort _nextHyperlinkId = 1;
    private readonly Dictionary<ushort, string> _hyperlinksById = new();
    private readonly Dictionary<string, ushort> _hyperlinkIdsByUri = new();

    /// <summary>Guards every mutation (via <see cref="Vt.VtParser.Parse"/>'s dispatch into this
    /// sink, and <see cref="Resize"/>) and every read (painting, cursor position, scrollback) of
    /// this screen's buffers/cursor/modes. Mutation happens on the pty reader thread; reads happen
    /// on the UI thread - callers on both sides must take this lock, this class does not take it
    /// internally (its own dispatch methods are only ever called already-locked, by
    /// <c>Terminal.TerminalSession</c>).</summary>
    public object SyncRoot { get; } = new();

    public TerminalModes Modes { get; } = new();
    public DirtyRows Dirty { get; private set; }
    public string Title { get; private set; } = "";

    public bool IsAltScreenActive => _usingAlt;
    public int CursorRow => _cursor.Row;
    public int CursorCol => _cursor.Col;
    public bool CursorPendingWrap => _cursor.PendingWrap;
    public int Rows => _active.Rows;
    public int Cols => _active.Cols;
    public int ScrollbackCount => _main.Scrollback?.Count ?? 0;

    /// <summary>Monotonic total (never plateaus once the ring is full, unlike
    /// <see cref="ScrollbackCount"/>) - lets a scrolled-back viewport detect "old rows kept getting
    /// evicted while I was looking at a fixed index" and re-anchor. See
    /// <see cref="ScrollbackRing.TotalPushed"/>.</summary>
    public long ScrollbackTotalPushed => _main.Scrollback?.TotalPushed ?? 0;

    /// <summary>Raised when OSC 0/1/2 sets the tab title (already sanitized).</summary>
    public event Action? TitleChanged;

    /// <summary>Raised when OSC 7 or OSC 9;9 reports a validated, existing Windows directory.</summary>
    public event Action<string>? CwdReported;

    private readonly Func<string, string?>? _osc7PosixPathTranslator;

    /// <param name="osc7PosixPathTranslator">Optional - for a shell whose OSC 7 payload is a POSIX
    /// path rather than a Windows one (WSL), translates it before <see cref="CwdReport"/> tries to
    /// resolve it as a plain Windows path. See <see cref="CwdReport.TryParseOsc7(ReadOnlySpan{char},System.Func{string,string?},out string)"/>.</param>
    public TerminalScreen(int rows, int cols, int scrollbackLines, Action<byte[]> writeToPty,
        Func<string, string?>? osc7PosixPathTranslator = null)
    {
        _main = new TerminalBuffer(rows, cols, withScrollback: true, scrollbackLines, CellColor.Default);
        _alt = new TerminalBuffer(rows, cols, withScrollback: false, 0, CellColor.Default);
        _active = _main;
        _cursor = CursorState.Initial(CellColor.Default, CellColor.Default);
        _savedCursorMain = _cursor;
        _savedCursorAlt = _cursor;
        _cursorBeforeAlt1049 = _cursor;
        _responder = new VtResponder(writeToPty);
        Dirty = new DirtyRows(rows);
        _osc7PosixPathTranslator = osc7PosixPathTranslator;
    }

    public TerminalRow GetRow(int row) => _active[row];
    public TerminalRow GetScrollbackRow(int index) => _main.Scrollback!.Get(index);

    /// <summary>Resolves a cell's <see cref="TerminalCell.LinkId"/> (OSC 8) to its URI. Id 0 (no
    /// link) and an id from a session with a different/reset link table both return false.</summary>
    public bool TryGetHyperlinkUri(ushort linkId, out string uri)
    {
        if (linkId != 0 && _hyperlinksById.TryGetValue(linkId, out var found))
        {
            uri = found;
            return true;
        }
        uri = "";
        return false;
    }

    /// <summary>Resizes both buffers. Never reflows long lines - ConPTY's own client-side buffer
    /// already reflows and re-emits on resize; reflowing here too would double-correct.</summary>
    public void Resize(int rows, int cols)
    {
        _main.Resize(rows, cols, CellColor.Default);
        _alt.Resize(rows, cols, CellColor.Default);
        ClampCursor();
        Dirty = new DirtyRows(rows);
        Dirty.MarkAll();
    }

    // ── IVtSink: Print / Execute ───────────────────────────────────────────────────────────

    public void Print(int rune)
    {
        var width = CharWidth.Of(rune);
        if (width == 0)
        {
            AttachCombiningToPreviousCell(rune);
            return;
        }

        if (_cursor.PendingWrap)
            WrapNow();

        if (width == 2 && _cursor.Col == _active.Cols - 1)
        {
            // No room for a wide char in the very last column - never split the glyph across a
            // line boundary. Blank the cell either way, but only wrap when DECAWM (AutoWrap) is
            // on - commit 476afa1 made the later newCol >= Cols check below respect AutoWrap but
            // missed this earlier special case, so a DECAWM-off app (mc, vim, any full-screen
            // TUI - exactly what that commit targeted) still got a forced wrap/scroll here. With
            // AutoWrap off there is nowhere to put the wide rune - falling through to the
            // unconditional WriteCell below would write past the last column - so the character
            // is dropped instead, matching xterm's behavior for a wide char that doesn't fit and
            // can't wrap.
            WriteCell(CurrentRow(), _cursor.Col, ' ', 1);
            if (!Modes.AutoWrap) return;
            WrapNow();
        }

        var row = CurrentRow();

        // IRM (Insert Mode, CSI 4h): shift existing content right before writing
        if (Modes.InsertMode)
        {
            var limit = _active.Cols;
            for (var c = limit - 1; c >= _cursor.Col + width; c--)
                row.Cells[c] = row.Cells[c - width];
            row.ClearRange(_cursor.Col, _cursor.Col + width, _cursor.Bg);

            // The shift above moves raw cells - unlike WriteCell (below), it doesn't know about
            // wide-char pairs, so it can strand half of one at either boundary of the moved
            // range: a WideLead can land at the very last column with no room left for its
            // trail, and a WideTrail can land right after the just-cleared gap with its lead
            // (never touched by the shift) stuck on the wrong side of that gap. Either half left
            // in place renders as a garbled/split glyph.
            BlankOrphanedWideHalf(row, limit - 1);
            BlankOrphanedWideHalf(row, _cursor.Col + width);
        }

        WriteCell(row, _cursor.Col, rune, width);
        _lastPrintedRune = rune;

        var newCol = _cursor.Col + width;
        if (newCol >= _active.Cols)
        {
            if (Modes.AutoWrap)
            {
                _cursor.Col = _active.Cols - 1;
                _cursor.PendingWrap = true;
            }
            // else: AutoWrap disabled — cursor stays at right margin, overwrite mode
        }
        else
        {
            _cursor.Col = newCol;
        }
    }

    private void WrapNow()
    {
        _cursor.PendingWrap = false;
        CurrentRow().Wrapped = true;
        LineFeed();
        _cursor.Col = 0;
    }

    private void AttachCombiningToPreviousCell(int rune)
    {
        var col = _cursor.PendingWrap ? _active.Cols - 1 : _cursor.Col - 1;
        if (col < 0) return;

        var row = CurrentRow();
        // After a wide (2-column) character, "the previous cell" by column arithmetic is the
        // WideTrail half of the pair - but WriteCell/DrawCellRun/RowToPlainText all treat WideLead
        // as the glyph's only address (WideTrail contributes nothing on its own, see WriteCell and
        // RowToPlainText's own doc comments). A mark attached to the trail index was never read
        // back by any of them - silently dropped from rendering, Copy, Find and accessible text.
        if (col < row.Cells.Length && (row.Cells[col].Flags & CellFlags.WideTrail) != 0)
            col--;
        if (col < 0) return;

        row.AttachCombining(col, rune, VtLimits.MaxCombiningPerCell);
        MarkRowDirty(_cursor.Row);
    }

    private void WriteCell(TerminalRow row, int col, int rune, int width)
    {
        // Overwriting either half of an existing wide-char pair must blank BOTH halves - an
        // orphaned lead-without-trail (or vice versa) renders as garbage.
        if (col < row.Cells.Length)
        {
            var existing = row.Cells[col];
            if ((existing.Flags & CellFlags.WideLead) != 0 && col + 1 < row.Cells.Length)
                row.Cells[col + 1] = TerminalCell.Blank(_cursor.Bg);
            else if ((existing.Flags & CellFlags.WideTrail) != 0 && col - 1 >= 0)
                row.Cells[col - 1] = TerminalCell.Blank(_cursor.Bg);
        }

        var cell = new TerminalCell { Rune = rune, Fg = _cursor.Fg, Bg = _cursor.Bg, Flags = _cursor.Attrs, LinkId = _currentLinkId };
        if (width == 2)
        {
            cell.Flags |= CellFlags.WideLead;
            row.Cells[col] = cell;
            if (col + 1 < row.Cells.Length)
                row.Cells[col + 1] = new TerminalCell { Fg = _cursor.Fg, Bg = _cursor.Bg, Flags = _cursor.Attrs | CellFlags.WideTrail, LinkId = _currentLinkId };
        }
        else
        {
            row.Cells[col] = cell;
        }

        row.Combining?.Remove(col);
        row.Version++;
        MarkRowDirty(_cursor.Row);
    }

    /// <summary>Blanks <paramref name="col"/> if it holds one half of a wide-char pair whose
    /// other half is no longer adjacent - the same "don't leave an orphaned lead/trail" guard
    /// <see cref="WriteCell"/> applies to an ordinary overwrite, needed here because the IRM
    /// shift in <see cref="Print"/> moves raw cells without going through it.</summary>
    private void BlankOrphanedWideHalf(TerminalRow row, int col)
    {
        if (col < 0 || col >= row.Cells.Length) return;

        var cell = row.Cells[col];
        if ((cell.Flags & CellFlags.WideLead) != 0 &&
            (col + 1 >= row.Cells.Length || (row.Cells[col + 1].Flags & CellFlags.WideTrail) == 0))
        {
            row.Cells[col] = TerminalCell.Blank(_cursor.Bg);
        }
        else if ((cell.Flags & CellFlags.WideTrail) != 0 &&
            (col - 1 < 0 || (row.Cells[col - 1].Flags & CellFlags.WideLead) == 0))
        {
            row.Cells[col] = TerminalCell.Blank(_cursor.Bg);
        }
    }

    public void Execute(char c0)
    {
        switch (c0)
        {
            case '\b':
                if (_cursor.Col > 0) _cursor.Col--;
                _cursor.PendingWrap = false;
                break;
            case '\t':
                AdvanceToNextTabStop();
                break;
            case '\n': case '\v': case '\f':
                LineFeed();
                if (Modes.NewlineMode) _cursor.Col = 0;
                break;
            case '\r':
                _cursor.Col = 0;
                _cursor.PendingWrap = false;
                break;
            case '\a':
                break; // BEL - visual bell hook wired up at the UI layer in a later phase
            case '\x0E':
                _cursor.UsingG1 = true; // SO
                break;
            case '\x0F':
                _cursor.UsingG1 = false; // SI
                break;
        }
    }

    // ── IVtSink: Esc ────────────────────────────────────────────────────────────────────────

    public void EscDispatch(char finalByte, ReadOnlySpan<char> intermediates)
    {
        if (intermediates.Length == 1)
        {
            switch (intermediates[0])
            {
                case '(': _cursor.G0IsDecGraphics = finalByte == '0'; return;
                case ')': _cursor.G1IsDecGraphics = finalByte == '0'; return;
                case '#': if (finalByte == '8') DecAlignmentTest(); return;
            }
        }

        switch (finalByte)
        {
            case '7': SaveCursor(); break;
            case '8': RestoreCursor(); break;
            case 'D': LineFeed(); break; // IND
            case 'E': _cursor.Col = 0; LineFeed(); break; // NEL
            case 'M': ReverseLineFeed(); break; // RI
            case 'H': if (_cursor.Col < _active.TabStops.Length) _active.TabStops[_cursor.Col] = true; break; // HTS
            case 'c': FullReset(); break; // RIS
            case '=': Modes.ApplicationKeypad = true; break;
            case '>': Modes.ApplicationKeypad = false; break;
        }
    }

    private void DecAlignmentTest()
    {
        for (var r = 0; r < _active.Rows; r++)
        {
            var row = _active[r];
            for (var c = 0; c < _active.Cols; c++)
                row.Cells[c] = new TerminalCell { Rune = 'E', Fg = CellColor.Default, Bg = CellColor.Default };
            row.Version++;
        }
        Dirty.MarkAll();
    }

    private void SaveCursor()
    {
        if (_usingAlt) _savedCursorAlt = _cursor;
        else _savedCursorMain = _cursor;
    }

    private void RestoreCursor()
    {
        _cursor = _usingAlt ? _savedCursorAlt : _savedCursorMain;
        ClampCursor();
    }

    // ── IVtSink: Csi ────────────────────────────────────────────────────────────────────────

    public void CsiDispatch(char finalByte, ReadOnlySpan<char> intermediates, char privateMarker,
        ReadOnlySpan<int> parameters, ReadOnlySpan<bool> subParamStart)
    {
        switch (finalByte)
        {
            case 'A': MoveCursorRelative(-Count1(parameters), 0); break;
            case 'B': MoveCursorRelative(Count1(parameters), 0); break;
            case 'C': MoveCursorRelative(0, Count1(parameters)); break;
            case 'D': MoveCursorRelative(0, -Count1(parameters)); break;
            case 'E': MoveCursorRelative(Count1(parameters), 0); _cursor.Col = 0; break;
            case 'F': MoveCursorRelative(-Count1(parameters), 0); _cursor.Col = 0; break;
            case 'G': case '`': _cursor.Col = Math.Clamp(Param1Based(parameters, 0) - 1, 0, _active.Cols - 1); _cursor.PendingWrap = false; break;
            case 'a': MoveCursorRelative(0, Count1(parameters)); break;
            case 'H': case 'f': MoveCursorAbsolute(Param1Based(parameters, 0) - 1, Param1Based(parameters, 1) - 1); break;
            case 'd': MoveCursorAbsoluteRow(Param1Based(parameters, 0) - 1); break;
            case 'e': MoveCursorRelative(Count1(parameters), 0); break;
            case 'I': TabForward(Count1(parameters)); break;
            case 'Z': TabBackward(Count1(parameters)); break;
            case 'J': HandleEraseDisplay(parameters); break;
            case 'K': HandleEraseLine(parameters); break;
            case 'L': _active.InsertLinesAt(_cursor.Row, Count1(parameters)); Dirty.MarkAll(); break;
            case 'M': _active.DeleteLinesAt(_cursor.Row, Count1(parameters)); Dirty.MarkAll(); break;
            case '@': InsertChars(Count1(parameters)); break;
            case 'P': DeleteChars(Count1(parameters)); break;
            case 'X': EraseChars(Count1(parameters)); break;
            case 'S': _active.ScrollRegionUp(Count1(parameters)); Dirty.MarkAll(); break;
            case 'T': _active.ScrollRegionDown(Count1(parameters)); Dirty.MarkAll(); break;
            case 'b': RepeatLastChar(Count1(parameters)); break;
            case 'c': _responder.HandleDeviceAttributes(privateMarker); break;
            case 'g': HandleTabClear(parameters); break;
            case 'h': SetMode(privateMarker, parameters, enable: true); break;
            case 'l': SetMode(privateMarker, parameters, enable: false); break;
            case 'm': HandleSgr(parameters, subParamStart); break;
            case 'n': if (privateMarker == '\0') _responder.HandleDeviceStatusReport(parameters, _cursor.Row, _cursor.Col); break;
            case 'r': HandleSetScrollRegion(parameters); break;
            case 's': if (privateMarker == '\0') SaveCursorPositionOnly(); break;
            case 'u': if (privateMarker == '\0') RestoreCursorPositionOnly(); break;
            case 'p': if (intermediates.Length == 1 && intermediates[0] == '!') SoftReset(); break;
            case 'q': if (intermediates.Length == 1 && intermediates[0] == ' ') SetCursorStyle(Count1(parameters)); break;
            // DECRQM (CSI $p), XTWINOPS (CSI t), DECRQSS, and anything else recognized-but-
            // unimplemented falls through here as a deliberate no-op - see VtResponder's doc
            // comment for why "never call the responder" is itself the refusal.
            default: break;
        }
    }

    private static int Count1(ReadOnlySpan<int> p) => p.Length > 0 && p[0] > 0 ? p[0] : 1;
    private static int Param1Based(ReadOnlySpan<int> p, int index) => index < p.Length && p[index] > 0 ? p[index] : 1;

    private void MoveCursorRelative(int rows, int cols)
    {
        _cursor.Row = Math.Clamp(_cursor.Row + rows, 0, _active.Rows - 1);
        _cursor.Col = Math.Clamp(_cursor.Col + cols, 0, _active.Cols - 1);
        _cursor.PendingWrap = false;
    }

    private void MoveCursorAbsolute(int row, int col)
    {
        var rowBase = Modes.OriginMode ? _active.ScrollTop : 0;
        var rowLimit = Modes.OriginMode ? _active.ScrollBottom : _active.Rows - 1;
        _cursor.Row = Math.Clamp(rowBase + row, rowBase, rowLimit);
        _cursor.Col = Math.Clamp(col, 0, _active.Cols - 1);
        _cursor.PendingWrap = false;
    }

    private void MoveCursorAbsoluteRow(int row)
    {
        var rowBase = Modes.OriginMode ? _active.ScrollTop : 0;
        var rowLimit = Modes.OriginMode ? _active.ScrollBottom : _active.Rows - 1;
        _cursor.Row = Math.Clamp(rowBase + row, rowBase, rowLimit);
        _cursor.PendingWrap = false;
    }

    private void ClampCursor()
    {
        _cursor.Row = Math.Clamp(_cursor.Row, 0, _active.Rows - 1);
        _cursor.Col = Math.Clamp(_cursor.Col, 0, _active.Cols - 1);
        _cursor.PendingWrap = false;
    }

    private TerminalRow CurrentRow() => _active[_cursor.Row];
    private void MarkRowDirty(int row) => Dirty.MarkRow(row);

    private void LineFeed()
    {
        if (_cursor.Row == _active.ScrollBottom)
            _active.ScrollRegionUp();
        else if (_cursor.Row < _active.Rows - 1)
            _cursor.Row++;
        _cursor.PendingWrap = false;
        Dirty.MarkAll();
    }

    private void ReverseLineFeed()
    {
        if (_cursor.Row == _active.ScrollTop)
            _active.ScrollRegionDown();
        else if (_cursor.Row > 0)
            _cursor.Row--;
        _cursor.PendingWrap = false;
        Dirty.MarkAll();
    }

    private void AdvanceToNextTabStop()
    {
        var col = _cursor.Col + 1;
        while (col < _active.Cols - 1 && !_active.TabStops[col]) col++;
        _cursor.Col = Math.Min(col, _active.Cols - 1);
        _cursor.PendingWrap = false;
    }

    private void TabForward(int n)
    {
        for (var k = 0; k < n; k++) AdvanceToNextTabStop();
    }

    private void TabBackward(int n)
    {
        for (var k = 0; k < n; k++)
        {
            var col = _cursor.Col - 1;
            while (col > 0 && !_active.TabStops[col]) col--;
            _cursor.Col = Math.Max(col, 0);
        }
        _cursor.PendingWrap = false;
    }

    private void HandleTabClear(ReadOnlySpan<int> parameters)
    {
        var mode = parameters.Length > 0 ? parameters[0] : 0;
        if (mode == 0) { if (_cursor.Col < _active.TabStops.Length) _active.TabStops[_cursor.Col] = false; }
        else if (mode == 3) Array.Clear(_active.TabStops);
    }

    private void HandleEraseDisplay(ReadOnlySpan<int> parameters)
    {
        var mode = parameters.Length > 0 ? parameters[0] : 0;
        switch (mode)
        {
            case 0:
                CurrentRow().ClearRange(_cursor.Col, _active.Cols, _cursor.Bg);
                for (var r = _cursor.Row + 1; r < _active.Rows; r++) _active[r].ClearAll(_cursor.Bg);
                break;
            case 1:
                CurrentRow().ClearRange(0, _cursor.Col + 1, _cursor.Bg);
                for (var r = 0; r < _cursor.Row; r++) _active[r].ClearAll(_cursor.Bg);
                break;
            case 2:
                for (var r = 0; r < _active.Rows; r++) _active[r].ClearAll(_cursor.Bg);
                break;
            case 3:
                // xterm extension ("erase saved lines") - scrollback ONLY, visible screen untouched.
                _active.Scrollback?.Clear();
                break;
        }
        Dirty.MarkAll();
    }

    private void HandleEraseLine(ReadOnlySpan<int> parameters)
    {
        var mode = parameters.Length > 0 ? parameters[0] : 0;
        switch (mode)
        {
            case 0: CurrentRow().ClearRange(_cursor.Col, _active.Cols, _cursor.Bg); break;
            case 1: CurrentRow().ClearRange(0, _cursor.Col + 1, _cursor.Bg); break;
            case 2: CurrentRow().ClearAll(_cursor.Bg); break;
        }
        MarkRowDirty(_cursor.Row);
    }

    private void InsertChars(int n)
    {
        var row = CurrentRow();
        var limit = _active.Cols;
        n = Math.Min(n, limit - _cursor.Col);
        if (n <= 0) return;
        for (var c = limit - 1; c >= _cursor.Col + n; c--)
            row.Cells[c] = row.Cells[c - n];
        row.ClearRange(_cursor.Col, _cursor.Col + n, _cursor.Bg);
        MarkRowDirty(_cursor.Row);
    }

    private void DeleteChars(int n)
    {
        var row = CurrentRow();
        var limit = _active.Cols;
        n = Math.Min(n, limit - _cursor.Col);
        if (n <= 0) return;
        for (var c = _cursor.Col; c < limit - n; c++)
            row.Cells[c] = row.Cells[c + n];
        row.ClearRange(limit - n, limit, _cursor.Bg);
        MarkRowDirty(_cursor.Row);
    }

    private void EraseChars(int n)
    {
        var end = Math.Min(_active.Cols, _cursor.Col + n);
        CurrentRow().ClearRange(_cursor.Col, end, _cursor.Bg);
        MarkRowDirty(_cursor.Row);
    }

    private void RepeatLastChar(int count)
    {
        if (_lastPrintedRune < 0) return;
        for (var k = 0; k < count; k++) Print(_lastPrintedRune);
    }

    private void HandleSetScrollRegion(ReadOnlySpan<int> parameters)
    {
        var top = parameters.Length > 0 && parameters[0] > 0 ? parameters[0] - 1 : 0;
        var bottom = parameters.Length > 1 && parameters[1] > 0 ? parameters[1] - 1 : _active.Rows - 1;
        if (top < bottom && bottom < _active.Rows)
        {
            _active.ScrollTop = top;
            _active.ScrollBottom = bottom;
        }
        _cursor.Row = Modes.OriginMode ? _active.ScrollTop : 0;
        _cursor.Col = 0;
        _cursor.PendingWrap = false;
    }

    private CursorState _scoCursor;
    private bool _scoCursorSaved;

    private void SaveCursorPositionOnly() { _scoCursor = _cursor; _scoCursorSaved = true; }

    private void RestoreCursorPositionOnly()
    {
        if (!_scoCursorSaved) return;
        _cursor.Row = _scoCursor.Row;
        _cursor.Col = _scoCursor.Col;
        ClampCursor();
    }

    private void SoftReset()
    {
        _cursor.Attrs = CellFlags.None;
        _cursor.Fg = CellColor.Default;
        _cursor.Bg = CellColor.Default;
        _cursor.PendingWrap = false;
        Modes.OriginMode = false;
        Modes.CursorVisible = true;
        Modes.CursorShape = CursorShape.Block;
        Modes.CursorBlink = true;
        _active.ScrollTop = 0;
        _active.ScrollBottom = _active.Rows - 1;
    }

    /// <summary>DECSCUSR (<c>CSI Ps SP q</c>): 0/1=blinking block, 2=steady block,
    /// 3=blinking underline, 4=steady underline, 5=blinking bar, 6=steady bar. An out-of-range Ps
    /// is simply ignored - not a reason to reset or throw.</summary>
    private void SetCursorStyle(int ps)
    {
        switch (ps)
        {
            case 0: case 1: Modes.CursorShape = CursorShape.Block; Modes.CursorBlink = true; break;
            case 2: Modes.CursorShape = CursorShape.Block; Modes.CursorBlink = false; break;
            case 3: Modes.CursorShape = CursorShape.Underline; Modes.CursorBlink = true; break;
            case 4: Modes.CursorShape = CursorShape.Underline; Modes.CursorBlink = false; break;
            case 5: Modes.CursorShape = CursorShape.Bar; Modes.CursorBlink = true; break;
            case 6: Modes.CursorShape = CursorShape.Bar; Modes.CursorBlink = false; break;
        }
    }

    private void FullReset()
    {
        _usingAlt = false;
        _active = _main;
        for (var r = 0; r < _main.Rows; r++) _main[r].ClearAll(CellColor.Default);
        for (var r = 0; r < _alt.Rows; r++) _alt[r].ClearAll(CellColor.Default);
        _main.ScrollTop = 0; _main.ScrollBottom = _main.Rows - 1;
        _alt.ScrollTop = 0; _alt.ScrollBottom = _alt.Rows - 1;
        _main.TabStops = TerminalBuffer.BuildDefaultTabStops(_main.Cols);
        _alt.TabStops = TerminalBuffer.BuildDefaultTabStops(_alt.Cols);
        _cursor = CursorState.Initial(CellColor.Default, CellColor.Default);
        _savedCursorMain = _cursor;
        _savedCursorAlt = _cursor;
        _scoCursorSaved = false;
        Modes.ResetToDefaults();
        Title = "";
        _currentLinkId = 0;
        _nextHyperlinkId = 1;
        _hyperlinksById.Clear();
        _hyperlinkIdsByUri.Clear();
        Dirty.MarkAll();
    }

    // ── SGR ─────────────────────────────────────────────────────────────────────────────────

    private void HandleSgr(ReadOnlySpan<int> parameters, ReadOnlySpan<bool> subParamStart)
    {
        if (parameters.Length == 0)
        {
            ResetSgrAttributes();
            return;
        }

        var i = 0;
        while (i < parameters.Length)
        {
            var p = parameters[i];
            switch (p)
            {
                case 0: ResetSgrAttributes(); i++; break;
                case 1: _cursor.Attrs |= CellFlags.Bold; i++; break;
                case 2: _cursor.Attrs |= CellFlags.Dim; i++; break;
                case 3: _cursor.Attrs |= CellFlags.Italic; i++; break;
                case 4: _cursor.Attrs |= CellFlags.Underline; i++; break;
                case 5: case 6: _cursor.Attrs |= CellFlags.Blink; i++; break;
                case 7: _cursor.Attrs |= CellFlags.Reverse; i++; break;
                case 8: _cursor.Attrs |= CellFlags.Invisible; i++; break;
                case 9: _cursor.Attrs |= CellFlags.Strike; i++; break;
                case 21: _cursor.Attrs |= CellFlags.DoubleUnderline; i++; break;
                case 22: _cursor.Attrs &= ~(CellFlags.Bold | CellFlags.Dim); i++; break;
                case 23: _cursor.Attrs &= ~CellFlags.Italic; i++; break;
                case 24: _cursor.Attrs &= ~(CellFlags.Underline | CellFlags.DoubleUnderline); i++; break;
                case 25: _cursor.Attrs &= ~CellFlags.Blink; i++; break;
                case 27: _cursor.Attrs &= ~CellFlags.Reverse; i++; break;
                case 28: _cursor.Attrs &= ~CellFlags.Invisible; i++; break;
                case 29: _cursor.Attrs &= ~CellFlags.Strike; i++; break;
                case 53: _cursor.Attrs |= CellFlags.Overline; i++; break;
                case 55: _cursor.Attrs &= ~CellFlags.Overline; i++; break;
                case >= 30 and <= 37: _cursor.Fg = CellColor.FromIndex((byte)(p - 30)); i++; break;
                case 38: { i += ParseExtendedColor(parameters, subParamStart, i, out var c); _cursor.Fg = c; break; }
                case 39: _cursor.Fg = CellColor.Default; i++; break;
                case >= 40 and <= 47: _cursor.Bg = CellColor.FromIndex((byte)(p - 40)); i++; break;
                case 48: { i += ParseExtendedColor(parameters, subParamStart, i, out var c); _cursor.Bg = c; break; }
                case 49: _cursor.Bg = CellColor.Default; i++; break;
                case >= 90 and <= 97: _cursor.Fg = CellColor.FromIndex((byte)(p - 90 + 8)); i++; break;
                case >= 100 and <= 107: _cursor.Bg = CellColor.FromIndex((byte)(p - 100 + 8)); i++; break;
                default: i++; break;
            }
        }
    }

    private void ResetSgrAttributes()
    {
        _cursor.Attrs = CellFlags.None;
        _cursor.Fg = CellColor.Default;
        _cursor.Bg = CellColor.Default;
    }

    /// <summary>Parses "38/48;5;n" (indexed) or "38/48;2;r;g;b" (RGB), in EITHER the semicolon
    /// form (each value its own top-level param) or the colon form (values attached to the 38/48
    /// group via ':', optionally with an extra colorspace-id field: "38:2::r:g:b"). Returns the
    /// number of parameter slots consumed starting at <paramref name="i"/> (the index of 38/48
    /// itself).</summary>
    private static int ParseExtendedColor(ReadOnlySpan<int> p, ReadOnlySpan<bool> subParamStart, int i, out CellColor color)
    {
        color = CellColor.Default;
        if (i + 1 >= p.Length) return 1;

        var mode = p[i + 1];
        if (mode == 5)
        {
            if (i + 2 >= p.Length) return 2;
            color = CellColor.FromIndex((byte)Math.Clamp(p[i + 2], 0, 255));
            return 3;
        }

        if (mode == 2)
        {
            var attached = 0;
            while (i + 2 + attached < p.Length && (i + 2 + attached >= subParamStart.Length || !subParamStart[i + 2 + attached]))
                attached++;

            if (attached >= 4)
            {
                // Colon form with a colorspace-id field ("38:2:cs:r:g:b" or "38:2::r:g:b" with
                // an empty/omitted cs) - skip the colorspace id, take the next three as r,g,b.
                color = CellColor.FromRgb(Clamp(p[i + 3]), Clamp(p[i + 4]), Clamp(p[i + 5]));
                return 6;
            }
            if (i + 4 < p.Length)
            {
                color = CellColor.FromRgb(Clamp(p[i + 2]), Clamp(p[i + 3]), Clamp(p[i + 4]));
                return 5;
            }
            return 2;
        }

        return 2; // unrecognized mode (e.g. legacy 1/3/4/5 forms) - skip just the mode byte
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);

    // ── Modes ───────────────────────────────────────────────────────────────────────────────

    private void SetMode(char privateMarker, ReadOnlySpan<int> parameters, bool enable)
    {
        foreach (var p in parameters)
        {
            if (privateMarker == '?')
                SetDecPrivateMode(p, enable);
            else
                SetAnsiMode(p, enable);
        }
    }

    private void SetAnsiMode(int p, bool enable)
    {
        switch (p)
        {
            case 4: Modes.InsertMode = enable; break;
            case 20: Modes.NewlineMode = enable; break;
        }
    }

    private void SetDecPrivateMode(int p, bool enable)
    {
        switch (p)
        {
            case 1: Modes.ApplicationCursorKeys = enable; break;
            case 3: break; // DECCOLM - tracked nowhere for v1; no actual 80/132-column resize
            case 5: Modes.ScreenReverse = enable; break;
            case 6:
                Modes.OriginMode = enable;
                _cursor.Row = Modes.OriginMode ? _active.ScrollTop : 0;
                _cursor.Col = 0;
                break;
            case 7: Modes.AutoWrap = enable; break;
            case 9: Modes.MouseX10 = enable; break;
            case 12: Modes.CursorBlink = enable; break;
            case 25: Modes.CursorVisible = enable; break;
            case 47: SetAltScreen(enable, alsoSaveRestoreCursor: false); break;
            case 1000: Modes.MouseVt200 = enable; break;
            case 1002: Modes.MouseButtonEvent = enable; break;
            case 1003: Modes.MouseAnyEvent = enable; break;
            case 1004: Modes.FocusReporting = enable; break;
            case 1006: Modes.MouseSgr = enable; break;
            case 1007: Modes.AlternateScroll = enable; break;
            case 1047: SetAltScreen(enable, alsoSaveRestoreCursor: false); break;
            case 1049: SetAltScreen(enable, alsoSaveRestoreCursor: true); break;
            case 2004: Modes.BracketedPaste = enable; break;
            case 9001:
                // win32-input-mode. ConPTY can ask us to send Win32 input records instead of VT
                // key sequences - recognized here ONLY so it doesn't fall through as an unknown
                // mode, and deliberately NEVER acted on: accidentally behaving as if this were
                // enabled would break all keyboard input, since VtKeyEncoder (added in a later
                // phase) only ever speaks plain VT.
                break;
        }
    }

    private void SetAltScreen(bool enable, bool alsoSaveRestoreCursor)
    {
        if (enable == _usingAlt) return;

        if (enable)
        {
            if (alsoSaveRestoreCursor) _cursorBeforeAlt1049 = _cursor;
            for (var r = 0; r < _alt.Rows; r++) _alt[r].ClearAll(CellColor.Default);
            _active = _alt;
            _usingAlt = true;
        }
        else
        {
            _active = _main;
            _usingAlt = false;
            if (alsoSaveRestoreCursor) _cursor = _cursorBeforeAlt1049;
        }

        _cursor.PendingWrap = false;
        ClampCursor();
        Dirty.MarkAll();
    }

    // ── OSC ─────────────────────────────────────────────────────────────────────────────────

    public void OscDispatch(ReadOnlySpan<char> data)
    {
        var semi = data.IndexOf(';');
        var numPart = semi < 0 ? data : data[..semi];
        if (!int.TryParse(numPart, out var oscNum))
            return;
        var payload = semi < 0 ? ReadOnlySpan<char>.Empty : data[(semi + 1)..];

        switch (oscNum)
        {
            case 0: case 1: case 2:
                Title = OscSanitizer.SanitizeTitle(payload);
                TitleChanged?.Invoke();
                break;
            case 7:
                if (CwdReport.TryParseOsc7(payload, _osc7PosixPathTranslator, out var path7))
                    CwdReported?.Invoke(path7);
                break;
            case 9:
                var semi2 = payload.IndexOf(';');
                if (semi2 >= 0 && payload[..semi2].SequenceEqual("9"))
                {
                    if (CwdReport.TryParseOsc9_9(payload[(semi2 + 1)..], out var path99))
                        CwdReported?.Invoke(path99);
                }
                break;
            case 8:
                HandleHyperlink(payload);
                break;
            // 4 (palette), 10/11/12 (set colors), 52 (clipboard), 133 (shell integration marks) -
            // parsed-and-discarded for v1; wired up in later phases. Every "?"-suffixed query form
            // of any OSC (color queries etc.) is included in that discard - never answered, by the
            // same "never implemented, not individually refused" principle as VtResponder's CSI
            // whitelist.
            default: break;
        }
    }

    /// <summary>OSC 8 (<c>OSC 8 ; params ; URI ST</c>): opens a hyperlink that subsequently
    /// printed cells get tagged with (<see cref="TerminalCell.LinkId"/>), until the next OSC 8
    /// with an empty URI closes it. <c>params</c> (typically <c>id=...</c>) is accepted but not
    /// otherwise interpreted - URIs are deduplicated by their own text regardless of what id the
    /// application asked for.</summary>
    private void HandleHyperlink(ReadOnlySpan<char> payload)
    {
        var semi = payload.IndexOf(';');
        var uri = semi < 0 ? payload : payload[(semi + 1)..];

        if (uri.IsEmpty || uri.Length > VtLimits.MaxOscLength)
        {
            _currentLinkId = 0;
            return;
        }

        var uriString = uri.ToString();
        if (_hyperlinkIdsByUri.TryGetValue(uriString, out var existingId))
        {
            _currentLinkId = existingId;
            return;
        }

        if (_hyperlinksById.Count >= MaxHyperlinks)
        {
            _currentLinkId = 0;
            return;
        }

        var id = _nextHyperlinkId;
        _nextHyperlinkId = (ushort)(_nextHyperlinkId + 1);
        if (_nextHyperlinkId == 0) _nextHyperlinkId = 1;

        // Evict stale entry if this id was already used (after ushort wrap-around)
        if (_hyperlinksById.TryGetValue(id, out var oldUri))
        {
            _hyperlinkIdsByUri.Remove(oldUri);
        }

        _hyperlinksById[id] = uriString;
        _hyperlinkIdsByUri[uriString] = id;
        _currentLinkId = id;
    }

    // ── DCS (consumed by the parser regardless; nothing here needs the passthrough content) ──

    public void DcsHook(char finalByte, ReadOnlySpan<char> intermediates, char privateMarker, ReadOnlySpan<int> parameters) { }
    public void DcsPut(char c) { }
    public void DcsUnhook() { }
}
