using CoderCommander.FileSystem;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the drive-relative-name escape fixed in <see cref="VfsPath.ChangeName"/>.
/// The guard rejected a name containing '/' or '\', or exactly "." / "..", but not a
/// drive-relative name like "C:evil.exe" (no backslash after the colon). Path.IsPathRooted
/// treats that string as rooted, so Path.Combine's plain-path branch in VfsPath.Combine returns
/// it verbatim, discarding the parent directory entirely - the single choke point the doc comment
/// on ChangeName claims every overwrite-conflict rename flow relies on to stop an
/// OverwriteResolveHandler-supplied name from escaping the target directory.
/// </summary>
public class VfsPathChangeNameEscapeTests
{
    [Test]
    public void ChangeName_DriveRelativeName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            VfsPath.ChangeName(@"C:\Users\bob\Downloads\setup.exe", "C:evil.exe"));
    }

    [Test]
    public void ChangeName_OrdinaryName_StillWorks()
    {
        var result = VfsPath.ChangeName(@"C:\Users\bob\Downloads\setup.exe", "setup (1).exe");
        Assert.That(result, Is.EqualTo(@"C:\Users\bob\Downloads\setup (1).exe"));
    }
}
