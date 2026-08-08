using System.Globalization;
using System.Text;
using CoderCommander.Services;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the data-loss bug fixed in <see cref="TextEncodingDetector"/>: a BOM-less
/// file used to be unconditionally decoded as UTF-8 with no validation, so any legacy-encoded file
/// (a Windows ANSI code page, Latin-1, ...) had every non-ASCII byte silently replaced with U+FFFD
/// on load - and since that's exactly what got written back out on the next save, the original
/// bytes were unrecoverable. Uses the system's own ANSI code page (dynamically, not a hardcoded
/// one) to build the test bytes, matching how a real legacy file on this machine would actually be
/// encoded and how the fix's own fallback picks a code page.
/// </summary>
public class TextEncodingDetectorFallbackTests
{
    [Test]
    public void Detect_NoBomNonUtf8Bytes_RoundTripsThroughAnsiCodePageInsteadOfReplacementCharacters()
    {
        var ansiEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);

        // Every single-byte Windows code page maps 0xA0-0xFF to some accented/extended
        // character, so this byte range is virtually guaranteed to produce bytes that are
        // simultaneously (a) not valid UTF-8 on their own and (b) decodable by the system's ANSI
        // code page - without hardcoding which specific code page this machine uses.
        var bytes = new byte[] { (byte)'A', 0xE9, 0xE0, 0xE8, (byte)'Z' };

        Assert.That(IsValidUtf8(bytes), Is.False, "Test setup: these bytes must not be valid UTF-8, or the test doesn't exercise the fallback path");

        var detected = TextEncodingDetector.Detect(bytes, out var preambleLength);
        Assert.That(preambleLength, Is.EqualTo(0));

        var decoded = detected.GetString(bytes);
        var expected = ansiEncoding.GetString(bytes);

        Assert.That(decoded, Is.EqualTo(expected),
            "Must decode through the system's own ANSI code page instead of falling back to UTF-8");
        Assert.That(decoded, Does.Not.Contain('�'),
            "Must not contain the replacement character - that's the data-loss signature of blindly decoding as UTF-8");
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
