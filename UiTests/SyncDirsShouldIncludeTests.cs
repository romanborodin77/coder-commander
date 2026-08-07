using System.Reflection;
using CoderCommander.WinForms;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the preview/execute mismatch fixed in <see cref="SyncDirsForm"/>'s copy
/// queueing: the diff list's checkboxes are enabled on every row with no restriction, but the old
/// include-filter silently excluded SyncStatus.Equal rows from the actual copy queue - a user who
/// deliberately checked an "=" row (e.g. to force a re-copy) saw no error, just a checked row that
/// quietly never got copied. ShouldInclude is private static - invoked via reflection.
/// </summary>
public class SyncDirsShouldIncludeTests
{
    private static readonly MethodInfo ShouldInclude = typeof(SyncDirsForm)
        .GetMethod("ShouldInclude", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static bool Invoke(SyncStatus status, SyncDirection dir) =>
        (bool)ShouldInclude.Invoke(null, new object[] { status, dir })!;

    [Test]
    public void Equal_IsIncludedInBothDirections()
    {
        Assert.That(Invoke(SyncStatus.Equal, SyncDirection.LeftToRight), Is.True);
        Assert.That(Invoke(SyncStatus.Equal, SyncDirection.RightToLeft), Is.True);
    }

    [Test]
    public void RightOnly_IsExcludedFromLeftToRightCopy()
    {
        // Nothing to copy from: there's no left-side source for a right-only entry.
        Assert.That(Invoke(SyncStatus.RightOnly, SyncDirection.LeftToRight), Is.False);
        Assert.That(Invoke(SyncStatus.RightOnly, SyncDirection.RightToLeft), Is.True);
    }

    [Test]
    public void LeftOnly_IsExcludedFromRightToLeftCopy()
    {
        Assert.That(Invoke(SyncStatus.LeftOnly, SyncDirection.RightToLeft), Is.False);
        Assert.That(Invoke(SyncStatus.LeftOnly, SyncDirection.LeftToRight), Is.True);
    }

    [Test]
    public void DiffStatuses_AreIncludedInBothDirections()
    {
        foreach (var status in new[] { SyncStatus.SizeDiffers, SyncStatus.TimeDiffers, SyncStatus.TypeDiffers })
        {
            Assert.That(Invoke(status, SyncDirection.LeftToRight), Is.True, status.ToString());
            Assert.That(Invoke(status, SyncDirection.RightToLeft), Is.True, status.ToString());
        }
    }
}
