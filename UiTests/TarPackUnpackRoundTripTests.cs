using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// End-to-end (no UI) TAR/TAR.GZ tests through the same <see cref="PackOperation"/>/
/// <see cref="UnpackOperation"/> path the app's Pack/Unpack commands use - covering the properties
/// that specifically distinguish TAR from ZIP: unicode names, empty directories, nesting, a
/// larger streamed file, and timestamps that ZIP's DOS-date range would have clamped (pre-1980,
/// post-2107) but TAR/PAX headers store exactly.
/// </summary>
[TestFixture("test.tar")]
[TestFixture("test.tar.gz")]
public class TarPackUnpackRoundTripTests
{
    private readonly string _archiveFileName;
    private string _root = "";
    private string _sourceDir = "";
    private string _destDir = "";
    private string _archivePath = "";
    private readonly LocalFileSystem _fs = new();

    public TarPackUnpackRoundTripTests(string archiveFileName)
    {
        _archiveFileName = archiveFileName;
    }

    [SetUp]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cc_tar_roundtrip_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_root, "source");
        _destDir = Path.Combine(_root, "dest");
        _archivePath = Path.Combine(_root, _archiveFileName);

        Directory.CreateDirectory(Path.Combine(_sourceDir, "sub"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "empty-dir"));
        Directory.CreateDirectory(_destDir);

        File.WriteAllText(Path.Combine(_sourceDir, "top.txt"), "top level file");
        File.WriteAllText(Path.Combine(_sourceDir, "sub", "nested.txt"), "nested file content");
        File.WriteAllText(Path.Combine(_sourceDir, "юникод-имя.txt"), "unicode file name content");
    }

    [TearDown]
    public void DeleteWorkspace()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<List<FileEntry>> GetTopLevelEntriesAsync(string dir) =>
        (await _fs.EnumerateAsync(dir, includeHidden: true).ConfigureAwait(false)).ToList();

    [Test]
    public async Task PackThenUnpack_RoundTrip_PreservesNamesStructureAndContent()
    {
        var files = await GetTopLevelEntriesAsync(_sourceDir);

        using var pack = new PackOperation(_fs, files, _sourceDir, _archivePath);
        await pack.ExecuteAsync();
        Assert.That(pack.State, Is.EqualTo(OperationState.Completed), $"Pack failed: {pack.LastError}");
        Assert.That(File.Exists(_archivePath), Is.True);

        var format = ArchiveFormatRegistry.Detect(_archivePath);
        Assert.That(format, Is.Not.Null);
        Assert.That(format!.Capabilities.HasFlag(ArchiveCapabilities.Browse), Is.True);

        using (var reader = format.OpenRead(_archivePath))
        {
            Assert.That(reader.SupportsRandomAccess, Is.False, "TAR/TAR.GZ must be reported as sequential-access only");

            var directory = await reader.ReadDirectoryAsync();
            var names = directory.Entries.Select(e => e.FullName.TrimEnd('/')).ToList();
            Assert.That(names, Does.Contain("top.txt"));
            Assert.That(names, Does.Contain("sub/nested.txt"));
            Assert.That(names, Does.Contain("empty-dir"));
            Assert.That(names, Does.Contain("юникод-имя.txt"));
        }

        using var unpack = new UnpackOperation(_archivePath, Array.Empty<FileEntry>(), "", _fs, _destDir);
        await unpack.ExecuteAsync();
        Assert.That(unpack.State, Is.EqualTo(OperationState.Completed), $"Unpack failed: {unpack.LastError}");

        Assert.That(File.ReadAllText(Path.Combine(_destDir, "top.txt")), Is.EqualTo("top level file"));
        Assert.That(File.ReadAllText(Path.Combine(_destDir, "sub", "nested.txt")), Is.EqualTo("nested file content"));
        Assert.That(File.ReadAllText(Path.Combine(_destDir, "юникод-имя.txt")), Is.EqualTo("unicode file name content"));
        Assert.That(Directory.Exists(Path.Combine(_destDir, "empty-dir")), Is.True);
    }

    [Test]
    public async Task PackThenUnpack_LargerStreamedFile_RoundTripsExactly()
    {
        var random = new Random(12345);
        var payload = new byte[2 * 1024 * 1024 + 137]; // a bit over 2MB, not aligned to any block size
        random.NextBytes(payload);
        File.WriteAllBytes(Path.Combine(_sourceDir, "big.bin"), payload);

        var files = await GetTopLevelEntriesAsync(_sourceDir);
        using var pack = new PackOperation(_fs, files, _sourceDir, _archivePath);
        await pack.ExecuteAsync();
        Assert.That(pack.State, Is.EqualTo(OperationState.Completed), $"Pack failed: {pack.LastError}");

        using var unpack = new UnpackOperation(_archivePath, Array.Empty<FileEntry>(), "", _fs, _destDir);
        await unpack.ExecuteAsync();
        Assert.That(unpack.State, Is.EqualTo(OperationState.Completed), $"Unpack failed: {unpack.LastError}");

        var roundTripped = File.ReadAllBytes(Path.Combine(_destDir, "big.bin"));
        Assert.That(roundTripped, Is.EqualTo(payload));
    }

    [Test]
    public async Task PackThenUnpack_TimestampOutsideZipDosRange_IsNotClamped()
    {
        // 1970-01-01 is before ZIP's DOS-date floor (1980) - PackOperation's ZIP path would clamp
        // this, but TAR/PAX headers store arbitrary Unix timestamps natively.
        var oldTimestamp = new DateTime(1970, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var filePath = Path.Combine(_sourceDir, "old.txt");
        File.WriteAllText(filePath, "ancient file");
        File.SetLastWriteTimeUtc(filePath, oldTimestamp);

        var files = await GetTopLevelEntriesAsync(_sourceDir);
        using var pack = new PackOperation(_fs, files, _sourceDir, _archivePath);
        await pack.ExecuteAsync();
        Assert.That(pack.State, Is.EqualTo(OperationState.Completed), $"Pack failed: {pack.LastError}");

        var format = ArchiveFormatRegistry.Detect(_archivePath)!;
        using var reader = format.OpenRead(_archivePath);
        var directory = await reader.ReadDirectoryAsync();
        var entry = directory.Entries.First(e => e.FullName.TrimEnd('/') == "old.txt");

        Assert.That(entry.LastWriteTimeUtc.Year, Is.EqualTo(1970), "TAR must preserve a pre-1980 timestamp, not clamp it like ZIP would");
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
    public async Task Pack_AddToExistingArchive_PreservesPriorEntries()
    {
        var files = await GetTopLevelEntriesAsync(_sourceDir);
        using (var pack = new PackOperation(_fs, files, _sourceDir, _archivePath))
            await pack.ExecuteAsync();

        var extraDir = Path.Combine(_root, "extra");
        Directory.CreateDirectory(extraDir);
        File.WriteAllText(Path.Combine(extraDir, "added-later.txt"), "added in a second pack");
        var extraFiles = await GetTopLevelEntriesAsync(extraDir);

        using (var pack2 = new PackOperation(_fs, extraFiles, extraDir, _archivePath))
            await pack2.ExecuteAsync();

        var format = ArchiveFormatRegistry.Detect(_archivePath)!;
        using var reader = format.OpenRead(_archivePath);
        var directory = await reader.ReadDirectoryAsync();
        var names = directory.Entries.Select(e => e.FullName.TrimEnd('/')).ToList();

        Assert.That(names, Does.Contain("top.txt"), "Original entries must survive adding to the archive a second time");
        Assert.That(names, Does.Contain("sub/nested.txt"));
        Assert.That(names, Does.Contain("added-later.txt"));
    }
}
