using System.Reflection;
using System.Windows.Forms;
using CoderCommander.Services;
using CoderCommander.WinForms;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the stale-match-position bug fixed in <see cref="FindReplaceBar"/>:
/// FindController's match list is a snapshot taken by SetPattern, and nothing used to invalidate
/// it when the document changed for a reason other than the bar's own Replace/Replace All (most
/// realistically, the user editing the canvas directly while the find bar stays open with a
/// pattern already entered). Acting on stale offsets would delete/insert at whatever text now
/// happens to sit at the old coordinates. Simulates an external content change via LoadText
/// (fires TextBuffer.Changed the same way any canvas edit does) and verifies the match list
/// re-scans instead of keeping offsets that may no longer even be valid positions in the new text.
/// </summary>
public class FindReplaceBarStaleMatchTests
{
    private static T GetPrivateField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field {name} not found on {target.GetType()}");
        return (T)field.GetValue(target)!;
    }

    [Test]
    public void ExternalBufferChange_InvalidatesStaleMatchList()
    {
        using var editor = new CodeEditorControl();
        editor.LoadText("cat\ncat\n");

        var findBar = GetPrivateField<FindReplaceBar>(editor, "_findBar");
        var findBox = GetPrivateField<TextBox>(findBar, "_findBox");
        var find = GetPrivateField<FindController>(findBar, "_find");

        findBox.Text = "cat"; // fires TextChanged -> OnPatternChanged
        Assert.That(find.Matches.Count, Is.EqualTo(2), "Sanity check: both lines must match before the external change");

        // Simulates an edit that didn't go through the find bar (e.g. the user typing directly
        // into the canvas) - LoadText fires TextBuffer.Changed exactly like any canvas edit does.
        editor.LoadText("dog\ndog\ncat\n");

        Assert.That(find.Matches.Count, Is.EqualTo(1),
            "The match list must re-scan against the new text, not keep the stale count/positions from before the external change");
        Assert.That(find.Matches[0].Start.Line, Is.EqualTo(2), "The single remaining match must point at the line that actually contains \"cat\" now");
    }
}
