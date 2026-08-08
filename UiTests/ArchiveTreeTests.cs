using CoderCommander.Archives;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression tests for two edge cases fixed in <see cref="ArchiveTree"/>:
/// <list type="bullet">
/// <item>HasDescendants always returned false at the archive root, since the general-case prefix
/// ("" + "/" = "/") never matches TrimmedName's output (which never starts with "/").</item>
/// <item>TrimmedName only stripped one leading "./" segment, so a doubly-prefixed entry name
/// ("././file.txt", which some tar producers emit) still started with "./" afterward.</item>
/// </list>
/// </summary>
public class ArchiveTreeTests
{
    private static ArchiveEntryRecord Entry(string name, bool isDir = false) =>
        new() { FullName = name, IsDirectory = isDir };

    [Test]
    public void HasDescendants_AtRoot_NonEmptyArchive_ReturnsTrue()
    {
        var entries = new[] { Entry("readme.txt") };
        Assert.That(ArchiveTree.HasDescendants(entries, ""), Is.True);
    }

    [Test]
    public void HasDescendants_AtRoot_EmptyArchive_ReturnsFalse()
    {
        Assert.That(ArchiveTree.HasDescendants(Array.Empty<ArchiveEntryRecord>(), ""), Is.False);
    }

    [Test]
    public void HasDescendants_NonRootPath_StillWorks()
    {
        var entries = new[] { Entry("sub/readme.txt") };
        Assert.That(ArchiveTree.HasDescendants(entries, "sub"), Is.True);
        Assert.That(ArchiveTree.HasDescendants(entries, "other"), Is.False);
    }

    [Test]
    public void FindEntry_DoublyPrefixedDotSlashEntry_MatchesTrimmedName()
    {
        var entries = new[] { Entry("././file.txt") };
        var found = ArchiveTree.FindEntry(entries, "file.txt");
        Assert.That(found, Is.Not.Null, "An entry doubly-prefixed with './' must still resolve to its trimmed name");
    }
}
