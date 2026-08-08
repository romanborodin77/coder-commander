using CoderCommander.FileSystem;
using CoderCommander.Operations;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the swallowed-enumeration-failure bug fixed in
/// <see cref="CopyOperation.FlattenAsync"/>: it used to plan a root directory's own entry before
/// attempting to enumerate its descendants, and a failed enumeration just logged a warning and
/// moved on - leaving that root in the plan with none of its actual content. CopyOperation would
/// then create an empty destination folder, copy zero files for it, and still report
/// OperationState.Completed. Combined with MoveOperation's now-fixed WrittenPaths-based deletion
/// (see MoveOperationSkipTests), a Move of such a root used to also delete the (never actually
/// copied) source directory.
/// </summary>
public class CopyOperationEnumerationFailureTests
{
    /// <summary>Wraps a real LocalFileSystem but makes EnumerateDeepAsync fail for one specific
    /// path, simulating a transient I/O error (disconnected share, reparse loop, etc.) without
    /// needing to actually break the OS-level filesystem.</summary>
    private sealed class ThrowingEnumerateFileSystem : IFileSystem
    {
        private readonly LocalFileSystem _inner = new();
        private readonly string _failPath;

        public ThrowingEnumerateFileSystem(string failPath) => _failPath = failPath;

        public string Name => _inner.Name;
        public Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default) =>
            _inner.EnumerateAsync(path, includeHidden, ct);
        public Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default) =>
            string.Equals(path, _failPath, StringComparison.OrdinalIgnoreCase)
                ? throw new IOException("Simulated enumeration failure")
                : _inner.EnumerateDeepAsync(path, includeHidden, ct);
        public Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default) => _inner.GetFileInfoAsync(path, ct);
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => _inner.ExistsAsync(path, ct);
        public Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
            _inner.CopyFileAsync(source, destination, overwrite, ct);
        public Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
            _inner.MoveAsync(source, destination, overwrite, ct);
        public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default) => _inner.DeleteAsync(path, recursive, ct);
        public Task CreateDirectoryAsync(string path, CancellationToken ct = default) => _inner.CreateDirectoryAsync(path, ct);
        public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) =>
            _inner.SetAttributesAsync(path, attributes, ct);
        public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default) => _inner.GetDriveSpaceAsync(path, ct);
        public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default) => _inner.OpenReadAsync(path, ct);
        public Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default) =>
            _inner.CopyFromStreamAsync(destinationPath, source, ct);
        public string GetRootPath(string path) => _inner.GetRootPath(path);
    }

    private string _sourceRoot = "";
    private string _destRoot = "";

    [SetUp]
    public void CreateFixtures()
    {
        _sourceRoot = Directory.CreateTempSubdirectory("cc_enumfail_src_").FullName;
        _destRoot = Directory.CreateTempSubdirectory("cc_enumfail_dest_").FullName;
        File.WriteAllText(Path.Combine(_sourceRoot, "should-not-be-lost.txt"), "content");
    }

    [TearDown]
    public void DeleteFixtures()
    {
        if (Directory.Exists(_sourceRoot)) Directory.Delete(_sourceRoot, recursive: true);
        if (Directory.Exists(_destRoot)) Directory.Delete(_destRoot, recursive: true);
    }

    [Test]
    public async Task FlattenAsync_EnumerationFails_ExcludesRootFromPlanAndReportsFailure()
    {
        var sourceFs = new ThrowingEnumerateFileSystem(_sourceRoot);
        var roots = new[] { new FileEntry(_sourceRoot, isDirectory: true) };
        var failures = new List<string>();

        var plan = await CopyOperation.FlattenAsync(sourceFs, roots, Path.GetDirectoryName(_sourceRoot)!, _destRoot, CancellationToken.None, failures);

        Assert.That(plan, Is.Empty, "A root whose subtree can't be enumerated must not leave a silently-empty entry in the plan");
        Assert.That(failures, Does.Contain(_sourceRoot));
    }

    [Test]
    public async Task CopyOperation_EnumerationFails_ReportsFailedNotCompleted()
    {
        var sourceFs = new ThrowingEnumerateFileSystem(_sourceRoot);
        var destFs = new LocalFileSystem();
        var files = new[] { new FileEntry(_sourceRoot, isDirectory: true) };

        using var copy = new CopyOperation(sourceFs, destFs, files, Path.GetDirectoryName(_sourceRoot)!, _destRoot);
        // FileOperation.ExecuteAsync catches every exception internally and records it via
        // State/LastError rather than rethrowing - it does not propagate to the caller.
        await copy.ExecuteAsync();

        Assert.That(copy.State, Is.EqualTo(OperationState.Failed),
            "Must not report Completed when a whole subtree's content silently failed to enumerate");
        Assert.That(copy.LastError, Is.InstanceOf<IOException>());
        Assert.That(copy.WrittenPaths, Is.Empty, "Nothing from the unreadable subtree should be marked as written");
    }
}
