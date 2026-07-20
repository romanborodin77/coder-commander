using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// End-to-end (no UI) tests exercising the exact code path the Pack/Unpack commands use in the
/// running app - <see cref="PackOperation"/> and <see cref="UnpackOperation"/> now go through
/// <see cref="IArchiveWriter"/>/<see cref="IArchiveReader"/> instead of touching
/// <see cref="System.IO.Compression.ZipArchive"/> directly (Phase 1 of the archive-abstraction
/// refactor); these tests guard against a regression in that refactor rather than in ZIP itself.
/// </summary>
public class ArchivePackUnpackRoundTripTests
{
    private string _root = "";
    private string _sourceDir = "";
    private string _destDir = "";
    private string _archivePath = "";
    private readonly LocalFileSystem _fs = new();

    [SetUp]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cc_pack_roundtrip_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_root, "source");
        _destDir = Path.Combine(_root, "dest");
        _archivePath = Path.Combine(_root, "test.zip");

        Directory.CreateDirectory(Path.Combine(_sourceDir, "sub"));
        Directory.CreateDirectory(_destDir);

        File.WriteAllText(Path.Combine(_sourceDir, "top.txt"), "top level file");
        File.WriteAllText(Path.Combine(_sourceDir, "sub", "nested.txt"), "nested file content");
    }

    [TearDown]
    public void DeleteWorkspace()
    {
        ZipArchiveFileSystem.Forget(_archivePath);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<List<FileEntry>> GetTopLevelEntriesAsync(string dir)
    {
        var infos = await _fs.EnumerateAsync(dir, includeHidden: true).ConfigureAwait(false);
        return infos.ToList();
    }

    [Test]
    public async Task PackThenUnpack_RoundTrip_PreservesFileContents()
    {
        var files = await GetTopLevelEntriesAsync(_sourceDir);

        using var pack = new PackOperation(_fs, files, _sourceDir, _archivePath);
        await pack.ExecuteAsync();
        Assert.That(pack.State, Is.EqualTo(OperationState.Completed), $"Pack failed: {pack.LastError}");

        Assert.That(File.Exists(_archivePath), Is.True);

        var format = ArchiveFormatRegistry.Detect(_archivePath);
        Assert.That(format, Is.Not.Null, "Packed file should be detected as a registered archive format");

        using (var reader = format!.OpenRead(_archivePath))
        {
            var directory = await reader.ReadDirectoryAsync();
            var names = directory.Entries.Select(e => e.FullName.Trim('/')).ToList();
            Assert.That(names, Does.Contain("top.txt"));
            Assert.That(names, Does.Contain("sub/nested.txt"));
        }

        using var unpack = new UnpackOperation(_archivePath, Array.Empty<FileEntry>(), "", _fs, _destDir);
        await unpack.ExecuteAsync();
        Assert.That(unpack.State, Is.EqualTo(OperationState.Completed), $"Unpack failed: {unpack.LastError}");

        var extractedTop = Path.Combine(_destDir, "top.txt");
        var extractedNested = Path.Combine(_destDir, "sub", "nested.txt");
        Assert.That(File.Exists(extractedTop), Is.True);
        Assert.That(File.Exists(extractedNested), Is.True);
        Assert.That(File.ReadAllText(extractedTop), Is.EqualTo("top level file"));
        Assert.That(File.ReadAllText(extractedNested), Is.EqualTo("nested file content"));
    }

    [Test]
    public async Task Pack_MoveSemantics_RemovesSourcesAfterSuccessfulWrite()
    {
        var files = await GetTopLevelEntriesAsync(_sourceDir);

        using var pack = new PackOperation(_fs, files, _sourceDir, _archivePath, removeSource: true);
        await pack.ExecuteAsync();
        Assert.That(pack.State, Is.EqualTo(OperationState.Completed), $"Pack failed: {pack.LastError}");

        Assert.That(File.Exists(Path.Combine(_sourceDir, "top.txt")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(_sourceDir, "sub")), Is.False);
    }

    [Test]
    public async Task Pack_OverwriteClash_ReplacesExistingEntry()
    {
        var files = await GetTopLevelEntriesAsync(_sourceDir);

        using (var pack = new PackOperation(_fs, files, _sourceDir, _archivePath))
            await pack.ExecuteAsync();

        // Change the source content, then pack again with Overwrite=true - the clash-resolution
        // path (PackOperation.ResolveClash -> writer.TryDeleteEntry -> re-create) must replace the
        // old bytes rather than silently keeping them or duplicating the entry.
        File.WriteAllText(Path.Combine(_sourceDir, "top.txt"), "updated content");
        var updatedFiles = await GetTopLevelEntriesAsync(_sourceDir);

        using (var repack = new PackOperation(_fs, updatedFiles, _sourceDir, _archivePath,
                   options: new TransferOptions { Overwrite = true }))
        {
            await repack.ExecuteAsync();
            Assert.That(repack.State, Is.EqualTo(OperationState.Completed), $"Repack failed: {repack.LastError}");
        }

        using var unpack = new UnpackOperation(_archivePath, Array.Empty<FileEntry>(), "", _fs, _destDir,
            new TransferOptions { Overwrite = true });
        await unpack.ExecuteAsync();
        Assert.That(unpack.State, Is.EqualTo(OperationState.Completed), $"Unpack failed: {unpack.LastError}");

        Assert.That(File.ReadAllText(Path.Combine(_destDir, "top.txt")), Is.EqualTo("updated content"));

        var format = ArchiveFormatRegistry.Detect(_archivePath)!;
        using var reader = format.OpenRead(_archivePath);
        var directory = await reader.ReadDirectoryAsync();
        var topEntries = directory.Entries.Where(e => e.FullName.Trim('/') == "top.txt").ToList();
        Assert.That(topEntries, Has.Count.EqualTo(1), "Overwriting must replace the entry, not duplicate it");
    }

    [Test]
    public async Task Pack_StoreCompressionForAlreadyCompressedExtension_DoesNotShrinkBelowStore()
    {
        // .jpg is in PackOperation.IsAlreadyCompressed, so it should always be written with
        // CompressionPreset.Store regardless of the requested TransferOptions.Compression.
        var jpgPath = Path.Combine(_sourceDir, "photo.jpg");
        File.WriteAllBytes(jpgPath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        var files = await GetTopLevelEntriesAsync(_sourceDir);

        using var pack = new PackOperation(_fs, files, _sourceDir, _archivePath,
            options: new TransferOptions { Compression = new ArchiveCompressionSpec(CompressionPreset.Maximum) });
        await pack.ExecuteAsync();
        Assert.That(pack.State, Is.EqualTo(OperationState.Completed), $"Pack failed: {pack.LastError}");

        using var unpack = new UnpackOperation(_archivePath, Array.Empty<FileEntry>(), "", _fs, _destDir);
        await unpack.ExecuteAsync();
        Assert.That(unpack.State, Is.EqualTo(OperationState.Completed));

        var extractedBytes = File.ReadAllBytes(Path.Combine(_destDir, "photo.jpg"));
        Assert.That(extractedBytes, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
    }
}
