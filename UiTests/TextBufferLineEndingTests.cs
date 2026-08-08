using CoderCommander.Services;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the line-ending-detection bug fixed in <see cref="TextBuffer"/>:
/// DetectLineEnding returned "\r\n" the instant a single CRLF appeared anywhere in the text,
/// rather than whichever style was actually dominant. A predominantly LF-terminated file (e.g. a
/// shell script) carrying a single stray CRLF line (pasted from a Windows machine once) had every
/// other line ending silently rewritten to CRLF on save, since GetText() joins all lines with one
/// file-wide LineEnding - a full-file rewrite for what should have been a one-character edit.
/// </summary>
public class TextBufferLineEndingTests
{
    [Test]
    public void LoadText_PredominantlyLfWithOneStrayCrlf_DetectsLfAsDominant()
    {
        var buffer = new TextBuffer();
        // 5 LF-only breaks, 1 CRLF break - LF is clearly dominant.
        buffer.LoadText("line1\nline2\nline3\r\nline4\nline5\nline6");

        Assert.That(buffer.LineEnding, Is.EqualTo("\n"),
            "LF must be detected as dominant despite one stray CRLF line, not CRLF just because it appears at all");
    }

    [Test]
    public void LoadText_PredominantlyCrlfWithOneStrayLf_DetectsCrlfAsDominant()
    {
        var buffer = new TextBuffer();
        buffer.LoadText("line1\r\nline2\r\nline3\nline4\r\nline5\r\n");

        Assert.That(buffer.LineEnding, Is.EqualTo("\r\n"));
    }

    [Test]
    public void LoadText_AllLf_DetectsLf()
    {
        var buffer = new TextBuffer();
        buffer.LoadText("a\nb\nc\n");
        Assert.That(buffer.LineEnding, Is.EqualTo("\n"));
    }

    [Test]
    public void LoadText_NoLineBreaksAtAll_DefaultsToCrlf()
    {
        var buffer = new TextBuffer();
        buffer.LoadText("single line, no breaks");
        Assert.That(buffer.LineEnding, Is.EqualTo("\r\n"));
    }
}
