namespace CoderCommander.Terminal.Screen;

/// <summary>Cursor rendering shape, set via DECSCUSR (<c>CSI Ps SP q</c>). Blink is tracked
/// separately (<see cref="TerminalModes.CursorBlink"/>) since DECSCUSR's Ps values fold both
/// together (0/1=blinking block, 2=steady block, 3/4=blinking/steady underline, 5/6=blinking/steady
/// bar).</summary>
internal enum CursorShape
{
    Block,
    Underline,
    Bar
}

/// <summary>Terminal-wide mode flags set/reset via CSI ?h/?l (DEC private) and CSI h/l (ANSI) -
/// deliberately a plain class with named bools rather than a bitfield enum, since callers need to
/// query/toggle individual modes by meaning, not iterate a bitset.</summary>
internal sealed class TerminalModes
{
    /// <summary>DECCKM (?1) - arrow keys send application (ESC O x) vs normal (ESC [ x) sequences.
    /// Read by the input encoder, not the screen itself.</summary>
    public bool ApplicationCursorKeys;

    /// <summary>DECKPAM/DECKPNM (ESC =/&gt;), not a CSI mode - numeric vs application keypad.</summary>
    public bool ApplicationKeypad;

    /// <summary>DECSCNM (?5) - swap default fg/bg screen-wide.</summary>
    public bool ScreenReverse;

    /// <summary>DECOM (?6) - cursor addressing relative to the scroll region.</summary>
    public bool OriginMode;

    /// <summary>DECAWM (?7) - default on.</summary>
    public bool AutoWrap = true;

    public bool CursorBlink = true;

    /// <summary>DECTCEM (?25) - default on.</summary>
    public bool CursorVisible = true;

    /// <summary>DECSCUSR (<c>CSI Ps SP q</c>) - default block.</summary>
    public CursorShape CursorShape = CursorShape.Block;

    /// <summary>9 - X10 mouse reporting (click only, no release/motion).</summary>
    public bool MouseX10;

    /// <summary>1000 - VT200 mouse reporting (click + release).</summary>
    public bool MouseVt200;

    /// <summary>1002 - button-event tracking (adds motion while a button is held).</summary>
    public bool MouseButtonEvent;

    /// <summary>1003 - any-event tracking (motion reported even with no button held).</summary>
    public bool MouseAnyEvent;

    /// <summary>1004 - focus in/out reporting.</summary>
    public bool FocusReporting;

    /// <summary>1006 - SGR extended mouse coordinate encoding.</summary>
    public bool MouseSgr;

    /// <summary>1007 - forward the wheel as arrow keys when the alt screen has no scrollback of
    /// its own to scroll (what makes the wheel work in less/man). Default on.</summary>
    public bool AlternateScroll = true;

    /// <summary>2004 - wrap pasted text in ESC[200~ / ESC[201~.</summary>
    public bool BracketedPaste;

    /// <summary>IRM (4) - insert vs replace mode for printable characters.</summary>
    public bool InsertMode;

    /// <summary>LNM (20) - CR alone also implies LF.</summary>
    public bool NewlineMode;

    public bool MouseTrackingEnabled => MouseX10 || MouseVt200 || MouseButtonEvent || MouseAnyEvent;

    public void ResetToDefaults()
    {
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        ScreenReverse = false;
        OriginMode = false;
        AutoWrap = true;
        CursorBlink = true;
        CursorVisible = true;
        CursorShape = CursorShape.Block;
        MouseX10 = MouseVt200 = MouseButtonEvent = MouseAnyEvent = false;
        FocusReporting = false;
        MouseSgr = false;
        AlternateScroll = true;
        BracketedPaste = false;
        InsertMode = false;
        NewlineMode = false;
    }
}
