using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Comprehensive cross-format x cross-compression-preset round-trip matrix: packs the same mixed
/// file set (nested folder, empty directory, unicode name) into every creatable format under every
/// preset that format actually supports, then unpacks and verifies full content equality. This is
/// the single place that proves every writable format/preset combination genuinely works end to
/// end, complementing the scattered per-phase spot checks elsewhere.
/// <para>
/// Format+preset pairs are hardcoded as <see cref="TestCaseAttribute"/>s rather than queried from
/// <see cref="ArchiveFormatRegistry"/> at test-discovery time - NUnit enumerates
/// <c>TestCaseSource</c> during discovery, which can run before <c>AssemblySetup</c>'s
/// <c>[OneTimeSetUp]</c> registers the formats, silently producing zero test cases. Looking the
/// format up by id inside the test body (which always runs after setup) avoids that entirely.
/// </para>
/// </summary>
public class AllFormatsCompressionMatrixTests
{
    private string _root = "";
    private readonly LocalFileSystem _fs = new();

    [SetUp]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cc_matrix_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void DeleteWorkspace()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestCase("zip", CompressionPreset.Store)]
    [TestCase("zip", CompressionPreset.Fastest)]
    [TestCase("zip", CompressionPreset.Balanced)]
    [TestCase("zip", CompressionPreset.Maximum)]
    [TestCase("tar", CompressionPreset.Store)]
    [TestCase("tar.gz", CompressionPreset.Balanced)]
    [TestCase("tar.bz2", CompressionPreset.Balanced)]
    public async Task PackThenUnpack_EveryCreatableFormatAndPreset_RoundTripsExactly(string formatId, CompressionPreset preset)
    {
        var format = ArchiveFormatRegistry.ById(formatId);
        Assert.That(format, Is.Not.Null, $"Format \"{formatId}\" should be registered");
        Assert.That(format!.Capabilities.HasFlag(ArchiveCapabilities.Create), Is.True, $"\"{formatId}\" should be creatable");

        var tag = $"{formatId.Replace('.', '_')}_{preset}";
        var sourceDir = Path.Combine(_root, "source_" + tag);
        Directory.CreateDirectory(Path.Combine(sourceDir, "sub"));
        Directory.CreateDirectory(Path.Combine(sourceDir, "empty-dir"));
        File.WriteAllText(Path.Combine(sourceDir, "top.txt"), "top level content for " + tag);
        File.WriteAllText(Path.Combine(sourceDir, "sub", "nested.txt"), "nested content");
        File.WriteAllText(Path.Combine(sourceDir, "юникод-имя.txt"), "unicode file name content");

        var archivePath = Path.Combine(_root, "archive_" + tag + format.DefaultExtension);
        var files = (await _fs.EnumerateAsync(sourceDir, includeHidden: true)).ToList();

        using (var pack = new PackOperation(_fs, files, sourceDir, archivePath,
                   options: new TransferOptions { Compression = new ArchiveCompressionSpec(preset) }))
        {
            await pack.ExecuteAsync();
            Assert.That(pack.State, Is.EqualTo(OperationState.Completed), $"Pack failed for {formatId}/{preset}: {pack.LastError}");
        }

        Assert.That(File.Exists(archivePath), Is.True, $"{formatId}: archive file should exist after packing");

        // Confirm the archive is actually recognized as this format and readable before unpacking.
        var detected = ArchiveFormatRegistry.Detect(archivePath);
        Assert.That(detected?.Id, Is.EqualTo(formatId), $"Packed file should be detected back as {formatId}");

        var destDir = Path.Combine(_root, "dest_" + tag);
        using (var unpack = new UnpackOperation(archivePath, Array.Empty<FileEntry>(), "", _fs, destDir))
        {
            await unpack.ExecuteAsync();
            Assert.That(unpack.State, Is.EqualTo(OperationState.Completed), $"Unpack failed for {formatId}/{preset}: {unpack.LastError}");
        }

        Assert.That(File.ReadAllText(Path.Combine(destDir, "top.txt")), Is.EqualTo("top level content for " + tag));
        Assert.That(File.ReadAllText(Path.Combine(destDir, "sub", "nested.txt")), Is.EqualTo("nested content"));
        Assert.That(File.ReadAllText(Path.Combine(destDir, "юникод-имя.txt")), Is.EqualTo("unicode file name content"));
        Assert.That(Directory.Exists(Path.Combine(destDir, "empty-dir")), Is.True, $"{formatId}: empty directory should round-trip");
    }

    /// <summary>Same matrix, but for a format whose only viable preset (given the environment's
    /// available writer tooling - see SharpCompressFormatsTests) is Store: TAR itself has no
    /// compression of its own, so this is really just re-confirming TAR is exercised above while
    /// documenting why it only appears with one preset in the matrix.</summary>
    [Test]
    public void Tar_OnlySupportsStorePreset()
    {
        var format = ArchiveFormatRegistry.ById("tar");
        Assert.That(format, Is.Not.Null);
        Assert.That(format!.SupportedPresets, Is.EqualTo(new[] { CompressionPreset.Store }));
    }

    /// <summary>TAR.GZ compresses the whole container, not per entry (see TarGzArchiveFormat), so
    /// unlike ZIP there's exactly one usable preset today.</summary>
    [Test]
    public void TarGz_OnlySupportsBalancedPresetForNow()
    {
        var format = ArchiveFormatRegistry.ById("tar.gz");
        Assert.That(format, Is.Not.Null);
        Assert.That(format!.SupportedPresets, Is.EqualTo(new[] { CompressionPreset.Balanced }));
    }

    /// <summary>The three remaining read-only formats (7z, RAR, TAR.XZ) never reach PackOperation
    /// at all - PackDialogForm only lists ArchiveFormatRegistry.Creatable, and OpenWrite throws
    /// immediately if something did try (see SharpCompressFormatsTests). TAR.BZ2 used to be in this
    /// list too until it became writable. Confirms that boundary stays correct as part of this
    /// "every format" sweep.</summary>
    [TestCase("7z")]
    [TestCase("rar")]
    [TestCase("tar.xz")]
    public void ReadOnlyFormats_AreNeverCreatable(string formatId)
    {
        var format = ArchiveFormatRegistry.ById(formatId);
        Assert.That(format, Is.Not.Null, $"Format \"{formatId}\" should be registered");
        Assert.That(format!.Capabilities.HasFlag(ArchiveCapabilities.Create), Is.False, $"\"{formatId}\" must not be creatable");
        Assert.That(ArchiveFormatRegistry.Creatable.Any(f => f.Id == formatId), Is.False,
            $"\"{formatId}\" must not appear in Creatable (what PackDialogForm's format list is built from)");
    }
}
