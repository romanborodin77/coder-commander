using CoderCommander.Services;

namespace CoderCommander.FileSystem.Remote.Ftp;

/// <summary>
/// <see cref="IFileSystem"/> over FTP and explicit FTPS.
///
/// <para>App-side paths are <c>ftp://host/dir/name</c>; translating those into the server's own
/// absolute paths is this class's job and nobody else's, so the rest of the app never sees an FTP
/// concept.</para>
///
/// <para><b>Capabilities are <see cref="FileSystemCapabilities.None"/></b>, which is load-bearing
/// rather than a placeholder: everything gated on <see cref="FileSystemCapabilities.NativePaths"/> -
/// secure wipe, folder-size walking, packing, the Recycle Bin, a FileSystemWatcher, git - would
/// either be meaningless here or would reach around this class to a local path that does not exist.
/// Declaring nothing makes every one of those guards refuse correctly with no new check written.</para>
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

    public string Name => "FTP";

    /// <inheritdoc/>
    public FileSystemCapabilities Capabilities => FileSystemCapabilities.None;

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
        var useMlsd = connection.SupportsMlsd;
        var lines = await connection.ReadLinesAsync(
            $"{(useMlsd ? "MLSD" : "LIST -a")} {serverPath}", ct).ConfigureAwait(false);

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
        queue.Enqueue(path);

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
                if (child.IsDirectory) queue.Enqueue(child.FullPath);
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

    public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        var inner = RemotePath.PathOf(path);
        if (inner.Length == 0) return;

        using var rental = await FtpRental.TakeAsync(_pool, ct).ConfigureAwait(false);

        // MKD creates one level and fails if the parent is missing, so ancestors are created
        // top-down. An existing directory answers 550, which is the desired end state rather than
        // an error - and is indistinguishable from "refused", which is why the failure is only
        // raised if the final level could not be created.
        var built = "";
        foreach (var segment in inner.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            built = built.Length == 0 ? segment : $"{built}/{segment}";

            await rental.Connection.SendAsync($"MKD {ToServerPath(ToAppPath(built))}", ct).ConfigureAwait(false);
        }

        var check = await rental.Connection.SendAsync($"CWD {ToServerPath(path)}", ct).ConfigureAwait(false);
        if (check.Code != 250)
            throw new IOException($"FTP: could not create \"{inner}\": {check}");
    }

    public async Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
    {
        var info = await GetFileInfoAsync(path, ct).ConfigureAwait(false);
        var isDirectory = info?.IsDirectory ?? false;

        if (!isDirectory)
        {
            using var fileRental = await FtpRental.TakeAsync(_pool, ct).ConfigureAwait(false);
            var reply = await fileRental.Connection.SendAsync($"DELE {ToServerPath(path)}", ct).ConfigureAwait(false);
            if (!reply.IsSuccess) throw new IOException($"FTP: could not delete \"{RemotePath.GetName(path)}\": {reply}");
            return;
        }

        // FTP has no recursive delete: RMD removes an empty directory and nothing else. The walk is
        // depth-first because a directory cannot go until its contents have.
        if (recursive)
        {
            foreach (var child in await EnumerateAsync(path, includeHidden: true, ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                await DeleteAsync(child.FullPath, recursive: true, ct).ConfigureAwait(false);
            }
        }

        using var dirRental = await FtpRental.TakeAsync(_pool, ct).ConfigureAwait(false);
        var removed = await dirRental.Connection.SendAsync($"RMD {ToServerPath(path)}", ct).ConfigureAwait(false);
        if (!removed.IsSuccess)
            throw new IOException($"FTP: could not remove \"{RemotePath.GetName(path)}\": {removed}");
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
