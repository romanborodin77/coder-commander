using CoderCommander.FileSystem;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the bug fixed in <see cref="VfsPath.GetRelative"/>: for two plain
/// (non-archive) paths on different drives, Path.GetRelativePath doesn't return a "../"-prefixed
/// result the way it does for paths that merely diverge partway down a shared root - it returns
/// the second path completely unchanged (still rooted). GetRelative's fallback to the bare name
/// only checked for the "../" form, so a cross-volume fullPath leaked through as a full rooted
/// path instead of falling back to the bare name like the method's own doc comment promises for
/// "unrelated trees".
/// </summary>
public class VfsPathGetRelativeCrossVolumeTests
{
    [Test]
    public void GetRelative_DifferentDrives_ReturnsBareName()
    {
        var result = VfsPath.GetRelative(@"C:\Work", @"D:\Backup\notes.txt");
        Assert.That(result, Is.EqualTo("notes.txt"));
    }

    [Test]
    public void GetRelative_SameDriveDivergingPaths_StillReturnsBareName()
    {
        var result = VfsPath.GetRelative(@"C:\Work\ProjectA", @"C:\Other\notes.txt");
        Assert.That(result, Is.EqualTo("notes.txt"));
    }

    [Test]
    public void GetRelative_SameTree_StillReturnsRelativePath()
    {
        var result = VfsPath.GetRelative(@"C:\Work", @"C:\Work\sub\notes.txt");
        Assert.That(result, Is.EqualTo(Path.Combine("sub", "notes.txt")));
    }
}
