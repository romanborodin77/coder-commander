using CoderCommander.Archives;
using CoderCommander.Services;

namespace CoderCommander.FileSystem.Materialization;

/// <summary>
/// A real local path for a file that may live on any <see cref="IFileSystem"/>, with optional
/// write-back. This is the same "materialize, act on the real path, write back" shape
/// <c>Viewers.Formats.MaterializingViewerLoader</c> already established for the F3 viewer, widened
/// from "read into memory, capped at a few hundred MB" to "stream through a real temp file, capped
/// in the gigabytes" - what an archive container needs (a real, seekable, System.IO-touchable path,
/// since <c>IArchiveFormat.OpenRead</c>/<c>OpenWrite</c> bottom out in <c>ZipFile</c>/
/// <c>System.Formats.Tar</c>/SharpCompress, none of which take a stream-shaped path) instead of what
/// a viewer payload needs (bytes in memory).
///
/// <para><b>Passthrough is the common case, not an edge case.</b> When <see cref="IFileSystem.Capabilities"/>
/// declares <see cref="FileSystemCapabilities.NativePaths"/>, nothing is copied at all -
/// <see cref="LocalPath"/> is the origin path itself, <see cref="WriteBackAsync"/> is a no-op (the
/// file was always the real one), and <see cref="Dispose"/> deletes nothing. Every caller of this
/// class runs the exact same code path for local and non-local origins; only this class ever
/// branches on the capability.</para>
///
/// <para><b>Write-back is sidecar-then-rename, never in place.</b> <see cref="IFileSystem.CopyFromStreamAsync"/>
/// is the only write primitive every provider has, and on a remote provider it's a single PUT/STOR -
/// there is no provider-side atomic "replace this file's bytes". Uploading directly over
/// <see cref="OriginPath"/> would leave a truncated file there if the upload failed or was
/// interrupted partway. Instead: upload to a sidecar name next to the origin, verify its size
/// matches the local file's, then <see cref="IFileSystem.MoveAsync"/> the sidecar over the origin
/// (overwrite). <see cref="OriginPath"/> is never touched until the replacement copy is confirmed
/// whole on the server.</para>
///
/// <para><b>The one gap this can't close</b>: <c>Remote.Sftp.SftpFileSystem.MoveAsync</c> deletes the
/// destination before renaming when <c>overwrite</c> is true (SFTP's rename has no atomic-replace
/// primitive), so there is a brief window on SFTP where neither name exists. Nothing in
/// <see cref="IFileSystem"/> can close that - the mitigation is ordering only: the sidecar is fully
/// uploaded and size-verified before that window ever opens, so the recoverable artifact (the
/// sidecar) is always present on the server if the rename step itself fails.</para>
/// </summary>
public sealed class MaterializedFile : IDisposable
{
    public IFileSystem Origin { get; }

    /// <summary>The file's identity - what every user-facing message, conflict dialog, and
    /// <c>ArchivePath</c>/entry-name construction must use. Never <see cref="LocalPath"/>, which is
    /// only ever meaningful to <c>System.IO</c>.</summary>
    public string OriginPath { get; }

    /// <summary>A path <c>System.IO</c> may touch directly. Equal to <see cref="OriginPath"/> when
    /// <see cref="IsPassthrough"/>.</summary>
    public string LocalPath { get; }

    /// <summary>True when nothing was copied - <see cref="Origin"/> declares
    /// <see cref="FileSystemCapabilities.NativePaths"/>, so <see cref="LocalPath"/> already is the
    /// real file.</summary>
    public bool IsPassthrough { get; }

    /// <summary>True when the origin did not exist at <see cref="AcquireAsync"/> time
    /// (<see cref="MaterializeOptions.AllowMissing"/>) - the conflict check in
    /// <see cref="WriteBackAsync"/> is skipped, since there is no prior state to have diverged from.</summary>
    public bool IsNew { get; }

    public bool IsDirty { get; private set; }

    private readonly string? _ownedFolder;
    private long _originSize;
    private DateTime _originStampUtc;
    private bool _disposed;

    private MaterializedFile(IFileSystem origin, string originPath, string localPath, bool isPassthrough,
        bool isNew, string? ownedFolder, long originSize, DateTime originStampUtc)
    {
        Origin = origin;
        OriginPath = originPath;
        LocalPath = localPath;
        IsPassthrough = isPassthrough;
        IsNew = isNew;
        _ownedFolder = ownedFolder;
        _originSize = originSize;
        _originStampUtc = originStampUtc;
    }

