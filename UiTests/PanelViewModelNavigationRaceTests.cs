using CoderCommander.FileSystem;
using CoderCommander.ViewModels;

namespace CoderCommander.UiTests;

/// <summary>
/// Direct (no UI) tests for the navigation race fixed in <see cref="PanelViewModel"/>:
/// NavigateAsync used to commit CurrentPath based on whichever concurrent call's
/// <c>ExistsAsync</c> await happened to resolve LAST, not whichever call actually STARTED
/// last - so a fast click into folder B racing a slow (e.g. network-path) click into folder A
/// could leave the panel showing A even though B was clicked more recently. These use a fake
/// <see cref="IFileSystem"/> with a controllable <c>ExistsAsync</c> delay to force that
/// out-of-order resolution deterministically instead of hoping real timing cooperates.
/// </summary>
public class PanelViewModelNavigationRaceTests
{
    /// <summary>Minimal IFileSystem whose ExistsAsync for a given path only completes once the
    /// test explicitly releases it - everything else is either instant or unused by these tests.</summary>
    private sealed class GatedFileSystem : IFileSystem
    {
        private readonly Dictionary<string, TaskCompletionSource<bool>> _gates = new(StringComparer.OrdinalIgnoreCase);

        public string Name => "Gated";

        /// <summary>Registers a path whose ExistsAsync will not complete until <see cref="Release"/> is called for it.</summary>
        public void Gate(string path) => _gates[Normalize(path)] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Lets a previously-<see cref="Gate"/>d path's ExistsAsync call return.</summary>
        public void Release(string path) => _gates[Normalize(path)].SetResult(true);

        private static string Normalize(string path) => path.TrimEnd('\\', '/');

        public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        {
            var key = Normalize(path);
            return _gates.TryGetValue(key, out var tcs) ? tcs.Task : Task.FromResult(true);
        }

        public Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FileEntry>>(Array.Empty<FileEntry>());

        public Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FileEntry>>(Array.Empty<FileEntry>());

        public Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default) => Task.FromResult<FileEntry?>(null);
        public Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(string path, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default) => Task.FromResult((0L, 0L));
        public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default) => throw new NotSupportedException();
        public string GetRootPath(string path) => "C:\\";
    }

    [Test]
    public async Task NavigateAsync_OlderSlowNavigationResolvingLast_DoesNotClobberNewerFastOne()
    {
        var fs = new GatedFileSystem();
        const string pathA = @"C:\A\";
        const string pathB = @"C:\B\";
        fs.Gate(pathA); // A's ExistsAsync will hang until released - simulates a slow/network path
        // B's ExistsAsync is left ungated (resolves immediately) - simulates an ordinary fast local click.

        using var vm = new PanelViewModel(fs);

        var taskA = vm.NavigateAsync(pathA); // starts first, but its ExistsAsync won't resolve yet
        var taskB = vm.NavigateAsync(pathB); // starts second, resolves immediately, should win
        await taskB;

        Assert.That(vm.CurrentPath, Is.EqualTo(pathB),
            "The more recently started navigation (B) should win even though A hasn't resolved yet");

        fs.Release(pathA); // now let A's stale ExistsAsync resolve
        await taskA;

        Assert.That(vm.CurrentPath, Is.EqualTo(pathB),
            "A's navigation resolving after B must not clobber CurrentPath - it started before B and must lose");
    }
}
