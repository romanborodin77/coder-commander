namespace CoderCommander.FileSystem;

/// <summary>
/// Wraps an <see cref="IFileSystem"/> to refuse every write - delegates every read member to
/// <paramref name="inner"/> untouched, throws <see cref="NotSupportedException"/> from every write
/// member. Used to open a materialized archive (one whose real container lives on a remote
/// connection, downloaded to a local temp copy purely to browse it) in the panel without offering
/// mutations the panel's own navigation has no trigger to write back on - see
/// <see cref="Views.MainForm.EnterArchiveAsync"/>.
///
/// <para>Preferred over threading a read-only flag through <c>Archives.ArchiveFileSystem</c> itself:
/// that type's writability is a property of the underlying <em>format</em> (7z/RAR/TAR.XZ are
/// always read-only, ZIP/TAR are not), not of how the container happened to be reached. Wrapping
/// keeps those two concerns - "can this format write at all" vs. "should this particular session be
/// allowed to" - independent, and it means <see cref="Capabilities"/> here is the one place both
/// answers get combined: an already-read-only format wrapped by this stays read-only (there is
/// nothing to strip), and a normally-writable one gets its write flags removed regardless of what
/// it declares.</para>
/// </summary>
public sealed class ReadOnlyFileSystem : IFileSystem
{
    private const string ReadOnlyMessage = "This archive was opened read-only (its container is not on this machine).";

    private readonly IFileSystem _inner;

    public ReadOnlyFileSystem(IFileSystem inner) => _inner = inner;

    public string Name => _inner.Name;

    /// <inheritdoc/>
    public FileSystemCapabilities Capabilities =>
        _inner.Capabilities & ~(FileSystemCapabilities.Writable | FileSystemCapabilities.Deletable);

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

    public Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
        throw new NotSupportedException(ReadOnlyMessage);

    public Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
        throw new NotSupportedException(ReadOnlyMessage);

    public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default) =>
        throw new NotSupportedException(ReadOnlyMessage);

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default) =>
        throw new NotSupportedException(ReadOnlyMessage);

    public Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default) =>
        throw new NotSupportedException(ReadOnlyMessage);

    /// <summary>Same no-op every archive/remote provider already gives this - see e.g.
    /// <c>WebDavFileSystem.SetAttributesAsync</c>'s own doc comment for why a no-op beats throwing
    /// here (callers set attributes opportunistically, and throwing would turn what looks like a
    /// harmless best-effort step into a hard failure).</summary>
    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) =>
        Task.CompletedTask;
}