    /// <summary>
    /// Acquires a real local path for <paramref name="path"/> on <paramref name="fs"/>. Streams
    /// through a real temp file under <paramref name="session"/> (never into memory - this is what
    /// distinguishes it from <c>Viewers.ViewerSource.ReadAllBytesAsync</c>) for a non-native origin;
    /// returns the origin path itself, untouched, for a native one.
    /// </summary>
    /// <exception cref="FileNotFoundException">The origin doesn't exist and
    /// <see cref="MaterializeOptions.AllowMissing"/> wasn't set.</exception>
    /// <exception cref="MaterializationTooLargeException">The origin's reported or actual size
    /// exceeds <see cref="MaterializeOptions.MaxBytes"/>.</exception>
    public static async Task<MaterializedFile> AcquireAsync(
        IFileSystem fs, string path, TempSessionRoot session, MaterializeOptions options, CancellationToken ct)
    {
        if (fs.Capabilities.HasFlag(FileSystemCapabilities.NativePaths))
            return new MaterializedFile(fs, path, path, isPassthrough: true, isNew: false, ownedFolder: null, 0, default);

        var info = await fs.GetFileInfoAsync(path, ct).ConfigureAwait(false);
        if (info == null)
        {
            if (!options.AllowMissing)
                throw new FileNotFoundException($"\"{path}\" was not found.", path);

            var newFolder = session.AllocateFileFolder();
            var newLocal = Path.Combine(newFolder, VfsPath.GetName(path));
            return new MaterializedFile(fs, path, newLocal, isPassthrough: false, isNew: true, ownedFolder: newFolder, 0, default);
        }

        // Checked up front from the provider's own reported size AND enforced again while
        // streaming below - some providers (FTP in particular) can report an unreliable or absent
        // size, so the up-front check alone isn't sufficient, the same "check the claim, then check
        // the reality" shape Archives.Office.OfficeLimits already uses for zip-bomb defense.
        if (info.Size > options.MaxBytes)
            throw new MaterializationTooLargeException(path, info.Size, options.MaxBytes);

        var folder = session.AllocateFileFolder();
        var local = Path.Combine(folder, VfsPath.GetName(path));

        var copied = 0L;
        var src = await fs.OpenReadAsync(path, ct).ConfigureAwait(false);
        await using (src.ConfigureAwait(false))
        {
            var dst = new FileStream(local, FileMode.Create, FileAccess.Write, FileShare.None);
            await using (dst.ConfigureAwait(false))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    copied += read;
                    if (copied > options.MaxBytes)
                        throw new MaterializationTooLargeException(path, copied, options.MaxBytes);
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }
        }

        return new MaterializedFile(fs, path, local, isPassthrough: false, isNew: false, ownedFolder: folder,
            info.Size, info.LastWriteTimeUtc);
    }

    /// <summary>Marks the local copy as modified, so <see cref="WriteBackAsync"/> actually uploads
    /// it instead of no-op'ing. Idempotent - call it after every mutation, not just once.</summary>
    public void MarkDirty() => IsDirty = true;

    /// <summary>
    /// Uploads <see cref="LocalPath"/> back over <see cref="OriginPath"/>. No-op when
    /// <see cref="IsPassthrough"/> (the file was always the real one) or when <see cref="IsDirty"/>
    /// is false (nothing changed, nothing to upload). Must be called AFTER anything holding
    /// <see cref="LocalPath"/> open has released it (e.g. after an <c>IArchiveWriter</c>'s
    /// <c>await using</c> block has closed) - uploading while a writer still has the file open
    /// would upload stale, pre-commit bytes.
    /// </summary>
    /// <exception cref="MaterializationConflictException">The origin changed on the server since
    /// <see cref="AcquireAsync"/>.</exception>
    public async Task WriteBackAsync(CancellationToken ct)
    {
        if (IsPassthrough || !IsDirty) return;

        if (!IsNew)
        {
            var now = await Origin.GetFileInfoAsync(OriginPath, ct).ConfigureAwait(false);
            if (now is null || now.Size != _originSize || now.LastWriteTimeUtc != _originStampUtc)
                throw new MaterializationConflictException(OriginPath);
        }

        var sidecar = OriginPath + ".cc-wb-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var src = ArchiveFileRetry.OpenReadWithRetry(LocalPath))
                await Origin.CopyFromStreamAsync(sidecar, src, ct).ConfigureAwait(false);

            var uploaded = await Origin.GetFileInfoAsync(sidecar, ct).ConfigureAwait(false);
            var localLength = new FileInfo(LocalPath).Length;
            if (uploaded is null || uploaded.Size != localLength)
                throw new IOException($"Write-back of \"{OriginPath}\" did not complete: uploaded size did not match the local copy.");

            // Deliberately CancellationToken.None: cancelling between "upload verified whole" and
            // "rename into place" would leave a fully-good sidecar sitting next to an un-updated
            // origin, which is strictly worse than spending one more round trip to finish the swap.
            await Origin.MoveAsync(sidecar, OriginPath, overwrite: true, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            try { await Origin.DeleteAsync(sidecar, recursive: false, CancellationToken.None).ConfigureAwait(false); }
            catch { /* best-effort cleanup of the sidecar; the original exception is what matters */ }
            throw;
        }

        var fresh = await Origin.GetFileInfoAsync(OriginPath, CancellationToken.None).ConfigureAwait(false);
        _originSize = fresh?.Size ?? _originSize;
        _originStampUtc = fresh?.LastWriteTimeUtc ?? _originStampUtc;
        IsDirty = false;
    }

    /// <summary>Deletes this instance's own temp folder, best-effort. Never uploads - write-back is
    /// explicit and awaited via <see cref="WriteBackAsync"/>, never implied by disposal, so a
    /// caller that forgets to call it gets a loud "nothing was saved" rather than a silent upload
    /// racing the caller's own cleanup.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownedFolder == null) return;

        try
        {
            if (Directory.Exists(_ownedFolder))
                Directory.Delete(_ownedFolder, recursive: true);
        }
        catch
        {
            // Best-effort - matches TempSessionRoot's own reasoning; a locked file (still open by
            // something) is left for the next startup's orphan sweep to retry.
        }
    }
}
