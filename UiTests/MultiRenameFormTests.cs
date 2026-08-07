using System.Reflection;
using CoderCommander.WinForms;

namespace CoderCommander.UiTests;

/// <summary>
/// Direct (no UI) tests for the crash fixed in <see cref="MultiRenameForm"/>'s placeholder
/// substitution: the digit-run capture in the placeholder regex (e.g. <c>[C12]</c>/<c>[N5]</c>)
/// has no length cap, so typing enough digits directly into the pattern textbox overflows
/// <see cref="int.MaxValue"/>. Before the fix this reached a bare <c>int.Parse</c> with no
/// try/catch anywhere on the path from <c>TextChanged</c> - an ordinary typo threw
/// <see cref="OverflowException"/> synchronously inside the WinForms message loop, and
/// Program.cs doesn't hook Application.ThreadException, so it crashed the app.
/// <c>ReplacePlaceholders</c> is a private static method - invoked via reflection since the
/// bug (and its fix) live entirely in placeholder-substitution logic that doesn't need a real
/// dialog instance to exercise.
/// </summary>
public class MultiRenameFormTests
{
    private static readonly MethodInfo ReplacePlaceholders = typeof(MultiRenameForm)
        .GetMethod("ReplacePlaceholders", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Test]
    public void ReplacePlaceholders_OversizedCounterDigits_DoesNotThrowOverflow()
    {
        Assert.DoesNotThrow(() =>
            ReplacePlaceholders.Invoke(null, new object?[] { "[C99999999999]", "name", "ext", null, 0, 1, 1 }));
    }

    [Test]
    public void ReplacePlaceholders_OversizedSubstringCountDigits_DoesNotThrowOverflow()
    {
        Assert.DoesNotThrow(() =>
            ReplacePlaceholders.Invoke(null, new object?[] { "[N99999999999]", "name", "ext", null, 0, 1, 1 }));
    }

    [Test]
    public void ReplacePlaceholders_NormalCounterPattern_StillWorksCorrectly()
    {
        var result = (string)ReplacePlaceholders.Invoke(null, new object?[] { "[C3:10]", "name", "ext", null, 2, 1, 1 })!;
        Assert.That(result, Is.EqualTo("012")); // width=3, start=10, index=2, step=1 -> 10 + 2*1 = 12, padded to 3 digits
    }
}
