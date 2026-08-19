using System.Text;

namespace CoderCommander.Terminal.Vt;

/// <summary>
/// DEC ANSI compatible state machine (the structure Paul Williams documented at
/// vt100.net/emu/dec_ansi_parser, as implemented by every mainstream terminal emulator). Parses
/// a stream of already-UTF-8-decoded <see cref="char"/>s (see <see cref="Utf8ChunkDecoder"/> for
/// the byte-to-char stage) into dispatched actions on an <see cref="IVtSink"/>.
/// <para>
/// <b>Resumability:</b> the whole point of a byte/char-at-a-time automaton with no lookahead and
/// no whole-sequence buffering is that correctness across an arbitrary chunk boundary falls out
/// "for free" - a chunk boundary is indistinguishable from any other point between two
/// characters, as long as the same <see cref="VtParser"/> instance keeps its state across calls
/// (never construct a fresh one per chunk). <c>VtParserSplitTests</c> verifies this directly by
/// feeding the same corpus whole vs. split at every possible index.
/// </para>
/// <para>
/// Zero allocation per character in steady state: parameter/intermediate buffers are fixed-size
/// arrays reused across sequences, and the OSC/DCS string buffer is a reused
/// <see cref="StringBuilder"/>.
/// </para>
/// </summary>
internal sealed class VtParser
{
    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        CsiIgnore,
        DcsEntry,
        DcsParam,
        DcsIntermediate,
        DcsPassthrough,
        DcsIgnore,
        OscString,
        SosPmApcString
    }

    private State _state = State.Ground;

    // True immediately after seeing ESC while inside a string state (Osc/DcsPassthrough/
    // DcsIgnore/SosPmApc) - the next char decides whether this is a real ST ('\') terminating the
    // string, or an unrelated new escape sequence that abandons the string outright (matches real
    // xterm behavior).
    private bool _awaitingStringTerminator;
    private State _stringStateBeforeEscape;
    private int _dcsLength;

    private readonly int[] _params = new int[VtLimits.MaxParams];
    private readonly bool[] _subParamStart = new bool[VtLimits.MaxParams];
    private int _paramCount;

    private readonly char[] _intermediates = new char[2];
    private int _intermediateCount;

    private char _privateMarker;

    private readonly StringBuilder _stringBuf = new(256);
    private bool _stringOverflowed;

    private char _pendingHighSurrogate;

    /// <summary>Resets to the initial (Ground) state - used for RIS (ESC c) and error recovery.
    /// Does not touch any pending high surrogate; a genuinely malformed stream that resets
    /// mid-surrogate-pair simply drops the stray half, which is the correct fail-safe behavior.</summary>
    public void Reset()
    {
        _state = State.Ground;
        _awaitingStringTerminator = false;
        ClearParams();
        ClearString();
    }

    public void Parse(ReadOnlySpan<char> input, IVtSink sink)
    {
        foreach (var c in input)
            ParseChar(c, sink);
    }

    private void ParseChar(char c, IVtSink sink)
    {
        // "Anywhere" transitions that pre-empt normal per-state handling, per the Williams
        // automaton: CAN/SUB abort whatever sequence is in progress and return to Ground.
        if (c is '\x18' or '\x1A')
        {
            if (_state is State.OscString or State.DcsPassthrough or State.DcsIgnore or State.SosPmApcString)
                EndString(sink, dispatch: false);
            sink.Execute(c);
            _state = State.Ground;
            _awaitingStringTerminator = false;
            return;
        }

        switch (_state)
        {
            case State.Ground: HandleGround(c, sink); break;
            case State.Escape: HandleEscape(c, sink); break;
            case State.EscapeIntermediate: HandleEscapeIntermediate(c, sink); break;
            case State.CsiEntry: HandleCsiEntry(c, sink); break;
            case State.CsiParam: HandleCsiParam(c, sink); break;
            case State.CsiIntermediate: HandleCsiIntermediate(c, sink); break;
            case State.CsiIgnore: HandleCsiIgnore(c); break;
            case State.DcsEntry: HandleDcsEntry(c, sink); break;
            case State.DcsParam: HandleDcsParam(c, sink); break;
            case State.DcsIntermediate: HandleDcsIntermediate(c, sink); break;
            case State.DcsPassthrough: HandleDcsPassthrough(c, sink); break;
            case State.DcsIgnore: HandleDcsIgnore(c); break;
            case State.OscString: HandleOscString(c, sink); break;
            case State.SosPmApcString: HandleSosPmApcString(c); break;
        }
    }

    // ── Ground ──────────────────────────────────────────────────────────────────────────────

    private void HandleGround(char c, IVtSink sink)
    {
        if (c == '\x1B') { EnterEscape(); return; }
        if (IsC0Executable(c)) { sink.Execute(c); return; }
        if (c == '\x7F') return; // DEL - ignore
        Print(c, sink);
    }

    private void Print(char c, IVtSink sink)
    {
        if (char.IsHighSurrogate(c))
        {
            // A stray, unpaired high surrogate (the previous one, if any, was never completed) is
            // dropped rather than mis-combined with whatever comes next.
            _pendingHighSurrogate = c;
            return;
        }

        if (char.IsLowSurrogate(c))
        {
            if (_pendingHighSurrogate != '\0')
            {
                sink.Print(char.ConvertToUtf32(_pendingHighSurrogate, c));
                _pendingHighSurrogate = '\0';
            }
            // A low surrogate with no preceding high surrogate is malformed input - dropped.
            return;
        }

        _pendingHighSurrogate = '\0';
        sink.Print(c);
    }

    // ── Escape ──────────────────────────────────────────────────────────────────────────────

    private void EnterEscape()
    {
        _state = State.Escape;
        ClearParams();
        _intermediateCount = 0;
    }

    private void HandleEscape(char c, IVtSink sink)
    {
        if (_awaitingStringTerminator)
        {
            ResolveStringTerminatorEscape(c, sink);
            return;
        }

        if (c == '\x1B') { EnterEscape(); return; } // ESC ESC restarts
        if (IsC0Executable(c)) { sink.Execute(c); return; }
        if (c == '\x7F') return;

        if (c is >= '\x20' and <= '\x2F') { Collect(c); _state = State.EscapeIntermediate; return; }

        switch (c)
        {
            case 'P': _state = State.DcsEntry; ClearString(); return;
            case '[': _state = State.CsiEntry; return;
            case ']': _state = State.OscString; ClearString(); return;
            case 'X': case '^': case '_': _state = State.SosPmApcString; ClearString(); return;
        }

        if (c is >= '\x30' and <= '\x7E')
        {
            sink.EscDispatch(c, _intermediates.AsSpan(0, _intermediateCount));
            _state = State.Ground;
            return;
        }
        // Anything else (e.g. a stray high codepoint) - ignore and stay put; malformed input.
    }

    private void HandleEscapeIntermediate(char c, IVtSink sink)
    {
        if (c == '\x1B') { EnterEscape(); return; }
        if (IsC0Executable(c)) { sink.Execute(c); return; }
        if (c == '\x7F') return;
        if (c is >= '\x20' and <= '\x2F') { Collect(c); return; }
        if (c is >= '\x30' and <= '\x7E')
        {
            sink.EscDispatch(c, _intermediates.AsSpan(0, _intermediateCount));
            _state = State.Ground;
        }
    }

    // ── CSI ─────────────────────────────────────────────────────────────────────────────────

    private void HandleCsiEntry(char c, IVtSink sink)
    {
        if (c == '\x1B') { EnterEscape(); return; }
        if (IsC0Executable(c)) { sink.Execute(c); return; }
        if (c == '\x7F') return;

        if (c is >= '\x20' and <= '\x2F') { Collect(c); _state = State.CsiIntermediate; return; }
        if (c is >= '0' and <= '9') { ParamDigit(c); _state = State.CsiParam; return; }
        if (c == ';') { ParamSeparator(subParam: false); _state = State.CsiParam; return; }
        if (c == ':') { ParamSeparator(subParam: true); _state = State.CsiParam; return; }
        if (c is >= '\x3C' and <= '\x3F') { _privateMarker = c; _state = State.CsiParam; return; }

        if (c is >= '\x40' and <= '\x7E')
        {
            sink.CsiDispatch(c, _intermediates.AsSpan(0, _intermediateCount), _privateMarker,
                _params.AsSpan(0, _paramCount), _subParamStart.AsSpan(0, _paramCount));
            _state = State.Ground;
            return;
        }

        _state = State.CsiIgnore;
    }

    private void HandleCsiParam(char c, IVtSink sink)
    {
        if (c == '\x1B') { EnterEscape(); return; }
        if (IsC0Executable(c)) { sink.Execute(c); return; }
        if (c == '\x7F') return;

        if (c is >= '0' and <= '9') { ParamDigit(c); return; }
        if (c == ';') { ParamSeparator(subParam: false); return; }
        if (c == ':') { ParamSeparator(subParam: true); return; }
        if (c is >= '\x20' and <= '\x2F') { Collect(c); _state = State.CsiIntermediate; return; }

        if (c is >= '\x40' and <= '\x7E')
        {
            sink.CsiDispatch(c, _intermediates.AsSpan(0, _intermediateCount), _privateMarker,
                _params.AsSpan(0, _paramCount), _subParamStart.AsSpan(0, _paramCount));
            _state = State.Ground;
            return;
        }

        // 0x3C-0x3F (another private marker) here, or anything else unexpected - malformed.
        _state = State.CsiIgnore;
    }

    private void HandleCsiIntermediate(char c, IVtSink sink)
    {
        if (c == '\x1B') { EnterEscape(); return; }
        if (IsC0Executable(c)) { sink.Execute(c); return; }
        if (c == '\x7F') return;
        if (c is >= '\x20' and <= '\x2F') { Collect(c); return; }

        if (c is >= '\x40' and <= '\x7E')
        {
            sink.CsiDispatch(c, _intermediates.AsSpan(0, _intermediateCount), _privateMarker,
                _params.AsSpan(0, _paramCount), _subParamStart.AsSpan(0, _paramCount));
            _state = State.Ground;
            return;
        }

        _state = State.CsiIgnore;
    }

    private void HandleCsiIgnore(char c)
    {
        if (c == '\x1B') { EnterEscape(); return; }
        if (c is >= '\x40' and <= '\x7E') { _state = State.Ground; return; }
        // Everything else (including C0s, per the strict automaton these should still execute,
        // but a malformed/overlong CSI is already being discarded - not worth the complexity of
        // also executing C0s mid-discard).
    }

    // ── DCS ─────────────────────────────────────────────────────────────────────────────────

    private void HandleDcsEntry(char c, IVtSink sink)
    {
        if (c == '\x1B') { EnterEscape(); return; }
        if (c == '\x7F' || IsC0Executable(c)) return; // ignored inside DCS, not executed

        if (c is >= '\x20' and <= '\x2F') { Collect(c); _state = State.DcsIntermediate; return; }
        if (c is >= '0' and <= '9') { ParamDigit(c); _state = State.DcsParam; return; }
        if (c == ';') { ParamSeparator(subParam: false); _state = State.DcsParam; return; }
        if (c == ':') { ParamSeparator(subParam: true); _state = State.DcsParam; return; }
        if (c is >= '\x3C' and <= '\x3F') { _privateMarker = c; _state = State.DcsParam; return; }

        if (c is >= '\x40' and <= '\x7E')
        {
            sink.DcsHook(c, _intermediates.AsSpan(0, _intermediateCount), _privateMarker, _params.AsSpan(0, _paramCount));
            _dcsLength = 0;
            _state = State.DcsPassthrough;
            return;
        }

        _state = State.DcsIgnore;
    }

    private void HandleDcsParam(char c, IVtSink sink)
    {
        if (c == '\x1B') { EnterEscape(); return; }
        if (c == '\x7F' || IsC0Executable(c)) return;

        if (c is >= '0' and <= '9') { ParamDigit(c); return; }
        if (c == ';') { ParamSeparator(subParam: false); return; }
        if (c == ':') { ParamSeparator(subParam: true); return; }
        if (c is >= '\x20' and <= '\x2F') { Collect(c); _state = State.DcsIntermediate; return; }

        if (c is >= '\x40' and <= '\x7E')
        {
            sink.DcsHook(c, _intermediates.AsSpan(0, _intermediateCount), _privateMarker, _params.AsSpan(0, _paramCount));
            _dcsLength = 0;
            _state = State.DcsPassthrough;
            return;
        }

        _state = State.DcsIgnore;
    }

    private void HandleDcsIntermediate(char c, IVtSink sink)
    {
        if (c == '\x1B') { EnterEscape(); return; }
        if (c == '\x7F' || IsC0Executable(c)) return;
        if (c is >= '\x20' and <= '\x2F') { Collect(c); return; }

        if (c is >= '\x40' and <= '\x7E')
        {
            sink.DcsHook(c, _intermediates.AsSpan(0, _intermediateCount), _privateMarker, _params.AsSpan(0, _paramCount));
            _dcsLength = 0;
            _state = State.DcsPassthrough;
            return;
        }

        _state = State.DcsIgnore;
    }

    private void HandleDcsPassthrough(char c, IVtSink sink)
    {
        if (c == '\x1B') { _awaitingStringTerminator = true; _stringStateBeforeEscape = State.DcsPassthrough; _state = State.Escape; return; }
        if (c == '\x7F') return;
        if (IsC0Executable(c)) return; // ignored, not executed, while passing through
        if (_dcsLength < VtLimits.MaxDcsLength)
        {
            sink.DcsPut(c);
            _dcsLength++;
        }
        else
        {
            // Cap exceeded — discard further DCS data until ST, same as DcsIgnore.
            sink.DcsUnhook();
            _dcsLength = 0;
            _state = State.DcsIgnore;
        }
    }

    private void HandleDcsIgnore(char c)
    {
        if (c == '\x1B') { _awaitingStringTerminator = true; _stringStateBeforeEscape = State.DcsIgnore; _state = State.Escape; return; }
        // Everything else discarded.
    }

    // ── OSC ─────────────────────────────────────────────────────────────────────────────────

    private void HandleOscString(char c, IVtSink sink)
    {
        if (c == '\x1B') { _awaitingStringTerminator = true; _stringStateBeforeEscape = State.OscString; _state = State.Escape; return; }
        if (c == '\a') { EndString(sink, dispatch: true); _state = State.Ground; return; } // BEL terminator
        if (c is '\x18' or '\x1A') return; // handled by the anywhere-check before we get here
        if (c is < '\x20' or '\x7F') return; // other C0s ignored inside OSC

        AppendStringChar(c);
    }

    // ── SOS/PM/APC ──────────────────────────────────────────────────────────────────────────

    private void HandleSosPmApcString(char c)
    {
        if (c == '\x1B') { _awaitingStringTerminator = true; _stringStateBeforeEscape = State.SosPmApcString; _state = State.Escape; return; }
        // Entirely discarded content - only the length cap matters, to bound how long we stay in
        // this state on a pathological stream with no terminator.
        if (_stringBuf.Length < VtLimits.MaxApcPmSosLength) _stringBuf.Append(c);
    }

    // ── String terminator resolution (shared by Osc/DcsPassthrough/DcsIgnore/SosPmApc) ────────

    private void ResolveStringTerminatorEscape(char c, IVtSink sink)
    {
        _awaitingStringTerminator = false;

        if (c == '\\')
        {
            // Confirmed ST (ESC \) - finalize whatever string sequence was in progress.
            // EndString's own dispatch=false branch is what calls DcsUnhook() for DcsPassthrough;
            // SosPmApcString/DcsIgnore need neither a dispatch nor an unhook, just a clear.
            if (_stringStateBeforeEscape == State.OscString)
                EndString(sink, dispatch: true);
            else if (_stringStateBeforeEscape == State.DcsPassthrough)
                EndString(sink, dispatch: false);
            else
                ClearString();
            _state = State.Ground;
            return;
        }

        // Not a real ST - the string is abandoned (matches xterm), and this ESC starts a fresh
        // sequence of its own. Re-dispatch c through normal Escape-state handling.
        ClearString();
        if (_stringStateBeforeEscape == State.DcsPassthrough) sink.DcsUnhook();
        EnterEscape();
        HandleEscape(c, sink);
    }

    private void EndString(IVtSink sink, bool dispatch)
    {
        if (dispatch && !_stringOverflowed)
        {
            sink.OscDispatch(_stringBuf.ToString().AsSpan());
        }
        else if (_stringStateBeforeEscape == State.DcsPassthrough)
        {
            sink.DcsUnhook();
        }
        ClearString();
    }

    // ── Param/intermediate helpers ──────────────────────────────────────────────────────────

    private void ClearParams()
    {
        _paramCount = 0;
        _privateMarker = '\0';
        Array.Clear(_params);
        Array.Clear(_subParamStart);
    }

    private void Collect(char c)
    {
        if (_intermediateCount < _intermediates.Length)
            _intermediates[_intermediateCount++] = c;
        // Beyond capacity: silently dropped - two intermediates is already generous for anything
        // in our supported CSI/ESC scope.
    }

    private void ParamDigit(char c)
    {
        if (_paramCount == 0)
        {
            _paramCount = 1;
            _subParamStart[0] = true;
        }

        var idx = _paramCount - 1;
        var digit = c - '0';
        var next = _params[idx] * 10 + digit;
        // Clamp DURING accumulation - clamping only after would let a long enough digit run
        // overflow a 32-bit int first (CSI 99999999999999b).
        _params[idx] = next > VtLimits.MaxParamValue || next < _params[idx] ? VtLimits.MaxParamValue : next;
    }

    private void ParamSeparator(bool subParam)
    {
        if (_paramCount >= VtLimits.MaxParams)
            return; // extra params beyond the cap are parsed-and-dropped, not stored

        // A separator before any digit means the current param is empty (default). If _paramCount
        // is 0, we need to account for that implicit empty param: increment to 1 for the empty one,
        // then to 2 for the new slot. Otherwise just open a new slot.
        if (_paramCount == 0)
            _paramCount = 2;
        else
            _paramCount++;
        _subParamStart[_paramCount - 1] = !subParam;
    }

    private void ClearString()
    {
        _stringBuf.Clear();
        _stringOverflowed = false;
    }

    private void AppendStringChar(char c)
    {
        if (_stringBuf.Length >= VtLimits.MaxOscLength)
        {
            _stringOverflowed = true;
            return;
        }
        _stringBuf.Append(c);
    }

    private static bool IsC0Executable(char c) =>
        c is '\a' or '\b' or '\t' or '\n' or '\v' or '\f' or '\r' or '\x0E' or '\x0F';
    // BEL, BS, HT, LF, VT, FF, CR, SO, SI. (BEL is only "executed" here when reached from a state
    // that treats it as a plain C0, i.e. Ground/Escape/Csi* - HandleOscString intercepts BEL
    // itself before this helper would ever be consulted for it there.)
}
