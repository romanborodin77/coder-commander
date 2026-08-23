using CoderCommander.Services;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace CoderCommander.FileSystem.Remote.Sftp;

/// <summary>
/// <see cref="IFileSystem"/> over SFTP, the file-transfer subsystem of SSH (draft-ietf-secsh-filexfer).
///
/// <para>App-side paths are <c>sftp://host/dir/name</c>; mapping those onto the server's own
/// absolute paths is this class's job and nobody else's.</para>
///
/// <para><b><see cref="FileSystemCapabilities.NativePaths"/> is deliberately never declared</b>:
/// everything gated on it - secure wipe, folder-size walking, packing, the Recycle Bin, a
/// FileSystemWatcher, git - would either be meaningless here or would reach around this class to a
/// local path that does not exist. <see cref="Capabilities"/> DOES declare
/// <see cref="FileSystemCapabilities.Writable"/>/<see cref="FileSystemCapabilities.Deletable"/>
/// unconditionally - SFTP genuinely supports writing and removing.</para>
///
/// <para><b>One client, no pool</b> - unlike FTP. The difference is in the protocols, not in the
/// implementations: an FTP control channel carries one conversation and blocks for the duration of a
/// transfer, whereas SFTP multiplexes numbered requests over a single SSH channel and is built to
/// have several outstanding at once. Opening a second client would mean a second SSH handshake -
/// the expensive part - to buy concurrency the protocol already provides. (SSH.NET does warn that
/// <c>ConnectionInfo</c> must not be shared between clients, which is why the one here is built for
/// its client and never handed out.)</para>
///
/// <para><b>Every exception SSH.NET raises is translated.</b> Its own types mean nothing to the rest
/// of the app, and a <c>SftpPathNotFoundException</c> arriving at an error dialog is worse than
/// useless to whoever reads it.</para>
/// </summary>
public sealed class SftpFileSystem : IFileSystem, IDisposable
{
    private readonly SftpClient _client;
    private readonly string _authority;
    private readonly string _basePath;

    public string Name => "SFTP";

    /// <inheritdoc/>
    public FileSystemCapabilities Capabilities => FileSystemCapabilities.Writable | FileSystemCapabilities.Deletable;

    internal SftpFileSystem(SftpClient client, string authority, string basePath)
    {
        _client = client;
        _authority = authority;
        _basePath = basePath.TrimEnd('/');
    }

    public string GetRootPath(string path) => RemotePath.Make("sftp", _authority);

    /// <summary>No shell path exists - an SFTP entry has no local Windows path at all.</summary>
    public string? GetShellPath(string path) => null;

    // ── Path mapping ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// App path to the server's absolute path.
    ///
    /// <para>Each segment is validated, not just the ones that came from a listing: a path can also
    /// arrive from the address bar. A name that escapes its directory is refused here rather than
    /// being handed to the server to interpret.</para>
    /// </summary>
    private string ToServerPath(string appPath)
    {
        var inner = RemotePath.PathOf(appPath);

        foreach (var segment in inner.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!RemotePath.IsSafeEntryName(segment))
                throw new IOException($"SFTP: refusing to use an unsafe path segment in \"{inner}\"");
        }

