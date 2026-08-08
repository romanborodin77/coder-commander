using CoderCommander.Services;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the bug fixed in <see cref="FindController.ReplaceAll"/>: the caret after
/// a Replace All was always placed at the position of the FIRST match in the whole document,
/// regardless of where the replacements actually happened. Starting a Replace All deep inside a
/// long file (e.g. around line 900) snapped the caret - and the view along with it - all the way
/// back up to line 1.
/// </summary>
public class EditorFindReplaceAllCaretTests
{
    [Test]
    public void ReplaceAll_MultipleMatches_LeavesCaretAtLastMatchNotFirst()
    {
        var buffer = new TextBuffer();
        buffer.LoadText("cat\ncat\ncat\n");
        var undo = new UndoStack();
        var find = new FindController();
        find.SetPattern(buffer, "cat", new TextPosition(0, 0));

        var replaced = find.ReplaceAll(buffer, undo, "dog", new TextPosition(0, 0), out var caretAfter);

        Assert.That(replaced, Is.EqualTo(3));
        Assert.That(caretAfter.Line, Is.EqualTo(2),
            "Caret must end at the last match (line 2), not snap back to the first match (line 0)");
    }
}
