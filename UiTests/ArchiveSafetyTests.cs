using CoderCommander.Archives;

namespace CoderCommander.UiTests;

/// <summary>Direct (no UI) tests for the shared zip-slip / path-traversal guards.</summary>
public class ArchiveSafetyTests
{
    [TestCase("a/b/c.txt", false)]
    [TestCase("normal.txt", false)]
    [TestCase("../escape.txt", true)]
    [TestCase("a/../../escape.txt", true)]
    [TestCase("a/b/../c.txt", true)]
    public void EscapesTarget_DetectsDotDotSegments(string relative, bool expectedEscapes)
    {
        Assert.That(ArchiveSafety.EscapesTarget(relative), Is.EqualTo(expectedEscapes));
    }

    [Test]
    public void EscapesTarget_RootedPath_Escapes()
    {
        Assert.That(ArchiveSafety.EscapesTarget(@"C:\Windows\System32\evil.dll"), Is.True);
    }

    [Test]
    public void EscapesRoot_EntryWithinRoot_DoesNotEscape()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cc_safety_test_{Guid.NewGuid():N}");
        Assert.That(ArchiveSafety.EscapesRoot(root, "sub/file.txt"), Is.False);
    }

    [Test]
    public void EscapesRoot_DotDotEntry_Escapes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cc_safety_test_{Guid.NewGuid():N}");
        Assert.That(ArchiveSafety.EscapesRoot(root, @"..\..\evil.txt"), Is.True);
    }

    [Test]
    public void EscapesRoot_AbsolutePathEntry_Escapes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cc_safety_test_{Guid.NewGuid():N}");
        Assert.That(ArchiveSafety.EscapesRoot(root, @"C:\Windows\System32\evil.dll"), Is.True);
    }

    // TAR entries can carry symlinks/hardlinks whose LinkName is exactly this kind of string -
    // CoderCommander doesn't materialize real symlinks on extraction (Phase 2 scope), but any
    // future code that does must run the target through these same checks first.

    [TestCase("../../../etc/passwd", true)]
    [TestCase("/etc/passwd", true)]
    [TestCase(@"\\server\share\evil.dll", true)]
    [TestCase("sibling-file.txt", false)]
    [TestCase("subdir/sibling-file.txt", false)]
    public void EscapesTarget_SymlinkStyleTarget_DetectsEscape(string linkTarget, bool expectedEscapes)
    {
        Assert.That(ArchiveSafety.EscapesTarget(linkTarget), Is.EqualTo(expectedEscapes));
    }

    [Test]
    public void EscapesRoot_SymlinkTargetClimbingOutOfRoot_Escapes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cc_safety_test_{Guid.NewGuid():N}");
        Assert.That(ArchiveSafety.EscapesRoot(root, Path.Combine("..", "..", "outside.txt")), Is.True);
    }
}
