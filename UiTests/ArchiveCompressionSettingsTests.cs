using System.Text.Json;
using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.UiTests;

/// <summary>
/// Direct (no UI) tests for the per-format compression settings model: the legacy
/// <c>CompressionLevel</c> -> <c>ArchiveCompression</c> migration, and how
/// <see cref="PackOperation"/> actually applies compression/skip settings to real ZIP output.
/// Migration tests call <see cref="SettingsService.Validate"/> directly on in-memory
/// <see cref="AppSettings"/> instances (internal, see AssemblyInfo.cs) rather than through
/// <see cref="SettingsService.Load"/>/<see cref="SettingsService.Save"/>, which would read and
/// write the real settings.json on whatever machine runs the tests.
/// </summary>
public class ArchiveCompressionSettingsTests
{
    [TestCase(0, "Store")]
    [TestCase(1, "Fastest")]
    [TestCase(2, "Balanced")]
    [TestCase(99, "Balanced")]
    public void Validate_LegacyLevelOnly_MigratesIntoZipEntryAndClearsLegacyField(int legacyLevel, string expectedPreset)
    {
        var settings = new AppSettings { LegacyCompressionLevel = legacyLevel };

        SettingsService.Validate(settings);

        Assert.That(settings.ArchiveCompression, Does.ContainKey("zip"));
        Assert.That(settings.ArchiveCompression["zip"], Is.EqualTo(expectedPreset));
        Assert.That(settings.LegacyCompressionLevel, Is.Null, "Migration must clear the legacy field once folded in");
    }

    [Test]
    public void Validate_BothFieldsPresent_ExistingArchiveCompressionWins_AndIsNotOverwritten()
    {
        var settings = new AppSettings { LegacyCompressionLevel = 0 };
        settings.ArchiveCompression["zip"] = "Maximum";
        settings.ArchiveCompression["tar.gz"] = "Balanced";

        SettingsService.Validate(settings);

        Assert.That(settings.ArchiveCompression["zip"], Is.EqualTo("Maximum"), "An existing per-format entry must not be clobbered by the legacy migration");
        Assert.That(settings.ArchiveCompression["tar.gz"], Is.EqualTo("Balanced"));
        Assert.That(settings.ArchiveCompression.Count, Is.EqualTo(2), "Migration must not add entries when ArchiveCompression is already non-empty");
    }

    [Test]
    public void Validate_NeitherFieldPresent_LeavesArchiveCompressionEmpty()
    {
        var settings = new AppSettings();

        SettingsService.Validate(settings);

        Assert.That(settings.ArchiveCompression, Is.Empty);
        Assert.That(settings.LegacyCompressionLevel, Is.Null);
    }

    [Test]
    public void Serializing_AfterMigration_OmitsTheLegacyCompressionLevelKey()
    {
        var settings = new AppSettings { LegacyCompressionLevel = 2 };
        SettingsService.Validate(settings);

        var json = JsonSerializer.Serialize(settings, SettingsService.JsonOpts);

        Assert.That(json, Does.Not.Contain("CompressionLevel"), "The legacy JSON key must disappear once nulled out, given WhenWritingNull");
        Assert.That(json, Does.Contain("\"ArchiveCompression\""));
    }

    [Test]
    public void Serializing_FreshSettings_NeverEmitsTheLegacyKeyEither()
    {
        var settings = new AppSettings();
        var json = JsonSerializer.Serialize(settings, SettingsService.JsonOpts);
        Assert.That(json, Does.Not.Contain("CompressionLevel"));
    }

    // --- PackOperation actually applying these settings to real archive output ---

    private string _root = "";
    private readonly LocalFileSystem _fs = new();

