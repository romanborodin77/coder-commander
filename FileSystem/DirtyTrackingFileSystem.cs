namespace CoderCommander.FileSystem;

/// <summary>
/// Wraps an <see cref="IFileSystem"/> to mark a <see cref="Materialization.MaterializedFile"/>
/// lease dirty after any successful mutating call - reads delegate to <paramref name="inner"/>
/// untouched. Used to open a materialized archive (one whose real container lives on a remote
/// connection, downloaded to a local temp copy to browse it) fully writable in the panel: mutations
/// land on the temp copy immediately, and <paramref name="markDirty"/> (in practice
/// <c>MaterializedFile.MarkDirty</c>) is what lets <c>PanelViewModel.ReleaseArchiveLease</c> know,
/// when the panel later leaves the archive, whether there is anything worth offering to write back -
/// see <see cref="Views.MainForm.EnterArchiveAsync"/>.
///
/// <para>Superseded the earlier <c>ReadOnlyFileSystem</c> (which refused every write outright): a
/// materialized archive was made read-only originally because panel navigation had no trigger to
/// write changes back on - <c>PanelViewModel</c> now provides exactly that trigger
/// (<c>ReleaseArchiveLease</c>, invoked on exiting/replacing the archive), so blocking writes
/// entirely stopped being the right default.</para>
/// </summary>
public sealed class DirtyTrackingFileSystem : IFileSystem, IBatchDeletableFileSystem
{
    private readonly IFileSystem _inner;
    private readonly Action _markDirty;

    public DirtyTrackingFileSystem(IFileSystem inner, Action markDirty)
    {
        _inner = inner;
        _markDirty = markDirty;
    }

    public string Name => _inner.Name;
    public FileSystemCapabilities Capabilities => _inner.Capabilities;

    public Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default) =>
        _inner.EnumerateAsync(path, includeHidden, ct);

    public Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default) =>
        _inner.EnumerateDeepAsync(path, includeHidden, ct);

    public Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default) =>
        _inner.GetFileInfoAsync(path, ct);

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
        _inner.ExistsAsync(path, ct);

    public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default) =>
        _inner.GetDriveSpaceAsync(path, ct);

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default) =>
        _inner.OpenReadAsync(path, ct);

    public string GetRootPath(string path) => _inner.GetRootPath(path);

    /// <summary>No dirty mark - every provider already treats this as a best-effort cosmetic
    /// step (see e.g. <c>WebDavFileSystem.SetAttributesAsync</c>'s own doc comment), not content
    /// worth offering a write-back for.</summary>
    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) =>
        _inner.SetAttributesAsync(path, attributes, ct);

    public async Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default)
    {
        await _inner.CopyFileAsync(source, destination, overwrite, ct).ConfigureAwait(false);
        _markDirty();
    }

    public async Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default)
    {
        await _inner.MoveAsync(source, destination, overwrite, ct).ConfigureAwait(false);
        _markDirty();
    }

    public async Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
    {
        await _inner.DeleteAsync(path, recursive, ct).ConfigureAwait(false);
        _markDirty();
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        await _inner.CreateDirectoryAsync(path, ct).ConfigureAwait(false);
        _markDirty();
    }

    public async Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default)
    {
        await _inner.CopyFromStreamAsync(destinationPath, source, ct).ConfigureAwait(false);
        _markDirty();
    }

    /// <summary>Delegates to <paramref name="inner"/>'s own batch delete when it has one (ZIP);
    /// otherwise falls back to a per-entry loop - either way, wrapped unconditionally so
    /// <c>Operations.DeleteOperation</c>'s <c>_fs is IBatchDeletableFileSystem</c> check sees this
    /// decorator the same way it would see the unwrapped inner filesystem.</summary>
    public async Task DeleteBatchAsync(IReadOnlyList<string> paths, bool recursive, CancellationToken ct = default)
    {
        if (_inner is IBatchDeletableFileSystem batch)
            await batch.DeleteBatchAsync(paths, recursive, ct).ConfigureAwait(false);
        else
            foreach (var path in paths)
                await _inner.DeleteAsync(path, recursive, ct).ConfigureAwait(false);
        _markDirty();
    }
}
