using CoderCommander.Archives;
using CoderCommander.Archives.SharpCompress;
using CoderCommander.FileSystem;
using CoderCommander.Operations;
using SharpCompress.Common;
using SharpCompress.Writers.SevenZip;
using SharpCompress.Writers.Tar;

namespace CoderCommander.UiTests;

/// <summary>
/// Direct (no UI) tests for the read-only SharpCompress-backed formats (7z, RAR, TAR.BZ2, TAR.XZ).
/// TAR.BZ2/TAR.XZ/7z fixtures are built with SharpCompress's own writers (test-only use - the app
/// itself never writes these formats). RAR has no writer anywhere in this environment (SharpCompress
/// can't write it, and there's no RarLab tooling available), so RAR tests are limited to format
/// registration/capabilities/signature/write-rejection rather than a full content round trip.
/// </summary>
public class SharpCompressFormatsTests
{
    private string _root = "";
    private readonly LocalFileSystem _fs = new();

    [SetUp]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cc_sharpcompress_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void DeleteWorkspace()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void WriteTarFixture(string path, CompressionType compression, IReadOnlyDictionary<string, string> files)
    {
        using var stream = File.Create(path);
        using var writer = new TarWriter(stream, new TarWriterOptions(compression, finalizeArchiveOnClose: true));
        foreach (var (name, content) in files)
        {
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            writer.Write(name, ms, DateTime.UtcNow);
        }
    }

    private static void WriteSevenZipFixture(string path, IReadOnlyDictionary<string, string> files)
    {
        using var stream = File.Create(path);
        using var writer = new SevenZipWriter(stream, new SevenZipWriterOptions());
        foreach (var (name, content) in files)
        {
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            writer.Write(name, ms, DateTime.UtcNow);
        }
    }

    /// <summary>TAR.BZ2 is deliberately absent here - it's the one SharpCompress-backed format
    /// that IS writable (see <c>TarBz2ArchiveFormat</c>/<c>SharpCompressTarWriter</c>), since
    /// SharpCompress's own TarWriter supports BZip2 natively. 7z/RAR/TAR.XZ have no writable path
    /// at all (7z needs a native 7z.dll nobody's built; RAR write is legally unavailable; TAR.XZ
    /// has no XZ encoder anywhere in this SharpCompress version).</summary>
    private static readonly IReadOnlyList<(IArchiveFormat Format, string Extension)> ReadOnlyFormats = new (IArchiveFormat, string)[]
    {
        (SevenZipArchiveFormat.Instance, ".7z"),
        (RarArchiveFormat.Instance, ".rar"),
        (TarXzArchiveFormat.Instance, ".tar.xz"),
    };

    [Test]
    public void AllFormats_DoNotAdvertiseWriteCapabilities()
    {
        foreach (var (format, _) in ReadOnlyFormats)
        {
            Assert.That(format.Capabilities.HasFlag(ArchiveCapabilities.Create), Is.False, $"{format.Id} must not advertise Create");
            Assert.That(format.Capabilities.HasFlag(ArchiveCapabilities.AddEntries), Is.False, $"{format.Id} must not advertise AddEntries");
            Assert.That(format.Capabilities.HasFlag(ArchiveCapabilities.DeleteEntries), Is.False, $"{format.Id} must not advertise DeleteEntries");
            Assert.That(format.Capabilities.HasFlag(ArchiveCapabilities.Read), Is.True, $"{format.Id} must still advertise Read");
        }
    }

    [Test]
    public void AllFormats_FromExtension_ResolveToThemselves()
    {
        foreach (var (format, extension) in ReadOnlyFormats)
        {
            var resolved = ArchiveFormatRegistry.FromExtension("archive" + extension);
            Assert.That(resolved, Is.Not.Null, $"extension {extension} should resolve");
            Assert.That(resolved!.Id, Is.EqualTo(format.Id));
        }
    }

