using System.IO.Compression;
using CoderCommander.Archives;

namespace CoderCommander.UiTests;

/// <summary>
/// Direct (no UI) tests for <see cref="ArchiveFormatRegistry"/>. ZIP is registered once,
/// process-wide, by <see cref="AssemblySetup"/> (mirroring <c>Program.cs</c>'s own startup
/// registration), so these tests can assume "zip" is already present.
/// </summary>
public class ArchiveFormatRegistryTests
{
    [Test]
    public void FromExtension_Zip_MatchesZipFormat()
    {
        var format = ArchiveFormatRegistry.FromExtension(@"C:\data\archive.zip");
        Assert.That(format, Is.Not.Null);
        Assert.That(format!.Id, Is.EqualTo("zip"));
    }

    [Test]
    public void FromExtension_Jar_MatchesZipFormat()
    {
        // .jar is a plain ZIP container under a different name.
        var format = ArchiveFormatRegistry.FromExtension(@"C:\data\app.jar");
        Assert.That(format, Is.Not.Null);
        Assert.That(format!.Id, Is.EqualTo("zip"));
    }

    [Test]
    public void FromExtension_Unrecognized_ReturnsNull()
    {
        var format = ArchiveFormatRegistry.FromExtension(@"C:\data\photo.png");
        Assert.That(format, Is.Null);
    }

    [Test]
    public void FromSignature_RealZipFile_MatchesZipFormat()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cc_registry_test_{Guid.NewGuid():N}.dat");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                zip.CreateEntry("hello.txt");
            }

            var format = ArchiveFormatRegistry.FromSignature(path);
            Assert.That(format, Is.Not.Null);
            Assert.That(format!.Id, Is.EqualTo("zip"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void FromSignature_PlainTextFile_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cc_registry_test_{Guid.NewGuid():N}.dat");
        try
        {
            File.WriteAllText(path, "just some text, not an archive");
            var format = ArchiveFormatRegistry.FromSignature(path);
            Assert.That(format, Is.Null);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void Detect_ExtensionWins_EvenWhenContentDoesNotMatchSignature()
    {
        // Extension-first policy: a .zip-named file whose content isn't a real ZIP still
        // resolves to the zip format via Detect, matching the plan's documented policy.
        var path = Path.Combine(Path.GetTempPath(), $"cc_registry_test_{Guid.NewGuid():N}.zip");
        try
        {
            File.WriteAllText(path, "not actually a zip");
            var format = ArchiveFormatRegistry.Detect(path);
            Assert.That(format, Is.Not.Null);
            Assert.That(format!.Id, Is.EqualTo("zip"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void Detect_UnrecognizedExtension_FallsBackToSignature()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cc_registry_test_{Guid.NewGuid():N}.docx");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                zip.CreateEntry("word/document.xml");
            }

            // .docx isn't registered as a zip extension, so Detect must fall back to sniffing.
            var format = ArchiveFormatRegistry.Detect(path);
            Assert.That(format, Is.Not.Null);
            Assert.That(format!.Id, Is.EqualTo("zip"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestCase(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04 }, "7z")]
    [TestCase(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 }, "rar")]
    [TestCase(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 }, "rar")]
    [TestCase(new byte[] { (byte)'B', (byte)'Z', (byte)'h', (byte)'9' }, "tar.bz2")]
    [TestCase(new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 }, "tar.xz")]
    public void FromSignature_MagicBytes_MatchExpectedFormat(byte[] header, string expectedFormatId)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cc_registry_test_{Guid.NewGuid():N}.dat");
        try
        {
            File.WriteAllBytes(path, header);
            var format = ArchiveFormatRegistry.FromSignature(path);
            Assert.That(format, Is.Not.Null);
            Assert.That(format!.Id, Is.EqualTo(expectedFormatId));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void IsSupportedArchiveFile_NonArchive_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cc_registry_test_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "plain text file");
            Assert.That(ArchiveFormatRegistry.IsSupportedArchiveFile(path), Is.False);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
