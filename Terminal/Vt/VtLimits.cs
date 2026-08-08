namespace CoderCommander.Terminal.Vt;

/// <summary>
/// Hard caps for the VT parser and screen model. Every byte reaching the parser is potentially
/// attacker-controlled (a `type evil.txt`, a crafted filename in `dir`, a git branch name) -
/// these limits exist so that a malicious or corrupt stream can't hang the app, exhaust memory,
/// or overflow parser state, and each one has a dedicated test in <c>VtLimitsTests</c>.
/// </summary>
internal static class VtLimits
{
    /// <summary>Extra CSI parameters beyond this are parsed (to keep the state machine
    /// well-defined) and silently dropped rather than dispatched.</summary>
    public const int MaxParams = 32;

    /// <summary>Clamp applied DURING digit accumulation, not after - CSI 99999999999999b would
    /// overflow a plain int if clamped only at the end.</summary>
    public const int MaxParamValue = 65535;

    /// <summary>OSC string payload length (chars) before the sequence is discarded-until-ST
    /// without ever dispatching.</summary>
    public const int MaxOscLength = 4096;

    /// <summary>DCS string payload length (chars), same discard-without-dispatch behavior.</summary>
    public const int MaxDcsLength = 4096;

    /// <summary>SOS/PM/APC string payload length (chars) - these are consumed to the string
    /// terminator and always discarded; only the length needs a cap.</summary>
    public const int MaxApcPmSosLength = 4096;

    /// <summary>Combining marks attached to a single cell - protects against a "zalgo" glyph
    /// stack turning one cell into unbounded work.</summary>
    public const int MaxCombiningPerCell = 8;

    public const int MaxHyperlinksPerTab = 4096;

    public const int MaxScrollbackLines = 100_000;

    public const long MaxScrollbackBytes = 64L * 1024 * 1024;

    /// <summary>OSC 0/1/2 window/tab title, after sanitization.</summary>
    public const int MaxTitleLength = 256;

    public const int MaxPasteBytes = 1024 * 1024;

    public const int PasteConfirmBytes = 16 * 1024;

    /// <summary>Token-bucket rate limit for <see cref="VtResponder"/>'s whitelisted replies
    /// (DA1/DA2/DSR5/CPR6) - without this, a batch file looping "ESC[6n" floods the shell's own
    /// line editor with cursor-position reports.</summary>
    public const int ResponsesPerSecond = 8;

    public const int ResponsesBurst = 16;

    public const int ResponsesPerSession = 512;
}