    [Test]
    public void AllFormats_OpenWrite_ThrowsAndNeverTouchesTheFile()
    {
        foreach (var (format, extension) in ReadOnlyFormats)
        {
            var path = Path.Combine(_root, "target" + extension);
            Assert.That(File.Exists(path), Is.False);

            Assert.Throws<NotSupportedException>(() => format.OpenWrite(path, new ArchiveWriteOptions()),
                $"{format.Id} must reject OpenWrite");

            Assert.That(File.Exists(path), Is.False, $"{format.Id}.OpenWrite must not create a file even on rejection");
        }
    }

    [Test]
    public void OpenWrite_OnExistingReadOnlyArchive_LeavesItByteForByteUnchanged()
    {
        var path = Path.Combine(_root, "existing.7z");
        WriteSevenZipFixture(path, new Dictionary<string, string> { ["a.txt"] = "hello" });
        var before = File.ReadAllBytes(path);

        Assert.Throws<NotSupportedException>(() => SevenZipArchiveFormat.Instance.OpenWrite(path, new ArchiveWriteOptions()));

        var after = File.ReadAllBytes(path);
        Assert.That(after, Is.EqualTo(before));
    }

    // TAR.XZ has no content round trip here: SharpCompress can decompress xz but has no xz
    // compressor/writer at all (only TarWriter+BZip2/GZip are available), so there's no way to
    // produce a genuine .tar.xz fixture in-process. That's fine for coverage purposes - TAR.BZ2
    // and TAR.XZ share the exact same SharpCompressReader/ReaderFactory.OpenReader code path in
    // our own code, differing only in which codec SharpCompress decodes internally, so this test
    // (and the format-recognition/capability/write-rejection tests above, which do cover TAR.XZ)
    // already exercise the code that matters.
    [TestCase(".tar.bz2")]
    public async Task TarCompressed_ReadDirectoryAndScan_ReturnsWrittenEntries(string extension)
    {
        var compression = CompressionType.BZip2;
        var path = Path.Combine(_root, "fixture" + extension);
        WriteTarFixture(path, compression, new Dictionary<string, string>
        {
            ["top.txt"] = "top level content",
            ["sub/nested.txt"] = "nested content"
        });

        var format = ArchiveFormatRegistry.Detect(path);
        Assert.That(format, Is.Not.Null);
        Assert.That(format!.Capabilities.HasFlag(ArchiveCapabilities.Read), Is.True);

        using var reader = format.OpenRead(path);
        Assert.That(reader.SupportsRandomAccess, Is.False);

        var directory = await reader.ReadDirectoryAsync();
        var names = directory.Entries.Select(e => e.FullName.TrimEnd('/')).ToList();
        Assert.That(names, Does.Contain("top.txt"));
        Assert.That(names, Does.Contain("sub/nested.txt"));

        var contents = new Dictionary<string, string>();
        await foreach (var item in reader.ScanAsync())
        {
            using var content = item.Content;
            if (item.Entry.IsDirectory) continue;
            using var sr = new StreamReader(content);
            contents[item.Entry.FullName.TrimEnd('/')] = await sr.ReadToEndAsync();
        }

        Assert.That(contents["top.txt"], Is.EqualTo("top level content"));
        Assert.That(contents["sub/nested.txt"], Is.EqualTo("nested content"));
    }

    [Test]
    public async Task SevenZip_ReadDirectoryAndScan_ReturnsWrittenEntries()
    {
        var path = Path.Combine(_root, "fixture.7z");
        WriteSevenZipFixture(path, new Dictionary<string, string>
        {
            ["one.txt"] = "first",
            ["two.txt"] = "second"
        });

        var format = ArchiveFormatRegistry.Detect(path);
        Assert.That(format, Is.Not.Null);
        Assert.That(format!.Id, Is.EqualTo("7z"));

        using var reader = format.OpenRead(path);
        var directory = await reader.ReadDirectoryAsync();
        var names = directory.Entries.Select(e => e.FullName.TrimEnd('/')).ToList();
        Assert.That(names, Does.Contain("one.txt"));
        Assert.That(names, Does.Contain("two.txt"));
    }

