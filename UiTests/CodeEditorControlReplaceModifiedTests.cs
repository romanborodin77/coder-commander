using System.Reflection;
using System.Windows.Forms;
using CoderCommander.WinForms;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the silent-data-loss bug fixed in <see cref="CodeEditorControl"/>: Replace/
/// Replace All in <see cref="FindReplaceBar"/> mutate the shared TextBuffer/UndoStack directly,
/// bypassing CodeEditorCanvas's own edit methods (and therefore its ContentChanged event, the
/// only thing CodeEditorControl used to listen to for recomputing Modified). A replacement left
/// the tab looking unmodified, so closing it with no "save changes?" prompt silently discarded
/// the edit. Uses ReplaceOne (not ReplaceAll, which shows a blocking confirmation StyledMessageBox
/// that can't be driven headlessly) to exercise the exact same ContentChanged wiring the fix adds.
/// </summary>
public class CodeEditorControlReplaceModifiedTests
{
    private static T GetPrivateField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field {name} not found on {target.GetType()}");
        return (T)field.GetValue(target)!;
    }

    [Test]
    public void ReplaceOne_ActuallyReplacingText_MarksControlAsModified()
    {
        using var editor = new CodeEditorControl();
        editor.LoadText("cat and cat");
        Assert.That(editor.Modified, Is.False, "Freshly loaded text must not start out modified");

        var findBar = GetPrivateField<FindReplaceBar>(editor, "_findBar");
        var findBox = GetPrivateField<TextBox>(findBar, "_findBox");
        var replaceBox = GetPrivateField<TextBox>(findBar, "_replaceBox");

        findBox.Text = "cat"; // fires TextChanged -> OnPatternChanged -> populates the match list
        replaceBox.Text = "dog";

        var replaceOne = findBar.GetType().GetMethod("ReplaceOne", BindingFlags.NonPublic | BindingFlags.Instance)!;
        replaceOne.Invoke(findBar, null);

        Assert.That(editor.Text, Does.StartWith("dog"), "Sanity check: the replacement must have actually happened");
        Assert.That(editor.Modified, Is.True,
            "Replacing text through the find bar must mark the control as modified, or the tab can be closed with the edit silently discarded");
    }
}