    [SetUp]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cc_compression_settings_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void DeleteWorkspace()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>Enough repetitive, highly-compressible text that Store vs Maximum produce a
    /// clearly, reliably different byte count.</summary>
    private static string CompressibleContent() => string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 5000));

    [Test]
    public async Task Pack_StoreVsMaximum_ProducesMeaningfullySmallerArchiveWithMaximum()
    {
        var sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "data.txt"), CompressibleContent());
        var files = (await _fs.EnumerateAsync(sourceDir, includeHidden: true)).ToList();

        var storePath = Path.Combine(_root, "store.zip");
        using (var pack = new PackOperation(_fs, files, sourceDir, storePath,
                   options: new TransferOptions { Compression = ArchiveCompressionSpec.Store }))
            await pack.ExecuteAsync();

        var maxPath = Path.Combine(_root, "max.zip");
        using (var pack = new PackOperation(_fs, files, sourceDir, maxPath,
                   options: new TransferOptions { Compression = new ArchiveCompressionSpec(CompressionPreset.Maximum) }))
            await pack.ExecuteAsync();

        var storeSize = new FileInfo(storePath).Length;
        var maxSize = new FileInfo(maxPath).Length;

        Assert.That(maxSize, Is.LessThan(storeSize / 2), "Maximum should compress this highly-repetitive text far below Store's size");
    }

    [Test]
    public async Task Pack_NoExplicitCompression_DefaultsToBalanced_SmallerThanStore()
    {
        var sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "data.txt"), CompressibleContent());
        var files = (await _fs.EnumerateAsync(sourceDir, includeHidden: true)).ToList();

        var defaultPath = Path.Combine(_root, "default.zip");
        using (var pack = new PackOperation(_fs, files, sourceDir, defaultPath)) // TransferOptions.Compression left null
            await pack.ExecuteAsync();

        var storePath = Path.Combine(_root, "store.zip");
        using (var pack = new PackOperation(_fs, files, sourceDir, storePath,
                   options: new TransferOptions { Compression = ArchiveCompressionSpec.Store }))
            await pack.ExecuteAsync();

        Assert.That(new FileInfo(defaultPath).Length, Is.LessThan(new FileInfo(storePath).Length),
            "A null Compression must still compress (falls back to Balanced), not silently store uncompressed");
    }

    [Test]
    public async Task Pack_AlreadyCompressedExtension_IsStoredWithoutCompression_WhenSkipOptionEnabled()
    {
        var sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDir);
        var content = CompressibleContent();
        File.WriteAllText(Path.Combine(sourceDir, "data.custom-compressed"), content);
        var files = (await _fs.EnumerateAsync(sourceDir, includeHidden: true)).ToList();

        var archivePath = Path.Combine(_root, "test.zip");
        using (var pack = new PackOperation(_fs, files, sourceDir, archivePath, options: new TransferOptions
               {
                   Compression = new ArchiveCompressionSpec(CompressionPreset.Maximum),
                   SkipCompressionForCompressedFiles = true,
                   AlreadyCompressedExtensions = new[] { ".custom-compressed" }
               }))
        {
            await pack.ExecuteAsync();
            Assert.That(pack.State, Is.EqualTo(OperationState.Completed), $"Pack failed: {pack.LastError}");
        }

        using var archive = System.IO.Compression.ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.Single();

        // Store means the entry's on-disk (compressed) size equals its uncompressed size - Deflate
        // on this highly repetitive text would shrink it dramatically, so this is a reliable signal.
        Assert.That(entry.CompressedLength, Is.EqualTo(entry.Length),
            "An extension in AlreadyCompressedExtensions must be stored (CompressedLength == Length), not Deflate-compressed, even though Maximum was requested");
    }

    [Test]
    public async Task Pack_AlreadyCompressedExtension_IsCompressed_WhenSkipOptionDisabled()
    {
        var sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "data.custom-compressed"), CompressibleContent());
        var files = (await _fs.EnumerateAsync(sourceDir, includeHidden: true)).ToList();

        var archivePath = Path.Combine(_root, "test.zip");
        using (var pack = new PackOperation(_fs, files, sourceDir, archivePath, options: new TransferOptions
               {
                   Compression = new ArchiveCompressionSpec(CompressionPreset.Maximum),
                   SkipCompressionForCompressedFiles = false,
                   AlreadyCompressedExtensions = new[] { ".custom-compressed" }
               }))
            await pack.ExecuteAsync();

        using var archive = System.IO.Compression.ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.Single();

        Assert.That(entry.CompressedLength, Is.LessThan(entry.Length),
            "With the skip option disabled, even a listed extension should compress normally");
    }
}
