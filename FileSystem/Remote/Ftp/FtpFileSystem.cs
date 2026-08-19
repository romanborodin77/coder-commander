using CoderCommander.Services;

namespace CoderCommander.FileSystem.Remote.Ftp;

/// <summary>
/// <see cref="IFileSystem"/> over FTP and explicit FTPS.
///
/// <para>App-side paths are <c>ftp://host/dir/name</c>; translating those into the server's own
/// absolute paths is this class's job and nobody else's, so the rest of the app never sees an FTP
/// concept.</para>
///
/// <para><b><see cref="FileSystemCapabilities.NativePaths"/> is deliberately never declared</b>:
/// everything gated on it - secure wipe, folder-size walking, packing, the Recycle Bin, a
/// FileSystemWatcher, git - would either be meaningless here or would reach around this class to a
/// local path that does not exist. Not declaring it makes every one of those guards refuse
/// correctly with no new check written. <see cref="Capabilities"/> DOES declare
/// <see cref="FileSystemCapabilities.Writable"/>/<see cref="FileSystemCapabilities.Deletable"/>
/// unconditionally - FTP genuinely supports STOR/DELE/MKD.</para>
///
/// <para><b>Every command is built from a validated path.</b> FTP commands are newline-delimited
/// with no escaping, so a name carrying a CR or LF would inject a command of the attacker's
/// choosing. Names are filtered as they leave the listing parser, paths are checked again here, and
/// <see cref="FtpControlConnection"/> refuses a command containing a line break as a last line of
/// defence. Three layers, because a single missed one is a remote command injection.</para>
/// </summary>
public sealed class FtpFileSystem : IFileSystem, IDisposable
{
    private readonly FtpConnectionPool _pool;
    private readonly string _authority;
    private readonly string _basePath;

    /// <summary>Directories this connection has already created, so a copy does not recreate the
    /// same destination once per file. Guarded by its own lock: two panels can copy at once.</summary>
    private readonly HashSet<string> _knownDirectories = new(StringComparer.Ordinal);

    public string Name => "FTP";

    /// <inheritdoc/>
    public FileSystemCapabilities Capabilities => FileSystemCapabilities.Writable | FileSystemCapabilities.Deletable;

    internal FtpFileSystem(FtpConnectionPool pool, string authority, string basePath)
    {
        _pool = pool;
        _authority = authority;
        _basePath = RemotePath.Normalize(basePath);
    }

    public string GetRootPath(string path) => RemotePath.Make("ftp", _authority);

    // ── Path mapping ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// App path to the server's absolute path.
    ///
    /// Always absolute, never relative to whatever directory the connection happens to be in: a
    /// pooled connection is shared over time, so its current directory is not a fact any operation
    /// may depend on.
    /// </summary>
    private string ToServerPath(string appPath)
    {
        var inner = RemotePath.PathOf(appPath);

        // The second of the three layers described in the class remarks. The first is the listing
        // parser; the third is the control connection itself.
        foreach (var segment in inner.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!RemotePath.IsSafeEntryName(segment))
                throw new IOException($"FTP: refusing to use an unsafe path segment in \"{RemotePath.PathOf(appPath)}\"");
        }

        var combined = _basePath.Length == 0
            ? inner
            : inner.Length == 0 ? _basePath : $"{_basePath}/{inner}";

