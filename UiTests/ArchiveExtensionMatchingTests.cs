using CoderCommander.Archives;

namespace CoderCommander.UiTests;

/// <summary>
/// Direct (no UI) tests for <see cref="ArchiveFormatRegistry.FromExtension"/>'s longest-match
/// policy now that more than one format is registered - in particular that a compound extension
/// like ".tar.gz" resolves to the compound format rather than a shorter suffix, and that matching
/// is case-insensitive.
/// </summary>
public class ArchiveExtensionMatchingTests
{
    [TestCase(@"C:\data\archive.tar.gz", "tar.gz")]
    [TestCase(@"C:\data\archive.TAR.GZ", "tar.gz")]
    [TestCase(@"C:\data\archive.tgz", "tar.gz")]
    [TestCase(@"C:\data\archive.TGZ", "tar.gz")]
    [TestCase(@"C:\data\archive.tar", "tar")]
    [TestCase(@"C:\data\archive.TAR", "tar")]
    [TestCase(@"C:\data\archive.zip", "zip")]
    [TestCase(@"C:\data\archive.jar", "zip")]
    [TestCase(@"C:\data\archive.7z", "7z")]
    [TestCase(@"C:\data\archive.7Z", "7z")]
    [TestCase(@"C:\data\archive.rar", "rar")]
    [TestCase(@"C:\data\archive.tar.bz2", "tar.bz2")]
    [TestCase(@"C:\data\archive.tbz2", "tar.bz2")]
    [TestCase(@"C:\data\archive.tbz", "tar.bz2")]
    [TestCase(@"C:\data\archive.tar.xz", "tar.xz")]
    [TestCase(@"C:\data\archive.txz", "tar.xz")]
    public void FromExtension_ResolvesToExpectedFormat(string path, string expectedFormatId)
    {
        var format = ArchiveFormatRegistry.FromExtension(path);
        Assert.That(format, Is.Not.Null);
        Assert.That(format!.Id, Is.EqualTo(expectedFormatId));
    }

    [Test]
    public void FromExtension_TarGz_DoesNotMatchAsPlainTar()
    {
        // ".tar.gz" doesn't end in ".tar", so this is really just confirming there's no
        // cross-registration bug making the two formats collide.
        var format = ArchiveFormatRegistry.FromExtension(@"C:\data\backup.tar.gz");
        Assert.That(format!.Id, Is.Not.EqualTo("tar"));
    }

    [Test]
    public void FromExtension_LongestMatchWins_WhenMultipleFormatsCouldApply()
    {
        // Simulates the general policy with the formats actually registered: ".tar.gz" (7 chars)
        // must beat any shorter suffix a future format might also claim (e.g. a bare ".gz").
        var tarGz = ArchiveFormatRegistry.FromExtension("data.tar.gz");
        Assert.That(tarGz!.Extensions, Has.Some.Matches<string>(ext => ext.Equals(".tar.gz", StringComparison.OrdinalIgnoreCase)));
    }
}
