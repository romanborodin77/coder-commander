using System.Text;
using CoderCommander.Archives;
using CoderCommander.Archives.Tar;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the structural-corruption bug fixed in
/// <see cref="ArchiveFileSystem.CopyFromStreamAsync"/>: writing a file whose name exactly matches
/// an existing (possibly implicit - no explicit directory-marker entry, which many archive tools
/// never write) directory used to succeed silently, since ArchiveTree.FindEntry only matches by
/// exact name and doesn't distinguish file vs. directory. The result was a path used as both a
/// file and a directory at once - LocalFileSystem already fails loud for the identical scenario
/// via File.Move's own IOException; the archive path did not.
/// </summary>
public class ArchiveFileSystemDirectoryClashTests
{
    private string _archivePath = "";

    [SetUp]
    public void CreateArchivePath()
    {
        _archivePath = Path.Combine(Path.GetTempPath(), $"cc_dirclash_test_{Guid.NewGuid():N}.tar");
    }

    [TearDown]
    public void DeleteArchive()
    {
        ArchiveFileSystem.Forget(_archivePath);
        if (File.Exists(_archivePath)) File.Delete(_archivePath);
    }

    [Test]
    public async Task CopyFromStreamAsync_TargetNameIsAnImplicitDirectory_ThrowsInsteadOfCorruptingArchive()
    {
        var format = TarArchiveFormat.Instance;

        // "output/" exists only implicitly, via a child entry - no explicit directory-marker
        // entry, matching how many real-world TAR/ZIP producers actually write archives.
        await using (var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions()))
        {
            var bytes = Encoding.UTF8.GetBytes("log content");
            using var stream = new MemoryStream(bytes);
            await writer.WriteFileAsync("output/log.txt", stream, bytes.Length, DateTime.UtcNow, ArchiveCompressionSpec.Store);
            await writer.CommitAsync();
        }

        var fs = new ArchiveFileSystem(format, _archivePath);
        var payload = Encoding.UTF8.GetBytes("a plain file, not a folder");
        using var payloadStream = new MemoryStream(payload);

        Assert.ThrowsAsync<IOException>(async () =>
            await fs.CopyFromStreamAsync($"{_archivePath}|output", payloadStream));

        using var reader = format.OpenRead(_archivePath);
        var directory = await reader.ReadDirectoryAsync();
        var names = directory.Entries.Select(e => e.FullName.TrimEnd('/')).ToList();
        Assert.That(names, Does.Contain("output/log.txt"), "The archive must be unchanged after the rejected write");
        Assert.That(names, Does.Not.Contain("output"), "No colliding file entry should have been added");
    }
}
