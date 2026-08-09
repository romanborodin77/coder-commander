using System.Text;

namespace CoderCommander.Terminal.Input;

/// <summary>
/// Encodes mouse events in the SGR mouse protocol (<c>CSI &lt; Cb ; Cx ; Cy M/m</c>, mode 1006) -
/// the only mouse encoding this app forwards. Legacy X10/UTF8 encodings are deliberately not
/// supported: SGR is what every modern full-screen TUI (vim, mc, htop) actually negotiates, and
/// skipping the legacy forms avoids their well-known coordinate-overflow limits.
/// </summary>
internal static class MouseEncoder
{
    public const int ButtonLeft = 0;
    public const int ButtonMiddle = 1;
    public const int ButtonRight = 2;
    /// <summary>"No button" - used for a release, and for a hover-motion report with nothing held.</summary>
    public const int ButtonNone = 3;

    private const int FlagShift = 4;
    private const int FlagAlt = 8;
    private const int FlagControl = 16;
    private const int FlagMotion = 32;

    /// <param name="button">One of the <c>Button*</c> constants.</param>
    /// <param name="col">0-based column.</param>
    /// <param name="row">0-based row.</param>
    /// <param name="press">True for a press/motion report (final byte 'M'), false for a release ('m').</param>
    public static byte[] EncodeButton(int button, int col, int row, bool press, bool shift, bool alt, bool control, bool motion = false)
    {
        var cb = button;
        if (shift) cb |= FlagShift;
        if (alt) cb |= FlagAlt;
        if (control) cb |= FlagControl;
        if (motion) cb |= FlagMotion;
        return Encode(cb, col, row, press);
    }

    /// <summary>Wheel notch. Always reported as a "press" (xterm convention - there is no wheel
    /// release event).</summary>
    public static byte[] EncodeWheel(bool up, int col, int row, bool shift, bool alt, bool control)
    {
        var cb = (up ? 64 : 65);
        if (shift) cb |= FlagShift;
        if (alt) cb |= FlagAlt;
        if (control) cb |= FlagControl;
        return Encode(cb, col, row, press: true);
    }

    private static byte[] Encode(int cb, int col, int row, bool press) =>
        Encoding.ASCII.GetBytes($"\x1b[<{cb};{col + 1};{row + 1}{(press ? 'M' : 'm')}");

    /// <summary>Focus in/out reporting (mode 1004) - <c>CSI I</c> / <c>CSI O</c>.</summary>
    public static byte[] EncodeFocus(bool gained) => Encoding.ASCII.GetBytes(gained ? "\x1b[I" : "\x1b[O");
}
