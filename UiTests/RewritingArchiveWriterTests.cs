using CoderCommander.Archives;
using CoderCommander.Archives.SharpCompress;
using CoderCommander.Archives.Tar;

namespace CoderCommander.UiTests;

/// <summary>
/// Direct (no UI) tests for <see cref="RewritingArchiveWriter"/> itself (TAR is the concrete
/// format exercising it) - the higher-level round trip already covered via
/// <c>TarPackUnpackRoundTripTests</c> doesn't probe the "abandoned session" case, which is the
/// one that actually matters for data safety.
/// </summary>
public class RewritingArchiveWriterTests
{
    private string _archivePath = "";

    [SetUp]
    public void CreateArchivePath()
    {
        _archivePath = Path.Combine(Path.GetTempPath(), $"cc_rewrite_test_{Guid.NewGuid():N}.tar");
    }

    [TearDown]
    public void DeleteArchive()
    {
        if (File.Exists(_archivePath)) File.Delete(_archivePath);
    }

    private static async Task WriteTextEntryAsync(IArchiveWriter writer, string name, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(bytes);
        await writer.WriteFileAsync(name, stream, bytes.Length, DateTime.UtcNow, ArchiveCompressionSpec.Store);
    }

    private async Task<List<string>> ReadEntryNamesAsync()
    {
        var format = TarArchiveFormat.Instance;
        using var reader = format.OpenRead(_archivePath);
        var directory = await reader.ReadDirectoryAsync();
        return directory.Entries.Select(e => e.FullName.TrimEnd('/')).ToList();
    }

    [Test]
    public async Task Commit_AddingToExistingArchive_PreservesPriorEntries()
    {
        var format = TarArchiveFormat.Instance;

        await using (var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions()))
        {
            await WriteTextEntryAsync(writer, "first.txt", "first content");
            await WriteTextEntryAsync(writer, "second.txt", "second content");
            await writer.CommitAsync();
        }

