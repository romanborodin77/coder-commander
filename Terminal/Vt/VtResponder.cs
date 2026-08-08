using System.Text;

namespace CoderCommander.Terminal.Vt;

/// <summary>
/// Writes the whitelisted set of VT query replies back to the pty's stdin, rate-limited.
/// <para>
/// <b>Threat model:</b> any byte out of the pty can be attacker-controlled, and certain escape
/// sequences make the terminal write bytes back into the pty's stdin, where they land directly
/// on the shell's command line - a reply containing "\r" executes immediately. Because of this,
/// only four replies exist, all with fixed or self-generated numeric content: DA1, DA2, DSR 5,
/// and CPR 6. Everything else that could theoretically be answered (window title reports,
/// DECRQSS, XTGETTCAP, color queries, ...) is <b>never implemented</b> - not refused via an
/// explicit check, simply never called from <c>Terminal.Screen.TerminalScreen</c>'s CSI/OSC
/// dispatch in the first place, which is what makes "answer nothing else" the load-bearing
/// security property rather than something that could be individually gotten wrong. CPR (CSI 6n)
/// is not optional despite the small whitelist: PSReadLine issues it on startup and on every
/// redraw, so PowerShell renders incorrectly or stalls without a reply.
/// </para>
/// </summary>
internal sealed class VtResponder
{
    private readonly Action<byte[]> _write;
    private double _tokens = VtLimits.ResponsesBurst;
    private long _lastRefillTicks = Environment.TickCount64;
    private int _totalResponses;

    public VtResponder(Action<byte[]> write) => _write = write;

    /// <summary>DA1 (CSI c) / DA2 (CSI &gt; c) - fixed content, apps use this to feature-probe.</summary>
    public void HandleDeviceAttributes(char privateMarker)
    {
        TrySend(privateMarker == '>' ? "\x1b[>0;10;1c" : "\x1b[?1;2c");
    }

    /// <summary>DSR (CSI 5n, fixed "OK" reply) and CPR (CSI 6n, reports the CURRENT cursor
    /// position - digits come from the screen's own tracked state, never from anything the
    /// stream itself supplied). Any other DSR variant (e.g. the private-marker DECXCPR "?6n") is
    /// simply not recognized here and gets no reply - the caller only invokes this for the
    /// plain (no private marker) form.</summary>
    public void HandleDeviceStatusReport(ReadOnlySpan<int> parameters, int cursorRow, int cursorCol)
    {
        if (parameters.Length == 0) return;

        if (parameters[0] == 5)
            TrySend("\x1b[0n");
        else if (parameters[0] == 6)
            TrySend($"\x1b[{cursorRow + 1};{cursorCol + 1}R");
    }

    private bool TrySend(string ascii)
    {
        if (!AllowedByRateLimit())
            return false;

        var bytes = Encoding.ASCII.GetBytes(ascii);
        // Belt-and-braces at the point of write, even though every caller above only ever builds
        // fixed strings or decimal digits from int cursor coordinates: nothing leaving this class
        // may ever contain CR/LF (which would execute immediately on the shell's command line) or
        // any byte outside the printable-ASCII/ESC range a VT reply can legitimately contain.
        foreach (var b in bytes)
        {
            if (b is (byte)'\r' or (byte)'\n')
                return false;
            if (b != 0x1B && (b < 0x20 || b > 0x7E))
                return false;
        }

        _write(bytes);
        return true;
    }

    /// <summary>Classic token bucket: starts full (<see cref="VtLimits.ResponsesBurst"/> tokens,
    /// so a legitimate short burst - e.g. PSReadLine's startup CPR plus a couple of redraws -
    /// isn't throttled), refills at <see cref="VtLimits.ResponsesPerSecond"/> tokens/sec, capped
    /// at the burst size, plus a hard lifetime cap for the whole session. Without this, a batch
    /// file looping "echo ESC[6n" stuffs hundreds of KB of cursor-position reports into the
    /// shell's own line editor - a DoS at minimum.</summary>
    private bool AllowedByRateLimit()
    {
        if (_totalResponses >= VtLimits.ResponsesPerSession)
            return false;

        var now = Environment.TickCount64;
        var elapsedSeconds = (now - _lastRefillTicks) / 1000.0;
        _tokens = Math.Min(VtLimits.ResponsesBurst, _tokens + elapsedSeconds * VtLimits.ResponsesPerSecond);
        _lastRefillTicks = now;

        if (_tokens < 1)
            return false;

        _tokens -= 1;
        _totalResponses++;
        return true;
    }
}
