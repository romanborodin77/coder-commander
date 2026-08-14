namespace CoderCommander.Viewers;

/// <summary>
/// Byte-to-string conversions shared by the ASCII and Binary viewer formats. Both render every
/// byte as one character without going through <c>TextEncodingDetector</c> (that's what "Text"
/// mode is for) - they exist specifically for files where autodetection guessed wrong or doesn't
/// apply. Neither ever hands a raw byte straight to <c>char</c> without sanitizing it first: a
/// <see cref="System.Windows.Forms.RichTextBox"/> sits on a Win32 <c>EDIT</c>/<c>RICHEDIT</c>
/// control, and an embedded NUL byte in the assigned <c>.Text</c> truncates the control's string
/// at that point - a real, silent-corruption risk if a caller ever did this naively.
/// </summary>
internal static class RawByteText
{
    /// <summary>ASCII mode: only the printable ASCII range (plus tab/LF/CR) passes through as
    /// itself; everything else - including every byte above 0x7F - becomes '.'. Strict and lossy
    /// by design: it's for spotting embedded ASCII strings in a file whose real encoding is
    /// unknown or binary, not for a faithful byte-for-byte view (that's Binary mode, below).</summary>
    public static string ToAsciiPrintable(byte[] raw)
    {
        var chars = new char[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            var b = raw[i];
            chars[i] = IsAsciiPrintableOrNewline(b) ? (char)b : '.';
        }
        return new string(chars);
    }

    /// <summary>Binary mode: every byte becomes its Latin-1 codepoint 1:1 (0x00-0xFF map straight
    /// to U+0000-U+00FF), preserving high-byte characters that ASCII mode above would collapse to
    /// '.' - except the small set of control bytes that would corrupt the RichTextBox itself
    /// (NUL and other C0 controls other than tab/LF/CR, plus DEL), which still become '.'.</summary>
    public static string ToLatin1Safe(byte[] raw)
    {
        var chars = new char[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            var b = raw[i];
            var isUnsafeControl = b < 0x20 && !IsAsciiPrintableOrNewline(b);
            chars[i] = isUnsafeControl || b == 0x7F ? '.' : (char)b;
        }
        return new string(chars);
    }

    private static bool IsAsciiPrintableOrNewline(byte b) =>
        b is (byte)'\t' or (byte)'\n' or (byte)'\r' || (b >= 0x20 && b < 0x7F);
}