    [Test]
    public async Task Unpack_FromTarBz2_ExtractsFilesCorrectly()
    {
        var archivePath = Path.Combine(_root, "fixture.tar.bz2");
        WriteTarFixture(archivePath, CompressionType.BZip2, new Dictionary<string, string>
        {
            ["a.txt"] = "alpha",
            ["dir/b.txt"] = "beta"
        });

        var destDir = Path.Combine(_root, "dest");
        Directory.CreateDirectory(destDir);

        using var unpack = new UnpackOperation(archivePath, Array.Empty<FileEntry>(), "", _fs, destDir);
        await unpack.ExecuteAsync();

        Assert.That(unpack.State, Is.EqualTo(OperationState.Completed), $"Unpack failed: {unpack.LastError}");
        Assert.That(File.ReadAllText(Path.Combine(destDir, "a.txt")), Is.EqualTo("alpha"));
        Assert.That(File.ReadAllText(Path.Combine(destDir, "dir", "b.txt")), Is.EqualTo("beta"));
    }

    [Test]
    public async Task ScanAsync_CancelledMidRead_ThrowsOperationCanceledCleanly()
    {
        var path = Path.Combine(_root, "fixture.tar.bz2");
        WriteTarFixture(path, CompressionType.BZip2, new Dictionary<string, string>
        {
            ["one.txt"] = "1",
            ["two.txt"] = "2",
            ["three.txt"] = "3"
        });

        var format = TarBz2ArchiveFormat.Instance;
        using var reader = format.OpenRead(path);

        using var cts = new CancellationTokenSource();
        var seen = 0;
        OperationCanceledException? caught = null;

        try
        {
            // Disposing without reading is deliberate here - it exercises NonDisposingStream's
            // drain-on-dispose behavior (see its doc comment), not just the happy path where the
            // caller already read everything.
            await foreach (var item in reader.ScanAsync(cts.Token))
            {
                item.Content.Dispose();
                seen++;
                if (seen == 1)
                    cts.Cancel();
            }
        }
        catch (OperationCanceledException ex)
        {
            caught = ex;
        }

        Assert.That(caught, Is.Not.Null, "Cancelling the token mid-scan should raise OperationCanceledException");
        Assert.That(seen, Is.EqualTo(1), "Cancellation should stop enumeration right after the token is cancelled");
    }

    /// <summary>Guards the exact bug this reader tripped over during development: SharpCompress's
    /// IReader (unlike System.Formats.Tar's TarReader) fails to advance past an entry whose stream
    /// was disposed without being read, silently truncating everything after it. Consumers
    /// (UnpackOperation, ArchiveFileSystem) routinely skip entries - not selected, encrypted -
    /// by disposing the stream without reading it, so this must work transparently.</summary>
    [Test]
    public async Task ScanAsync_SkippingAnEntryWithoutReadingIt_DoesNotBreakSubsequentEntries()
    {
        var path = Path.Combine(_root, "fixture-skip.tar.bz2");
        WriteTarFixture(path, CompressionType.BZip2, new Dictionary<string, string>
        {
            ["skip-me.txt"] = "should be skipped without ever being read",
            ["keep-me.txt"] = "must still be reachable and readable afterwards"
        });

        var format = TarBz2ArchiveFormat.Instance;
        using var reader = format.OpenRead(path);

        var contents = new Dictionary<string, string>();
        await foreach (var item in reader.ScanAsync())
        {
            if (item.Entry.FullName.TrimEnd('/') == "skip-me.txt")
            {
                item.Content.Dispose(); // deliberately not read - this is the case under test
                continue;
            }

            using var content = item.Content;
            using var sr = new StreamReader(content);
            contents[item.Entry.FullName.TrimEnd('/')] = await sr.ReadToEndAsync();
        }

        Assert.That(contents, Does.ContainKey("keep-me.txt"), "The entry after a skipped one must still be reachable");
        Assert.That(contents["keep-me.txt"], Is.EqualTo("must still be reachable and readable afterwards"));
    }

    // --- Encrypted entries: SharpCompress doesn't offer a writer that produces genuinely
    // encrypted fixtures for any of these formats, so the encrypted-handling logic itself is
    // tested against a minimal fake IArchiveFormat/IArchiveReader instead of a real archive -
    // this isolates CoderCommander's own IsEncrypted handling from whether a real crypto fixture
    // can be produced in this environment.

