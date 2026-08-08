using System.Runtime.InteropServices;
using System.Text;
using CoderCommander.Terminal.Screen;

namespace CoderCommander.Terminal.Input;

/// <summary>
/// Pure, static, table-driven key -&gt; VT byte encoder. Every method is a deterministic function
/// of its inputs (no I/O, no mutable state) except <see cref="IsAltGrPressed"/>, a thin
/// <c>GetKeyState</c> wrapper the caller uses to decide routing rather than something this class
/// consults internally.
/// <para>
/// <b>AltGr note</b> (why this class is shaped the way it is): WinForms reports AltGr as
/// <c>Control|Alt</c> on KeyDown, indistinguishable from a genuine Ctrl+Alt chord unless the caller
/// also checks <c>GetKeyState(VK_RMENU)</c>. Get this wrong and every AltGr-composed character on a
/// German/Polish/Brazilian layout becomes a mis-fired control code instead of the character the
/// layout actually produced. The caller (<c>TerminalCanvas</c>) must call <see cref="IsAltGrPressed"/>
/// in its KeyDown handler and, if true, skip <see cref="TryEncodeSpecialKey"/> for that key entirely -
/// AltGr-composed characters must be left to arrive via KeyPress/WM_CHAR instead, where Windows has
/// already resolved the dead-key/IME/layout composition. Printable characters in general must only
/// ever be encoded from WM_CHAR (<see cref="EncodePrintableChar"/>), never from KeyDown - that is
/// what makes dead keys, AltGr, and IME composition work for free instead of needing to be
/// reimplemented here.
/// </para>
/// </summary>
internal static partial class VtKeyEncoder
{
    private const int VirtualKeyRightAlt = 0xA5;

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int nVirtKey);

    /// <summary>True while the physical Right-Alt (AltGr) key is held.</summary>
    public static bool IsAltGrPressed() => (GetKeyState(VirtualKeyRightAlt) & 0x8000) != 0;

    /// <summary>
    /// Encodes a non-printable key: arrows, Home/End/PgUp/PgDn/Insert/Delete, F1-F12, Enter/Tab/
    /// Backspace/Escape, Ctrl+letter control codes, and the numeric keypad under DECKPAM
    /// (<see cref="TerminalModes.ApplicationKeypad"/>). Returns null for anything this table
    /// doesn't claim - the caller decides whether to fall through to an app-level hotkey or drop
    /// the key.
    /// </summary>
    public static byte[]? TryEncodeSpecialKey(Keys keyCode, bool shift, bool control, bool alt, TerminalModes modes)
    {
        if (modes.ApplicationKeypad && TryEncodeApplicationKeypad(keyCode) is { } keypad)
            return keypad;

        if (TryEncodeArrowOrEditingKey(keyCode, shift, control, alt, modes, out var special))
            return special;

        switch (keyCode)
        {
            case Keys.Enter: return Prefixed(alt, (byte)'\r');
            case Keys.Tab: return shift ? Ascii("\x1b[Z") : Prefixed(alt, (byte)'\t');
            case Keys.Back: return Prefixed(alt, control ? (byte)0x08 : (byte)0x7F);
            case Keys.Escape: return Prefixed(alt, 0x1B);
        }

        if (control && !alt && TryEncodeControlLetter(keyCode, out var controlByte))
            return [controlByte];

        // Meta/Alt+letter (e.g. bash readline's Alt+F/Alt+B/Alt+D): encoded here, by physical key
        // rather than via WM_CHAR, specifically so the caller can swallow it in ProcessCmdKey before
        // Windows treats Alt+key as a menu-mnemonic accelerator (WM_SYSCHAR) - deferring to WM_CHAR
        // for this combo would mean fighting the menu system for it instead.
        if (alt && !control && keyCode is >= Keys.A and <= Keys.Z)
            return [0x1B, (byte)('a' + (keyCode - Keys.A))];

        return TryEncodeFunctionKey(keyCode, shift, control, alt);
    }

    /// <summary>
    /// Encodes a printable character delivered via WM_CHAR/OnKeyPress. <paramref name="altPressed"/>
    /// must be the REAL left-Alt state (never true for AltGr - see the class doc comment); when
    /// true, the UTF-8 bytes are prefixed with ESC (the classic "meta sends escape" convention).
    /// </summary>
    public static byte[] EncodePrintableChar(char ch, bool altPressed)
    {
        var utf8 = Encoding.UTF8.GetBytes(ch.ToString());
        if (!altPressed)
            return utf8;

        var result = new byte[utf8.Length + 1];
        result[0] = 0x1B;
        utf8.CopyTo(result, 1);
        return result;
    }

    private static byte[] Prefixed(bool alt, byte b) => alt ? [0x1B, b] : [b];

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    /// <summary>Xterm's extended-modifier code: 1 + shift(1) + alt(2) + ctrl(4). A value of 1 means
    /// "no modifiers", in which case the plain (unmodified) form of the sequence is used instead.</summary>
    private static int ModifierCode(bool shift, bool control, bool alt) =>
        1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (control ? 4 : 0);

    private static bool TryEncodeArrowOrEditingKey(Keys keyCode, bool shift, bool control, bool alt,
        TerminalModes modes, out byte[]? bytes)
    {
        bytes = null;
        var modifierCode = ModifierCode(shift, control, alt);
        var hasModifier = modifierCode != 1;

        char? arrowFinal = keyCode switch
        {
            Keys.Up => 'A',
            Keys.Down => 'B',
            Keys.Right => 'C',
            Keys.Left => 'D',
            _ => null
        };
        if (arrowFinal is { } af)
        {
            bytes = hasModifier
                ? Ascii($"\x1b[1;{modifierCode}{af}")
                : Ascii(modes.ApplicationCursorKeys ? $"\x1bO{af}" : $"\x1b[{af}");
            return true;
        }

        if (keyCode is Keys.Home or Keys.End)
        {
            var final = keyCode == Keys.Home ? 'H' : 'F';
            bytes = hasModifier
                ? Ascii($"\x1b[1;{modifierCode}{final}")
                : Ascii(modes.ApplicationCursorKeys ? $"\x1bO{final}" : $"\x1b[{final}");
            return true;
        }

        int? tildeCode = keyCode switch
        {
            Keys.Insert => 2,
            Keys.Delete => 3,
            Keys.Prior => 5, // Page Up
            Keys.Next => 6,  // Page Down
            _ => null
        };
        if (tildeCode is { } tc)
        {
            bytes = hasModifier ? Ascii($"\x1b[{tc};{modifierCode}~") : Ascii($"\x1b[{tc}~");
            return true;
        }

        return false;
    }

    private static byte[]? TryEncodeFunctionKey(Keys keyCode, bool shift, bool control, bool alt)
    {
        var modifierCode = ModifierCode(shift, control, alt);
        var hasModifier = modifierCode != 1;

        char? ss3Final = keyCode switch
        {
            Keys.F1 => 'P',
            Keys.F2 => 'Q',
            Keys.F3 => 'R',
            Keys.F4 => 'S',
            _ => null
        };
        if (ss3Final is { } sf)
            return Ascii(hasModifier ? $"\x1b[1;{modifierCode}{sf}" : $"\x1bO{sf}");

        // Historical xterm gap: 16 and 22 are intentionally unused.
        int? tildeCode = keyCode switch
        {
            Keys.F5 => 15,
            Keys.F6 => 17,
            Keys.F7 => 18,
            Keys.F8 => 19,
            Keys.F9 => 20,
            Keys.F10 => 21,
            Keys.F11 => 23,
            Keys.F12 => 24,
            _ => null
        };
        if (tildeCode is { } tc)
            return Ascii(hasModifier ? $"\x1b[{tc};{modifierCode}~" : $"\x1b[{tc}~");

        return null;
    }

    /// <summary>Numeric keypad under DECKPAM (application keypad mode). Only digits and the decimal
    /// point are remapped - operator keys (+-*/) and Enter are left to their normal encoding, which
    /// is what every app that actually cares about DECKPAM (numeric-entry TUIs) depends on.</summary>
    private static byte[]? TryEncodeApplicationKeypad(Keys keyCode)
    {
        char? final = keyCode switch
        {
            Keys.NumPad0 => 'p',
            Keys.NumPad1 => 'q',
            Keys.NumPad2 => 'r',
            Keys.NumPad3 => 's',
            Keys.NumPad4 => 't',
            Keys.NumPad5 => 'u',
            Keys.NumPad6 => 'v',
            Keys.NumPad7 => 'w',
            Keys.NumPad8 => 'x',
            Keys.NumPad9 => 'y',
            Keys.Decimal => 'n',
            _ => null
        };
        return final is { } f ? Ascii($"\x1bO{f}") : null;
    }

    /// <summary>Ctrl+letter/symbol control codes. Mapped by physical key position (US-layout
    /// convention every terminal emulator uses - xterm and Windows Terminal both key off VK code,
    /// not the shifted glyph a non-US layout would actually produce).</summary>
    private static bool TryEncodeControlLetter(Keys keyCode, out byte controlByte)
    {
        controlByte = 0;
        switch (keyCode)
        {
            case >= Keys.A and <= Keys.Z:
                controlByte = (byte)(keyCode - Keys.A + 1);
                return true;
            case Keys.D2 or Keys.Space:
                controlByte = 0x00; // Ctrl+Space / Ctrl+2 (Ctrl+@)
                return true;
            case Keys.D6:
                controlByte = 0x1E; // Ctrl+^
                return true;
            case Keys.OemMinus:
                controlByte = 0x1F; // Ctrl+_
                return true;
            case Keys.Oem4:
                controlByte = 0x1B; // Ctrl+[ (same as Escape)
                return true;
            case Keys.Oem5:
                controlByte = 0x1C; // Ctrl+\
                return true;
            case Keys.Oem6:
                controlByte = 0x1D; // Ctrl+]
                return true;
            default:
                return false;
        }
    }
}
