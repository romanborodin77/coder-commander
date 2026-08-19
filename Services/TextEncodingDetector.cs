using System.Globalization;
using System.Text;

namespace CoderCommander.Services;

/// <summary>Detects a text file's encoding from its byte-order-mark. Shared by the viewer and the editor
/// so both agree on what "the file's encoding" means, and so a round-trip load/save doesn't add or
/// duplicate a BOM the original file didn't have.</summary>
public static class TextEncodingDetector
{
    /// <summary>Returns the detected encoding; <paramref name="preambleLength"/> is how many leading bytes
    /// are the BOM (0 if none was found) and must be skipped before decoding the rest as text.</summary>
    public static Encoding Detect(byte[] data, out int preambleLength)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            preambleLength = 3;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }
        // UTF-32 LE BOM (FF FE 00 00) must be checked before UTF-16 LE (FF FE) — the first two
        // bytes are identical, so without this check a UTF-32 file is misdetected as UTF-16.
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xFE && data[2] == 0 && data[3] == 0)
        {
            preambleLength = 4;
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true);
        }
        if (data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 0xFE && data[3] == 0xFF)
        {
            preambleLength = 4;
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        }
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            preambleLength = 2;
            return Encoding.Unicode;
        }
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            preambleLength = 2;
            return Encoding.BigEndianUnicode;
        }
        preambleLength = 0;

        // No BOM - validate as UTF-8 before committing to it. A legacy-encoded file (a Windows
        // ANSI code page, Latin-1, Shift-JIS, ...) is not valid UTF-8 byte-for-byte in general;
        // decoding it as UTF-8 without validation used to silently replace every invalid
        // sequence with U+FFFD - and since that's exactly what round-tripped back out on the
        // next save (this class's own summary promises a round-trip that doesn't corrupt what
        // wasn't ASCII), the original bytes were unrecoverable the moment the user saved.
        if (IsValidUtf8(data))
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // Falls back to the system's ANSI code page - the same default Windows/Notepad itself
        // uses for a BOM-less, non-UTF-8 text file - so at least a legacy file in the user's own
        // locale's encoding (e.g. Windows-1251 for Cyrillic) round-trips correctly instead of
        // losing every non-ASCII character.
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
        {
            // CodePagesEncodingProvider isn't registered, or the code page id is unavailable for
            // some other reason - fall back to the old behavior (UTF-8 with replacement
            // characters) as a last resort rather than throwing out of a detection routine.
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
    }

    private static bool IsValidUtf8(byte[] data)
    {
        try
        {
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
