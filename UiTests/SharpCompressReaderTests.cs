using CoderCommander.Archives.SharpCompress;

namespace CoderCommander.UiTests;

/// <summary>
/// Direct (no UI) test for the file-handle leak fixed in
/// <see cref="SharpCompressReader.ScanAsync"/>: before the fix, the archive's <c>FileStream</c>
/// was opened before the <c>try/finally</c> that disposes it, so a failure inside
/// <c>OpenReader</c> (corrupt or misdetected 7z/RAR data - exactly what a damaged archive from
/// <c>UnpackOperation</c> looks like) left the handle open forever. Mirrors the already-correct
/// <c>ReadDirectoryAsync</c> in the same class, which uses <c>using var fileStream = ...</c> and
/// never had this problem.
/// </summary>
public class SharpCompressReaderTests
{
    private string _path = "";

    [SetUp]
    public void CreateCorruptArchive()
    {
        _path = Path.Combine(Path.GetTempPath(), $"cc_sharpcompress_leak_test_{Guid.NewGuid():N}.7z");
        // Garbage bytes: not a valid 7z signature, so SevenZipArchive.OpenArchive must throw
        // before SharpCompressReader.ScanAsync reaches its first `yield return`.
        File.WriteAllBytes(_path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
    }

    [TearDown]
    public void DeleteCorruptArchive()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Test]
    public async Task ScanAsync_CorruptArchive_DoesNotLeakFileHandle()
    {
        var reader = new SharpCompressReader(_path, SharpCompressKind.SevenZip);

        var threw = false;
        try
        {
            await foreach (var _ in reader.ScanAsync())
            {
            }
        }
        catch (Exception)
        {
            threw = true; // expected: garbage bytes are not a valid 7z archive
        }

        Assert.That(threw, Is.True, "Scanning a corrupt archive was expected to fail");

        // If ScanAsync's FileStream leaked, this exclusive open (FileShare.None) would fail with
        // a sharing-violation IOException since the leaked handle is still open for reading.
        Assert.DoesNotThrow(() =>
        {
            using var exclusive = File.Open(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }, "SharpCompressReader.ScanAsync must release its FileStream even when OpenReader throws");
    }
}