        var root = _basePath.Length == 0 ? "" : _basePath;
        return inner.Length == 0 ? (root.Length == 0 ? "/" : root) : $"{root}/{inner}";
    }

    private string ToAppPath(string relativeInner) => RemotePath.Make("sftp", _authority, relativeInner);

    // ── Reading ─────────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default)
    {
        var serverPath = ToServerPath(path);
        var inner = RemotePath.PathOf(path);
        var entries = new List<FileEntry>();

        await RunAsync(async token =>
        {
            await foreach (var file in _client.ListDirectoryAsync(serverPath, token).ConfigureAwait(false))
            {
                if (entries.Count >= RemoteLimits.MaxEntriesPerDirectory)
                {
                    LogService.Warning($"SFTP: listing truncated at {RemoteLimits.MaxEntriesPerDirectory} entries");
                    break;
                }

                if (file.Name is "." or "..") continue;

                // The server names the entry, and that name goes on to build a local path during a
                // download - so it is checked exactly like an archive entry name.
                if (!RemotePath.IsSafeEntryName(file.Name))
                {
                    LogService.Warning("SFTP: rejected a listing entry with an unsafe name");
                    continue;
                }

                entries.Add(ToEntry(file, inner));
            }
        }, "list", inner, ct).ConfigureAwait(false);

        return entries;
    }

    /// <summary>
    /// A symbolic link is reported as whatever it points at, because that is what
    /// <see cref="ISftpFile.IsDirectory"/> already answers - SFTP's <c>SSH_FXP_LSTAT</c> results are
    /// resolved by SSH.NET. A link pointing outside the tree is therefore enterable, which is the
    /// same thing an SSH session itself would allow; the guard that matters is on <i>names</i>, which
    /// is what could escape a local directory during a download.
    /// </summary>
    private FileEntry ToEntry(ISftpFile file, string parentInner) => new(
        fullPath: ToAppPath(parentInner.Length == 0 ? file.Name : $"{parentInner}/{file.Name}"),
        isDirectory: file.IsDirectory,
        exists: true,
        size: file.IsDirectory ? 0 : file.Length,
        attributes: file.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
        createdTimeUtc: default,
        lastWriteTimeUtc: file.LastWriteTimeUtc,
        lastAccessTimeUtc: file.LastAccessTimeUtc);

    public async Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default)
    {
        var result = new List<FileEntry>();
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue(path);
        visited.Add(path);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (result.Count >= RemoteLimits.MaxEntriesPerDirectory) break;

            var current = queue.Dequeue();
            IReadOnlyList<FileEntry> children;
            try
            {
                children = await EnumerateAsync(current, includeHidden, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One unreadable subdirectory must not abandon the whole walk - the same rule the
                // local provider's enumeration failures follow.
                LogService.Warning($"SFTP: cannot list {RemotePath.PathOf(current)}: {ex.GetType().Name}");
                continue;
            }

            foreach (var child in children)
            {
                result.Add(child);
                if (child.IsDirectory && visited.Add(child.FullPath))
                    queue.Enqueue(child.FullPath);
            }
        }

        return result;
    }

    /// <summary>One <c>SSH_FXP_STAT</c>, not a listing of the parent. SFTP can answer about a single
    /// path, which FTP cannot - so this costs one round trip rather than one per sibling.</summary>
    public async Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default)
    {
        var serverPath = ToServerPath(path);
        var parentInner = RemotePath.PathOf(RemotePath.GetParent(path));

        try
        {
            return await RunAsync(async token =>
            {
                var file = await _client.GetAsync(serverPath, token).ConfigureAwait(false);
                return ToEntry(file, parentInner);
            }, "stat", RemotePath.PathOf(path), ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var serverPath = ToServerPath(path);
            return await RunAsync(token => _client.ExistsAsync(serverPath, token), "exists", RemotePath.PathOf(path), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Debug($"SFTP: existence check failed for {RemotePath.PathOf(path)}: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary><c>SftpFileStream</c> reads lazily over the same SSH channel, so a large file is
    /// never buffered whole - the same contract the local and WebDAV providers give.</summary>
    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        var serverPath = ToServerPath(path);
        return RunAsync<Stream>(async token =>
            await _client.OpenAsync(serverPath, FileMode.Open, FileAccess.Read, token).ConfigureAwait(false),
            "open", RemotePath.PathOf(path), ct);
    }

    // ── Writing ─────────────────────────────────────────────────────────────────────────────

    public Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default)
    {
        var serverPath = ToServerPath(destinationPath);
        return RunAsync(
            token => _client.UploadFileAsync(source, serverPath, canOverride: true, uploadProgress: null, token),
            "upload", RemotePath.PathOf(destinationPath), ct);
    }

    /// <summary>
    /// Creates a directory and any missing ancestors.
    ///
    /// <para>Directories already created are remembered, for the same reason as in the other remote
    /// providers: a copy calls this once per file, always for the same destination, and each call is
    /// otherwise a round trip per level per file.</para>
    /// </summary>
    public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        var inner = RemotePath.PathOf(path);
        if (inner.Length == 0) return;

        lock (_knownDirectories)
        {
            if (_knownDirectories.Contains(inner)) return;
        }

        var built = "";
        var created = new List<string>();
        foreach (var segment in inner.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            built = built.Length == 0 ? segment : $"{built}/{segment}";
            created.Add(built);

            var serverPath = ToServerPath(ToAppPath(built));
            if (await ExistsAsync(ToAppPath(built), ct).ConfigureAwait(false)) continue;

            await RunAsync(token => _client.CreateDirectoryAsync(serverPath, token), "mkdir", built, ct)
                .ConfigureAwait(false);
        }

        lock (_knownDirectories)
        {
            foreach (var directory in created) _knownDirectories.Add(directory);
        }
    }

    /// <summary>Deletes a file or a directory tree. SFTP's <c>SSH_FXP_RMDIR</c> removes an empty
    /// directory only, so the walk is done here, depth first.</summary>
    public async Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
    {
        var serverPath = ToServerPath(path);

        // Try file delete first — this removes symlinks without following them. SSH.NET's GetAsync
        // (used by GetFileInfoAsync) follows symlinks via SSH_FXP_STAT, so a symlink to a directory
        // would be reported as IsDirectory=true, and the directory branch below would recurse into
        // the TARGET directory, deleting its contents — data loss. DeleteFileAsync uses
        // SSH_FXP_REMOVE which operates on the link itself, not the target.
        try
        {
            await RunAsync(token => _client.DeleteFileAsync(serverPath, token), "delete", RemotePath.GetName(path), ct)
                .ConfigureAwait(false);
            return;
        }
        catch (Renci.SshNet.Common.SftpPermissionDeniedException) { throw; }
        catch (Renci.SshNet.Common.SftpPathNotFoundException) { throw; }
        catch (Renci.SshNet.Common.SshException) { /* not a file (or a directory) — fall through to directory path */ }

        if (recursive)
        {
            foreach (var child in await EnumerateAsync(path, includeHidden: true, ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                await DeleteAsync(child.FullPath, recursive: true, ct).ConfigureAwait(false);
            }
        }

        await RunAsync(token => _client.DeleteDirectoryAsync(serverPath, token), "rmdir", RemotePath.GetName(path), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Rename or move. SFTP's rename is one atomic operation, unlike FTP's two-command pair.
    ///
    /// <para><paramref name="overwrite"/> is enforced here rather than by the server: the base
    /// protocol's rename fails if the destination exists and offers no flag to say otherwise, so
    /// overwriting means removing the destination first. That is not atomic, and it is the honest
    /// implementation of a guarantee the protocol does not make.</para>
    /// </summary>
    public async Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default)
    {
        if (await ExistsAsync(destination, ct).ConfigureAwait(false))
        {
            if (!overwrite)
                throw new IOException($"SFTP: \"{RemotePath.GetName(destination)}\" already exists");

            await DeleteAsync(destination, recursive: false, ct).ConfigureAwait(false);
        }

        await RunAsync(token => _client.RenameFileAsync(ToServerPath(source), ToServerPath(destination), token),
            "rename", RemotePath.GetName(source), ct).ConfigureAwait(false);
    }

    /// <summary>SFTP has no server-side copy, so the bytes travel down and back up through this
    /// machine. Refusing instead would break an ordinary copy within one connection.</summary>
    public async Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default)
    {
        if (!overwrite && await ExistsAsync(destination, ct).ConfigureAwait(false))
            throw new IOException($"SFTP: \"{RemotePath.GetName(destination)}\" already exists");

        await using var input = await OpenReadAsync(source, ct).ConfigureAwait(false);
        await CopyFromStreamAsync(destination, input, ct).ConfigureAwait(false);
    }

    /// <summary>A deliberate no-op. SFTP carries POSIX permission bits, which are not the Windows
    /// attributes this app deals in; mapping one onto the other would invent a meaning neither side
    /// agreed to. Throwing is worse than doing nothing: callers set attributes opportunistically
    /// after a copy, and a failure here would turn a completed transfer into a failed one.</summary>
    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <summary>
    /// Free space, where the server implements the <c>statvfs@openssh.com</c> extension.
    ///
    /// <para><c>(0, 0)</c> otherwise, which is this codebase's established "couldn't determine" - the
    /// decompression-bomb guard already treats it as "skip the check" rather than as "no space".</para>
    /// </summary>
    public async Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var status = await RunAsync(
                token => _client.GetStatusAsync(ToServerPath(path), token),
                "statvfs", RemotePath.PathOf(path), ct).ConfigureAwait(false);
            return ((long)(status.AvailableBlocks * status.BlockSize), (long)(status.TotalBlocks * status.BlockSize));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Debug($"SFTP: no free-space information ({ex.GetType().Name})");
            return (0, 0);
        }
    }

    // ── Plumbing ────────────────────────────────────────────────────────────────────────────

    /// <summary>Directories this connection has already created, so a copy does not recreate the
    /// same destination once per file. Guarded by its own lock: two panels can copy at once.</summary>
    private readonly HashSet<string> _knownDirectories = new(StringComparer.Ordinal);

    /// <summary>Serializes reconnection attempts — without it, two concurrent operations
    /// hitting SshConnectionException simultaneously would both call _client.Connect(),
    /// and SSH.NET doesn't document Connect() as thread-safe.</summary>
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    private Task RunAsync(Func<CancellationToken, Task> body, string operation, string subject, CancellationToken ct) =>
        RunAsync(async token => { await body(token).ConfigureAwait(false); return true; }, operation, subject, ct);

    /// <summary>
    /// Runs one SSH.NET call, bounding it and translating whatever it throws.
    ///
    /// <para>SSH.NET's exception types carry information the rest of the app cannot act on and a
    /// user cannot read. Each becomes an ordinary framework exception with a sentence that says what
    /// failed and on what - and, in the case of a permission error, what to do about it.</para>
    ///
    /// <para>The timeout matters as much as the translation: SSH.NET's own <c>OperationTimeout</c>
    /// defaults to infinite, so a server that stops answering mid-request would otherwise block the
    /// caller forever.</para>
    ///
    /// <para><b>Auto-reconnect.</b> A <see cref="SshConnectionException"/> means the SSH transport
    /// is gone — a network blip, a server restart, a keepalive that missed. Without reconnection the
    /// client stays disconnected and every subsequent operation fails until the user manually
    /// disconnects and reconnects. One reconnection attempt is made here; if it succeeds the
    /// operation is retried once. If reconnection fails the original exception propagates.</para>
    /// </summary>
    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> body, string operation, string subject, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RemoteLimits.RequestTimeout);

        try
        {
            return await body(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException($"SFTP: the server stopped responding during {operation} (no answer within {RemoteLimits.RequestTimeout.TotalSeconds:0} s)");
        }
        catch (SftpPathNotFoundException)
        {
            throw new FileNotFoundException($"SFTP: \"{subject}\" was not found on the server");
        }
        catch (SftpPermissionDeniedException)
        {
            throw new UnauthorizedAccessException($"SFTP: the account does not have permission to {operation} \"{subject}\"");
        }
        catch (SshConnectionException ex)
        {
            await _reconnectLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!_client.IsConnected)
                    _client.Connect();
                using var retryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                retryCts.CancelAfter(RemoteLimits.RequestTimeout);
                return await body(retryCts.Token).ConfigureAwait(false);
            }
            catch (SftpPathNotFoundException) { throw; }
            catch (SftpPermissionDeniedException) { throw; }
            catch
            {
                throw new IOException($"SFTP: the connection was lost during {operation}: {ex.Message}");
            }
            finally
            {
                _reconnectLock.Release();
            }
        }
        catch (SshException ex)
        {
            throw new IOException($"SFTP: {operation} of \"{subject}\" failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            if (_client.IsConnected) _client.Disconnect();
        }
        catch (Exception ex)
        {
            // The connection is going away regardless; a failure to say goodbye politely is not
            // worth propagating out of a Dispose.
            LogService.Debug($"SFTP: disconnect failed ({ex.GetType().Name})");
        }

        _client.Dispose();
        _reconnectLock.Dispose();
    }
}