        await using (var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions()))
        {
            await WriteTextEntryAsync(writer, "third.txt", "third content");
            await writer.CommitAsync();
        }

        var names = await ReadEntryNamesAsync();
        Assert.That(names, Does.Contain("first.txt"));
        Assert.That(names, Does.Contain("second.txt"));
        Assert.That(names, Does.Contain("third.txt"));
    }

    [Test]
    public async Task Commit_DeletingAnEntry_RemovesOnlyThatEntry()
    {
        var format = TarArchiveFormat.Instance;

        await using (var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions()))
        {
            await WriteTextEntryAsync(writer, "keep.txt", "keep me");
            await WriteTextEntryAsync(writer, "remove.txt", "remove me");
            await writer.CommitAsync();
        }

        using (var reader = format.OpenRead(_archivePath))
        {
            var directory = await reader.ReadDirectoryAsync();
            var toRemove = directory.Entries.Single(e => e.FullName.TrimEnd('/') == "remove.txt");

            await using var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions());
            writer.TryDeleteEntry(toRemove);
            await writer.CommitAsync();
        }

        var names = await ReadEntryNamesAsync();
        Assert.That(names, Does.Contain("keep.txt"));
        Assert.That(names, Does.Not.Contain("remove.txt"));
    }

    /// <summary>The most important test: a session that never reaches <see cref="IArchiveWriter.CommitAsync"/>
    /// (simulating a crash or an exception thrown mid-loop by the caller) must leave the original
    /// archive completely byte-for-byte untouched, not partially rewritten or truncated.</summary>
    [Test]
    public async Task AbandonedSession_WithoutCommit_LeavesOriginalArchiveByteForByteUnchanged()
    {
        var format = TarArchiveFormat.Instance;

        await using (var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions()))
        {
            await WriteTextEntryAsync(writer, "original.txt", "original content");
            await writer.CommitAsync();
        }

        var originalBytes = await File.ReadAllBytesAsync(_archivePath);

        // Open again, stage changes, but deliberately never call CommitAsync - `await using`
        // disposes the writer as if an exception had aborted the operation partway through.
        await using (var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions()))
        {
            await WriteTextEntryAsync(writer, "should-not-appear.txt", "this must not end up in the archive");
        }

        var afterAbandonedSession = await File.ReadAllBytesAsync(_archivePath);
        Assert.That(afterAbandonedSession, Is.EqualTo(originalBytes), "Original archive must be untouched when the writer is disposed without committing");

        var names = await ReadEntryNamesAsync();
        Assert.That(names, Does.Contain("original.txt"));
        Assert.That(names, Does.Not.Contain("should-not-appear.txt"));
    }

    [Test]
    public async Task AbandonedSession_ThrowingMidLoop_LeavesOriginalArchiveIntact()
    {
        var format = TarArchiveFormat.Instance;

        await using (var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions()))
        {
            await WriteTextEntryAsync(writer, "safe.txt", "must survive");
            await writer.CommitAsync();
        }

        var originalBytes = await File.ReadAllBytesAsync(_archivePath);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions());
            await WriteTextEntryAsync(writer, "partial.txt", "half-written");
            throw new InvalidOperationException("simulated failure mid-pack");
            // writer.CommitAsync() is never reached.
        });

        var afterFailure = await File.ReadAllBytesAsync(_archivePath);
        Assert.That(afterFailure, Is.EqualTo(originalBytes));
    }

    /// <summary>Regression test for the case-folding bug in RewritingArchiveWriter's internal
    /// Key(): TAR is case-sensitive and can legitimately contain both "README.txt" and
    /// "readme.txt" as distinct entries - deleting one must not affect the other, even though
    /// their names differ only by case.</summary>
    [Test]
    public async Task Commit_DeletingOneEntry_DoesNotDropAnUntouchedCaseDifferingSibling()
    {
        var format = TarArchiveFormat.Instance;

        await using (var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions()))
        {
            await WriteTextEntryAsync(writer, "README.txt", "uppercase content");
            await WriteTextEntryAsync(writer, "readme.txt", "lowercase content");
            await writer.CommitAsync();
        }

        using (var reader = format.OpenRead(_archivePath))
        {
            var directory = await reader.ReadDirectoryAsync();
            var toRemove = directory.Entries.Single(e => e.FullName.TrimEnd('/') == "README.txt");

            await using var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions());
            writer.TryDeleteEntry(toRemove);
            await writer.CommitAsync();
        }

        var names = await ReadEntryNamesAsync();
        Assert.That(names, Does.Not.Contain("README.txt"));
        Assert.That(names, Does.Contain("readme.txt"),
            "Deleting README.txt must not also drop the untouched, case-differing readme.txt");
    }

    [Test]
    public void OpenWrite_NewArchive_ReportsRewriteThroughMode()
    {
        var format = TarArchiveFormat.Instance;
        using var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions());
        Assert.That(writer.Mode, Is.EqualTo(ArchiveWriteMode.RewriteThrough));
    }

    /// <summary>Same "add to existing archive" property as the TAR test above, but through
    /// <see cref="TarBz2ArchiveFormat"/>/<see cref="SharpCompressTarWriter"/> specifically - proves
    /// the new SharpCompress-backed writer actually integrates correctly with
    /// <see cref="RewritingArchiveWriter"/>'s stage-then-rewrite mechanics, not just that the
    /// mechanics themselves are correct (already covered above via TAR).</summary>
    [Test]
    public async Task Commit_AddingToExistingTarBz2Archive_PreservesPriorEntries()
    {
        var bz2Path = Path.ChangeExtension(_archivePath, ".tar.bz2");
        try
        {
            var format = TarBz2ArchiveFormat.Instance;

            await using (var writer = format.OpenWrite(bz2Path, new ArchiveWriteOptions()))
            {
                await WriteTextEntryAsync(writer, "first.txt", "first content");
                await writer.CommitAsync();
            }

            await using (var writer = format.OpenWrite(bz2Path, new ArchiveWriteOptions()))
            {
                await WriteTextEntryAsync(writer, "second.txt", "second content");
                await writer.CommitAsync();
            }

            using var reader = format.OpenRead(bz2Path);
            var directory = await reader.ReadDirectoryAsync();
            var names = directory.Entries.Select(e => e.FullName.TrimEnd('/')).ToList();
            Assert.That(names, Does.Contain("first.txt"));
            Assert.That(names, Does.Contain("second.txt"));
        }
        finally
        {
            if (File.Exists(bz2Path)) File.Delete(bz2Path);
        }
    }
}