        return "/" + combined;
    }

    private string ToAppPath(string relativeInner) => RemotePath.Make("ftp", _authority, relativeInner);

    // ── Reading ─────────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default)
    {
        var serverPath = ToServerPath(path);
        var inner = RemotePath.PathOf(path);

        using var rental = await FtpRental.TakeAsync(_pool, ct).ConfigureAwait(false);
        var connection = rental.Connection;

        // MLSD wherever it exists: its output is defined by RFC 3659 and needs no shape-matching.
        // LIST output was never specified at all - see FtpListParser for what that costs.
        //
        // Plain LIST, with no "-a": the flag is a widespread convention rather than part of the
        // protocol, and a server that does not pass its argument to a shell reads "-a" as the path
        // and lists nothing. Losing dotfiles on some servers is a far smaller failure than losing
        // every listing on others. `includeHidden` is therefore not honoured over FTP, in either
        // listing format - the protocol has no notion of a hidden file to honour.
        var useMlsd = connection.SupportsMlsd;
        var lines = await connection.ReadLinesAsync(
            $"{(useMlsd ? "MLSD" : "LIST")} {serverPath}", ct).ConfigureAwait(false);

        var entries = useMlsd ? FtpListParser.ParseMlsd(lines) : FtpListParser.ParseList(lines);

        return entries.Select(e => new FileEntry(
            fullPath: ToAppPath(inner.Length == 0 ? e.Name : $"{inner}/{e.Name}"),
            isDirectory: e.IsDirectory,
            exists: true,
            size: e.Size,
            attributes: e.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
            createdTimeUtc: default,
            lastWriteTimeUtc: e.LastWriteTimeUtc,
            lastAccessTimeUtc: default)).ToList();
    }

    /// <summary>Walks the subtree one directory at a time. FTP has no recursive listing that any two
    /// servers agree on - <c>LIST -R</c> is a shell flag the server may or may not pass through -
    /// so the walk is done here, where it can be cancelled and bounded.</summary>
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
                LogService.Warning($"FTP: cannot list {RemotePath.PathOf(current)}: {ex.GetType().Name}");
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

    public async Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default)
    {
        var name = RemotePath.GetName(path);
        if (name.Length == 0 || RemotePath.PathOf(path).Length == 0) return null;

        var siblings = await EnumerateAsync(RemotePath.GetParent(path), includeHidden: true, ct).ConfigureAwait(false);
        return siblings.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Existence, by the cheapest command the server actually supports.
    ///
    /// <para><c>MLST</c> (RFC 3659) answers for files and directories alike and is preferred.
    /// Without it there is no single command that does: <c>SIZE</c> answers only for files - and
    /// only in binary mode - while a directory has to be probed with <c>CWD</c>. Both are tried,
    /// because answering "no" for every directory would make navigation impossible.</para>
    /// </summary>
    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var serverPath = ToServerPath(path);

            using var rental = await FtpRental.TakeAsync(_pool, ct).ConfigureAwait(false);
            var connection = rental.Connection;

            // The root of the connection always exists as far as the app is concerned; asking about
            // it with CWD would be answered by the server's home directory, not by this one.
            if (serverPath == "/" && _basePath.Length == 0) return true;

            if (connection.SupportsMlst)
                return (await connection.SendAsync($"MLST {serverPath}", ct).ConfigureAwait(false)).Code == 250;

            if ((await connection.SendAsync($"SIZE {serverPath}", ct).ConfigureAwait(false)).Code == 213)
                return true;

            return (await connection.SendAsync($"CWD {serverPath}", ct).ConfigureAwait(false)).Code == 250;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Debug($"FTP: existence check failed for {RemotePath.PathOf(path)}: {ex.GetType().Name}");
            return false;
        }
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        var serverPath = ToServerPath(path);

        // Not a `using`: the rental has to outlive this method, because the control connection
        // cannot carry anything else until the transfer ends. The returned stream owns it and gives
        // it back when it is disposed.
        var rental = await FtpRental.TakeAsync(_pool, ct).ConfigureAwait(false);
        try
        {
            var stream = (FtpDataStream)await rental.Connection
                .OpenDataStreamAsync($"RETR {serverPath}", ct).ConfigureAwait(false);
            stream.Released = rental.Dispose;
            return stream;
        }
        catch
        {
            rental.Dispose();
            throw;
        }
    }

    // ── Writing ─────────────────────────────────────────────────────────────────────────────

    public async Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default)
    {
        var serverPath = ToServerPath(destinationPath);

        using var rental = await FtpRental.TakeAsync(_pool, ct).ConfigureAwait(false);

        var data = await rental.Connection.OpenDataStreamAsync($"STOR {serverPath}", ct).ConfigureAwait(false);
        await using (data.ConfigureAwait(false))
        {
            await source.CopyToAsync(data, RemoteLimits.TransferBufferSize, ct).ConfigureAwait(false);
        }
        // Disposing the data stream is what ends the transfer and reads the server's verdict; a
        // rejected upload throws from there rather than being reported as a successful copy.
    }

    /// <summary>
    /// Creates a directory and any missing ancestors.
    ///
    /// <para><b>Directories already created are remembered.</b> A copy calls this once per file, for
    /// the same destination directory every time - which on a local disk is a cheap no-op and over
    /// FTP is a fresh round trip per level per file. Copying a few hundred files then spends more
    /// time creating a directory that has existed since the first one than transferring anything.
    /// The cache lives as long as the connection; a directory removed on the server behind our back
    /// makes the next write fail, which is the same thing that would happen anyway.</para>
    /// </summary>
    public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        var inner = RemotePath.PathOf(path);
        if (inner.Length == 0) return;

        lock (_knownDirectories)
        {
            if (_knownDirectories.Contains(inner)) return;
        }

        using var rental = await FtpRental.TakeAsync(_pool, ct).ConfigureAwait(false);

        // MKD creates one level and fails if the parent is missing, so ancestors are created
        // top-down. An existing directory answers 550, which is the desired end state rather than
        // an error - and is indistinguishable from "refused", which is why the failure is only
        // raised if the final level could not be reached.
        var built = "";
        var created = new List<string>();
        foreach (var segment in inner.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            built = built.Length == 0 ? segment : $"{built}/{segment}";
            created.Add(built);

            await rental.Connection.SendAsync($"MKD {ToServerPath(ToAppPath(built))}", ct).ConfigureAwait(false);
        }

        var check = await rental.Connection.SendAsync($"CWD {ToServerPath(path)}", ct).ConfigureAwait(false);
        if (check.Code != 250)
            throw new IOException($"FTP: could not create \"{inner}\": {check}");

        lock (_knownDirectories)
        {
            foreach (var directory in created) _knownDirectories.Add(directory);
        }
    }

    /// <summary>
    /// Deletes a file or a directory tree. FTP has no recursive delete - <c>RMD</c> removes an empty
    /// directory and nothing else - so the walk is done here, depth first.
    ///
    /// <para><b>DELE is attempted before asking what the path is.</b> The obvious implementation
    /// probes first, and that probe is a listing of the whole parent directory: deleting n files
    /// from one directory then costs n listings of it rather than n commands. Trying the common case
    /// first costs one round trip for a file and one extra for a directory.</para>
    /// </summary>
    public async Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
    {
        FtpReply deleteReply;
        using (var fileRental = await FtpRental.TakeAsync(_pool, ct).ConfigureAwait(false))
        {
            deleteReply = await fileRental.Connection.SendAsync($"DELE {ToServerPath(path)}", ct).ConfigureAwait(false);
            if (deleteReply.IsSuccess) return;
        }

        // DELE refused it: either this is a directory, or it genuinely cannot be deleted.
        if (recursive)
        {
            IReadOnlyList<FileEntry> children;
            try
            {
                children = await EnumerateAsync(path, includeHidden: true, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Not listable, so it was not a directory we could descend into - the original
                // refusal is the honest answer, not this secondary failure.
                throw new IOException($"FTP: could not delete \"{RemotePath.GetName(path)}\": {deleteReply}");
            }

            foreach (var child in children)
            {
                ct.ThrowIfCancellationRequested();
                await DeleteAsync(child.FullPath, recursive: true, ct).ConfigureAwait(false);
            }
        }

        using var dirRental = await FtpRental.TakeAsync(_pool, ct).ConfigureAwait(false);
        var removed = await dirRental.Connection.SendAsync($"RMD {ToServerPath(path)}", ct).ConfigureAwait(false);
        if (!removed.IsSuccess)
            throw new IOException($"FTP: could not remove \"{RemotePath.GetName(path)}\": {removed} (delete said: {deleteReply})");
    }

    /// <summary>
    /// Rename or move, which FTP spells as the two-command <c>RNFR</c>/<c>RNTO</c> pair.
    ///
    /// <para>The pair is stateful: RNFR arms the rename and RNTO completes it, and nothing else may
    /// go between them. That is exactly why they are sent on one rented connection rather than
    /// through two independent calls.</para>
    ///
    /// <para><paramref name="overwrite"/> cannot be honoured: the protocol has no flag for it and
    /// servers differ on whether RNTO replaces an existing file. Refusing when the destination
    /// exists and overwriting was not asked for is the half that can be enforced.</para>
    /// </summary>
    public async Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default)
    {
        if (!overwrite && await ExistsAsync(destination, ct).ConfigureAwait(false))
            throw new IOException($"FTP: \"{RemotePath.GetName(destination)}\" already exists");

        using var rental = await FtpRental.TakeAsync(_pool, ct).ConfigureAwait(false);

        var from = await rental.Connection.SendAsync($"RNFR {ToServerPath(source)}", ct).ConfigureAwait(false);
        if (from.Code != 350)
            throw new IOException($"FTP: cannot rename \"{RemotePath.GetName(source)}\": {from}");

        var to = await rental.Connection.SendAsync($"RNTO {ToServerPath(destination)}", ct).ConfigureAwait(false);
        if (!to.IsSuccess)
            throw new IOException($"FTP: cannot rename to \"{RemotePath.GetName(destination)}\": {to}");
    }

    /// <summary>
    /// FTP has no server-side copy, so the bytes travel down and back up through this machine.
    ///
    /// <para>That is genuinely what it costs, and pretending otherwise - by refusing - would break
    /// an ordinary copy within one connection. The two transfers use two pooled connections, which
    /// is why the pool holds more than one.</para>
    /// </summary>
    public async Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default)
    {
        if (!overwrite && await ExistsAsync(destination, ct).ConfigureAwait(false))
            throw new IOException($"FTP: \"{RemotePath.GetName(destination)}\" already exists");

        await using var input = await OpenReadAsync(source, ct).ConfigureAwait(false);
        await CopyFromStreamAsync(destination, input, ct).ConfigureAwait(false);
    }

    /// <summary>Deliberately a no-op, not an exception: callers set attributes opportunistically
    /// after a copy, and throwing would turn a completed transfer into a failed one. FTP has no
    /// portable notion of the attributes this app deals in.</summary>
    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <summary>No standard command reports free space, and the extensions that do are rare enough
    /// not to be worth a round trip. <c>(0, 0)</c> is this codebase's established "couldn't
    /// determine", which the decompression-bomb guard already treats as "skip the check" rather
    /// than as "no space".</summary>
    public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default) =>
        Task.FromResult((0L, 0L));

    public void Dispose() => _pool.Dispose();
}
