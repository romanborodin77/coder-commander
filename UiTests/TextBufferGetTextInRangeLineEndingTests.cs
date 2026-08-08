using CoderCommander.Services;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the bug fixed in <see cref="TextBuffer.GetTextInRange"/>: multi-line
/// selections were always joined with a bare '\n', regardless of the document's actual
/// LineEnding. Copy/Cut in a CRLF document (CodeEditorCanvas's clipboard calls both go through
/// GetTextInRange) put text on the clipboard as one line glued together with invisible '\n's -
/// pasting into another application (Notepad, Excel, a terminal) showed a single run-on line
/// instead of the several the user selected. Pasting back into this same editor worked, which is
/// why the bug was easy to miss: InsertText's SplitLines already re-splits on any of \r\n/\r/\n.
/// </summary>
public class TextBufferGetTextInRangeLineEndingTests
{
    [Test]
    public void GetTextInRange_CrlfDocument_JoinsSelectedLinesWithCrlf()
    {
        var buffer = new TextBuffer();
        buffer.LoadText("line1\r\nline2\r\nline3\r\n");
        Assert.That(buffer.LineEnding, Is.EqualTo("\r\n"));

        var text = buffer.GetTextInRange(new TextPosition(0, 0), new TextPosition(2, 5));

        Assert.That(text, Is.EqualTo("line1\r\nline2\r\nline3"),
            "A multi-line selection copied out of a CRLF document must itself be CRLF-joined, not glued with bare '\\n's");
    }

    [Test]
    public void GetTextInRange_LfDocument_JoinsSelectedLinesWithLf()
    {
        var buffer = new TextBuffer();
        buffer.LoadText("line1\nline2\nline3\n");
        Assert.That(buffer.LineEnding, Is.EqualTo("\n"));

        var text = buffer.GetTextInRange(new TextPosition(0, 0), new TextPosition(2, 5));

        Assert.That(text, Is.EqualTo("line1\nline2\nline3"));
    }
}
