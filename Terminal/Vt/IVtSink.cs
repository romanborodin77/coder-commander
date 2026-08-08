namespace CoderCommander.Terminal.Vt;

/// <summary>
/// Receives dispatched actions from <see cref="VtParser"/>. Implemented by
/// <c>Terminal.Screen.TerminalScreen</c> in production; tests substitute a recording sink that
/// just appends a string per call, which is what makes <c>VtParserSplitTests</c> possible (feed
/// the same input whole vs. split across an arbitrary chunk boundary, compare the recorded
/// action sequence).
/// </summary>
internal interface IVtSink
{
    /// <summary>A printable character (already resolved to a full Unicode code point - the
    /// parser holds a pending high surrogate internally so this is never called with half of a
    /// surrogate pair).</summary>
    void Print(int rune);

    /// <summary>A C0 control character executed immediately (BS, HT, LF, VT, FF, CR, BEL, SO, SI).</summary>
    void Execute(char c0);

    /// <summary>A non-CSI escape sequence (ESC followed by intermediates then a final byte),
    /// e.g. ESC 7 (DECSC), ESC c (RIS), ESC ( B (charset designation).</summary>
    void EscDispatch(char finalByte, ReadOnlySpan<char> intermediates);

    /// <summary>A CSI sequence. <paramref name="privateMarker"/> is one of '&lt;' '=' '&gt;' '?'
    /// or '\0'. <paramref name="parameters"/> and <paramref name="subParamStart"/> together
    /// encode colon-separated sub-parameters: subParamStart[i] is true when parameters[i] begins
    /// a new top-level parameter (false when it's a colon-separated continuation of the previous
    /// one, e.g. the "2", "255", "0", "0" in "38:2:255:0:0" after the leading "38").</summary>
    void CsiDispatch(char finalByte, ReadOnlySpan<char> intermediates, char privateMarker,
        ReadOnlySpan<int> parameters, ReadOnlySpan<bool> subParamStart);

    /// <summary>A complete OSC sequence's payload (without the leading "]" or trailing
    /// terminator). Terminated by BEL or ST (ESC \).</summary>
    void OscDispatch(ReadOnlySpan<char> data);

    /// <summary>DCS sequence header dispatched once its final byte arrives; <see cref="DcsPut"/>
    /// calls follow with the passthrough data until <see cref="DcsUnhook"/>. Nothing in our
    /// supported scope actually needs DCS passthrough content, but the hook is here so an
    /// unrecognized DCS (which a real shell can legitimately emit, e.g. a terminfo query) is
    /// consumed correctly instead of corrupting parser state.</summary>
    void DcsHook(char finalByte, ReadOnlySpan<char> intermediates, char privateMarker, ReadOnlySpan<int> parameters);

    void DcsPut(char c);

    void DcsUnhook();
}