    [OneTimeSetUp]
    public void RegisterFakeEncryptedFormat()
    {
        if (ArchiveFormatRegistry.ById(FakeEncryptedFormat.FormatId) == null)
            ArchiveFormatRegistry.Register(new FakeEncryptedFormat());
    }

    [Test]
    public async Task Unpack_EncryptedEntry_IsSkippedCleanly_OthersStillExtract()
    {
        var archivePath = Path.Combine(_root, "test" + FakeEncryptedFormat.Extension);
        File.WriteAllText(archivePath, "dummy - content is ignored by FakeEncryptedFormat");

        var destDir = Path.Combine(_root, "dest");
        Directory.CreateDirectory(destDir);

        using var unpack = new UnpackOperation(archivePath, Array.Empty<FileEntry>(), "", _fs, destDir);
        await unpack.ExecuteAsync();

        Assert.That(unpack.State, Is.EqualTo(OperationState.Completed), $"Unpack failed: {unpack.LastError}");
        Assert.That(File.Exists(Path.Combine(destDir, "secret.txt")), Is.False, "Encrypted entry must not be extracted");
        Assert.That(File.Exists(Path.Combine(destDir, "plain.txt")), Is.True, "Non-encrypted entries must still extract normally");
        Assert.That(File.ReadAllText(Path.Combine(destDir, "plain.txt")), Is.EqualTo("hello"));
    }

    [Test]
    public void ArchiveFileSystem_OpenReadAsync_EncryptedEntry_ThrowsNotSupportedNotRawException()
    {
        var archivePath = Path.Combine(_root, "panel" + FakeEncryptedFormat.Extension);
        File.WriteAllText(archivePath, "dummy");

        var format = new FakeEncryptedFormat();
        var vfs = format.CreateFileSystem(archivePath)!;
        var entryPath = CoderCommander.FileSystem.ArchivePath.MakePath(archivePath, "secret.txt");

        Assert.ThrowsAsync<NotSupportedException>(async () => await vfs.OpenReadAsync(entryPath));
    }

    private sealed class FakeEncryptedFormat : IArchiveFormat
    {
        public const string FormatId = "fake-encrypted-test-format";
        public const string Extension = ".fakeenc";

        public string Id => FormatId;
        public string DisplayNameKey => "";
        public IReadOnlyList<string> Extensions { get; } = new[] { Extension };
        public string DefaultExtension => Extension;
        public ArchiveCapabilities Capabilities => ArchiveCapabilities.Read | ArchiveCapabilities.Browse;
        public IReadOnlyList<CompressionPreset> SupportedPresets { get; } = Array.Empty<CompressionPreset>();
        public bool MatchesSignature(ReadOnlySpan<byte> header) => false;
        public IArchiveReader OpenRead(string archivePath) => new Reader();
        public IArchiveWriter OpenWrite(string archivePath, ArchiveWriteOptions options) => throw new NotSupportedException();
        public IFileSystem? CreateFileSystem(string archivePath) => new ArchiveFileSystem(this, archivePath);

        private sealed class Reader : IArchiveReader
        {
            private static readonly ArchiveEntryRecord Secret = new()
            {
                FullName = "secret.txt", IsDirectory = false, Size = 6, Index = 0, IsEncrypted = true
            };
            private static readonly ArchiveEntryRecord Plain = new()
            {
                FullName = "plain.txt", IsDirectory = false, Size = 5, Index = 1, IsEncrypted = false
            };

            public bool SupportsRandomAccess => false;

            public Task<ArchiveDirectory> ReadDirectoryAsync(CancellationToken ct = default) =>
                Task.FromResult(new ArchiveDirectory(new[] { Secret, Plain }, isValid: true));

            public Stream OpenEntry(ArchiveEntryRecord entry) => throw new NotSupportedException();

            public async IAsyncEnumerable<ArchiveEntryStream> ScanAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
            {
                yield return new ArchiveEntryStream(Secret, new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6 }));
                await Task.Yield();
                yield return new ArchiveEntryStream(Plain, new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello")));
            }

            public void Dispose() { }
        }
    }
}
