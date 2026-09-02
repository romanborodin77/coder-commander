using MediaDevices;
using System.Runtime.InteropServices;
using CoderCommander.FileSystem.Remote;
using CoderCommander.Services;
using CoderCommander.Utils;

namespace CoderCommander.FileSystem.Mtp;

/// <summary>
/// <see cref="IFileSystem"/> over an MTP/PTP device (Android phone, camera, media player) via the
/// <see cref="MediaDevice"/> wrapper around the Windows Portable Devices COM API.
///
/// <para><b>Paths</b>: MTP devices have no drive letter — their paths are device-internal
/// (<c>\Internal Storage\DCIM\Camera\photo.jpg</c>). This class exposes them as
/// <c>mtp://&lt;deviceId&gt;/Internal Storage/DCIM/...</c> using the app's <see cref="RemotePath"/>
/// convention, so navigation, path arithmetic and connection matching all work identically to
/// WebDAV/FTP/SFTP/SMB.</para>
///
/// <para><b>CAPABILITIES</b>: <see cref="FileSystemCapabilities.Writable"/> |
/// <see cref="FileSystemCapabilities.Deletable"/> — MTP supports upload, delete and
/// create-directory, but <b>not</b> <see cref="FileSystemCapabilities.NativePaths"/> (device paths
/// don't resolve outside the device, so ShellExecute/drag-out/real-path operations are unavailable
/// — materialization applies, same as archives). Write/delete is best-effort: MTP protocol
/// stability varies by device/driver.</para>
///
/// <para><b>Threading</b>: every <see cref="MediaDevice"/> call is synchronous and blocking. Each
/// method wraps its calls in <c>Task.Run</c> to keep the UI responsive, matching the project's
/// established pattern for non-async I/O (see <c>FtpControlConnection</c>).</para>
/// </summary>
internal sealed class MtpFileSystem : IFileSystem, IDisposable
{
    private readonly MediaDevice _device;
    private readonly string _deviceId;
    private readonly object _deviceLock = new();
    private volatile bool _disposed;

    public string Name => "MTP";
    public FileSystemCapabilities Capabilities =>
        FileSystemCapabilities.Writable | FileSystemCapabilities.Deletable;

    /// <param name="device">A connected <see cref="MediaDevice"/> (caller owns <c>Connect</c>;
    /// this class owns <c>Disconnect</c> on <see cref="Dispose"/>).</param>
    /// <param name="deviceId">Stable device identifier for path construction.</param>
    internal MtpFileSystem(MediaDevice device, string deviceId)
    {
        _device = device;
        _deviceId = deviceId;
    }

    // ── Path translation ──

    /// <summary><c>mtp://deviceId/Internal Storage/path</c> → <c>\Internal Storage\path</c>.</summary>
    private string ToDevice(string mtpPath)
    {
        var body = RemotePath.BodyOf(mtpPath); // "deviceId/Internal Storage/path"
        var slash = body.IndexOf('/', StringComparison.Ordinal);
        var devicePath = slash >= 0 ? body[(slash + 1)..] : "";
        return devicePath.Length > 0 ? "\\" + devicePath.Replace('/', '\\') : "\\";
    }

    /// <summary>Builds an <see cref="mtp://"/> path from a device-internal path.</summary>
    private string ToMtp(string devicePath)
    {
        var clean = devicePath.TrimStart('\\').Replace('\\', '/');
        return RemotePath.Make("mtp", _deviceId, clean);
    }

    /// <summary>Last segment of a device path, for FileEntry.Name.</summary>
    private static string GetName(string devicePath)
    {
        var clean = devicePath.TrimEnd('\\');
        var idx = clean.LastIndexOf('\\');
        return idx >= 0 ? clean[(idx + 1)..] : clean;
    }

    /// <summary>Checks the device is still usable before a MediaDevice call. Throws a clean
    /// IOException if the device has been disposed or unplugged, rather than letting a raw
    /// COMException surface.</summary>
    private void EnsureConnected()
    {
        if (_disposed)
            throw new IOException("MTP: device has been disconnected.");
    }

    // ── IFileSystem ──

    public Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            EnsureConnected();
            var devicePath = ToDevice(path);
            var entries = new List<FileEntry>();

            try
            {
                lock (_deviceLock)
                {
                    foreach (var full in _device.GetDirectories(devicePath))
                    {
                        // MediaDevices returns FULL device paths here - "\Internal shared storage",
                        // not "Internal shared storage". Treating them as bare names broke browsing
                        // outright: the safety check below rejects anything containing a separator,
                        // so every single entry was dropped and every MTP device listed as empty.
                        //
                        // The device names the entry, the same way a WebDAV/FTP/SFTP server does -
                        // that name goes on to build a real local path during a download, so it is
                        // checked exactly like a server-supplied listing entry (see
                        // WebDavPropfindParser's own identical check). Every other remote provider
                        // does this; MTP was the one gap.
                        if (!RemotePath.IsSafeEntryName(GetName(full)))
                        {
                            LogService.Warning("MTP: rejected a directory listing entry with an unsafe name");
                            continue;
                        }
                        long size = 0;
                        DateTime writeTime = default;
                        try
                        {
                            var di = _device.GetDirectoryInfo(full);
                            size = (long)di.Length;
                            writeTime = di.LastWriteTime?.ToUniversalTime() ?? default;
                        }
                        catch { /* best-effort — device may not report metadata */ }
                        entries.Add(new FileEntry(
                            ToMtp(full), isDirectory: true, exists: true,
                            size: size, attributes: FileAttributes.Directory,
                            lastWriteTimeUtc: writeTime));
                    }
                    // Full device paths again, exactly as for the directories above.
                    foreach (var full in _device.GetFiles(devicePath))
                    {
                        if (!RemotePath.IsSafeEntryName(GetName(full)))
                        {
                            LogService.Warning("MTP: rejected a file listing entry with an unsafe name");
                            continue;
                        }
                        long size = 0;
                        DateTime writeTime = default;
                        try
                        {
                            var fi = _device.GetFileInfo(full);
                            size = (long)fi.Length;
                            writeTime = fi.LastWriteTime?.ToUniversalTime() ?? default;
                        }
                        catch { /* best-effort — device may not report metadata */ }
                        entries.Add(new FileEntry(
                            ToMtp(full), isDirectory: false, exists: true,
                            size: size, attributes: FileAttributes.Normal,
                            lastWriteTimeUtc: writeTime));
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                throw new IOException($"MTP: device error enumerating \"{path}\": {ex.Message}", ex);
            }

            return (IReadOnlyList<FileEntry>)entries;
        }, ct);

    /// <summary>
    /// Iterative (queue-based) walk, not recursive - a self-referential or very deep device tree
    /// (a symlink-like alias some devices report, or a malformed/malicious driver response) used
    /// to risk a StackOverflowException (unrecoverable - it terminates the process) via unbounded
    /// recursion. Mirrors WebDavFileSystem.EnumerateDeepAsync's shape: a visited-set guards cycles
    /// and RemoteLimits.MaxEntriesPerDirectory caps the total, same as every other remote provider.
    /// </summary>
    public Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            var result = new List<FileEntry>();
            var queue = new Queue<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                    LogService.Warning($"MTP: cannot list {current}: {ex.GetType().Name}");
                    continue;
                }

                foreach (var child in children)
                {
                    result.Add(child);
                    if (child.IsDirectory && visited.Add(child.FullPath))
                        queue.Enqueue(child.FullPath);
                }
            }

            return (IReadOnlyList<FileEntry>)result;
        }, ct);

    public Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default) =>
        Task.Run<FileEntry?>(() =>
        {
            EnsureConnected();
            var devicePath = ToDevice(path);
            try
            {
                lock (_deviceLock)
                {
                    if (_device.DirectoryExists(devicePath))
                    {
                        long size = 0;
                        DateTime writeTime = default;
                        try { var di = _device.GetDirectoryInfo(devicePath); size = (long)di.Length; writeTime = di.LastWriteTime?.ToUniversalTime() ?? default; } catch { /* best-effort */ }
                        return new FileEntry(ToMtp(devicePath), isDirectory: true, exists: true,
                            size: size, attributes: FileAttributes.Directory, lastWriteTimeUtc: writeTime);
                    }
                    if (_device.FileExists(devicePath))
                    {
                        long size = 0;
                        DateTime writeTime = default;
                        try { var fi = _device.GetFileInfo(devicePath); size = (long)fi.Length; writeTime = fi.LastWriteTime?.ToUniversalTime() ?? default; } catch { /* best-effort */ }
                        return new FileEntry(ToMtp(devicePath), isDirectory: false, exists: true,
                            size: size, attributes: FileAttributes.Normal, lastWriteTimeUtc: writeTime);
                    }
                    return null;
                }
            }
            catch (Exception ex) when (ex is COMException or ObjectDisposedException)
            {
                throw new IOException($"MTP: device error getting info for \"{path}\": {ex.Message}", ex);
            }
        }, ct);

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            EnsureConnected();
            var p = ToDevice(path);
            try
            {
                lock (_deviceLock)
                {
                    return _device.FileExists(p) || _device.DirectoryExists(p);
                }
            }
            catch (Exception ex) when (ex is COMException or ObjectDisposedException)
            {
                throw new IOException($"MTP: device error checking \"{path}\": {ex.Message}", ex);
            }
        }, ct);

    public Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            EnsureConnected();
            var src = ToDevice(source);
            var dst = ToDevice(destination);
            var tempFile = TempFileNaming.InSystemTemp("mtp");
            try
            {
                lock (_deviceLock)
                {
                    if (!overwrite && _device.FileExists(dst))
                        throw new IOException($"MTP: \"{destination}\" already exists.");
                    _device.DownloadFile(src, tempFile);
                    _device.UploadFile(tempFile, dst);
                }
            }
            catch (COMException ex)
            {
                throw new IOException($"MTP: device error during copy: {ex.Message}", ex);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* best-effort */ }
            }
        }, ct);

    public Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            EnsureConnected();
            var src = ToDevice(source);
            var dst = ToDevice(destination);
            var tempFile = TempFileNaming.InSystemTemp("mtp_move");
            try
            {
                lock (_deviceLock)
                {
                    if (!overwrite && _device.FileExists(dst))
                        throw new IOException($"MTP: \"{destination}\" already exists.");
                    // MTP doesn't expose rename; download + upload + delete original.
                    _device.DownloadFile(src, tempFile);
                    _device.UploadFile(tempFile, dst);
                    _device.DeleteFile(src);
                }
            }
            catch (COMException ex)
            {
                throw new IOException($"MTP: device error during move: {ex.Message}", ex);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* best-effort */ }
            }
        }, ct);

    public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            EnsureConnected();
            var p = ToDevice(path);
            try
            {
                lock (_deviceLock)
                {
                    if (_device.DirectoryExists(p))
                        _device.DeleteDirectory(p, recursive);
                    else if (_device.FileExists(p))
                        _device.DeleteFile(p);
                }
            }
            catch (Exception ex) when (ex is COMException or ObjectDisposedException)
            {
                throw new IOException($"MTP: device error deleting \"{path}\": {ex.Message}", ex);
            }
        }, ct);

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            EnsureConnected();
            try
            {
                lock (_deviceLock) _device.CreateDirectory(ToDevice(path));
            }
            catch (Exception ex) when (ex is COMException or ObjectDisposedException)
            {
                throw new IOException($"MTP: device error creating directory \"{path}\": {ex.Message}", ex);
            }
        }, ct);

    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) =>
        Task.CompletedTask; // MTP doesn't support arbitrary attribute changes

    /// <summary>
    /// Free and total bytes for the storage the path lives on. This used to return zeros with a
    /// comment saying MediaDevice does not expose free space - it does, just not on the device:
    /// <c>GetDrives()</c> returns one entry per storage with real capacity figures (a phone reports
    /// its internal storage and any card separately). The panel's status bar therefore showed
    /// nothing at all for a device that knows perfectly well how full it is.
    /// </summary>
    public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            EnsureConnected();
            var devicePath = ToDevice(path);

            try
            {
                lock (_deviceLock)
                {
                    var drives = _device.GetDrives().ToList();
                    if (drives.Count == 0) return (0L, 0L);

                    // A device path starts with its storage's own name, so the storage the path
                    // belongs to is the one whose root the path starts with. At the device root
                    // itself there is no storage named yet - report the first, which is the
                    // internal one on every phone this was tried against.
                    var drive = drives.FirstOrDefault(d =>
                        d.Name is { Length: > 0 } &&
                        devicePath.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase))
                        ?? drives[0];

                    return ((long)drive.TotalFreeSpace, (long)drive.TotalSize);
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Capacity is decoration on a status bar; a device that will not answer should not
                // turn a panel refresh into an error.
                return (0L, 0L);
            }
        }, ct);

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default) =>
        Task.Run<Stream>(() =>
        {
            EnsureConnected();
            var devicePath = ToDevice(path);
            // MTP doesn't support streaming reads — download to a temp file and return a FileStream
            // that deletes the temp file on close.
            var tempFile = TempFileNaming.InSystemTemp("mtp");
            try
            {
                lock (_deviceLock) { _device.DownloadFile(devicePath, tempFile); }
            }
            catch (Exception ex) when (ex is COMException or ObjectDisposedException)
            {
                try { File.Delete(tempFile); } catch { /* best-effort */ }
                throw new IOException($"MTP: device error reading \"{path}\": {ex.Message}", ex);
            }
            catch
            {
                // DownloadFile failed — clean up the empty/partial temp file before rethrowing,
                // matching the try/finally pattern in CopyFileAsync/MoveAsync.
                try { File.Delete(tempFile); } catch { /* best-effort */ }
                throw;
            }
            return new MtpTempStream(tempFile);
        }, ct);

    public Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            EnsureConnected();
            var devicePath = ToDevice(destinationPath);
            // Upload via a temp file — MTP's UploadFile takes a file path, not a stream.
            var tempFile = TempFileNaming.InSystemTemp("mtp_upload");
            try
            {
                using (var fs = File.Create(tempFile))
                    source.CopyTo(fs);
                lock (_deviceLock) { _device.UploadFile(tempFile, devicePath); }
            }
            catch (Exception ex) when (ex is COMException or ObjectDisposedException)
            {
                throw new IOException($"MTP: device error writing \"{destinationPath}\": {ex.Message}", ex);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* best-effort */ }
            }
        }, ct);

    public string GetRootPath(string path) =>
        RemotePath.Make("mtp", _deviceId);

    /// <summary>No shell path exists - an MTP device's entries live behind the Windows Portable
    /// Devices COM API, not a filesystem path the shell can resolve.</summary>
    public string? GetShellPath(string path) => null;

    // ── IDisposable ──

    public void Dispose()
    {
        if (_disposed) return;
        // Set first, unlocked - a transfer currently inside one of the `lock (_deviceLock) {
        // _device.DownloadFile/UploadFile(...) }` calls above cannot be interrupted (MediaDevices
        // offers no cancellation for these), and MainForm.OnFormClosed calls this on the UI
        // thread. Taking the lock unconditionally here used to block the whole app for the full
        // duration of a multi-gigabyte MTP transfer on close, with no progress and no way to
        // cancel.
        _disposed = true;

        // Bounded wait: if a transfer is genuinely still running, don't block further - the
        // process is exiting either way, and the OS/WPD driver reclaims the device connection on
        // process exit regardless of whether Disconnect()/Dispose() ran cleanly here.
        if (!Monitor.TryEnter(_deviceLock, TimeSpan.FromMilliseconds(500)))
        {
            LogService.Warning($"MTP: device {_deviceId} is still transferring on close - skipping graceful disconnect.");
            return;
        }
        try
        {
            try { _device.Disconnect(); } catch { /* best-effort on teardown */ }
            _device.Dispose();
        }
        finally
        {
            Monitor.Exit(_deviceLock);
        }
    }

    /// <summary>A <see cref="FileStream"/> over a temp file that deletes the file on dispose.
    /// Used by <see cref="OpenReadAsync"/> — MTP can't stream, so we download to temp and clean up
    /// when the consumer is done.</summary>
    private sealed class MtpTempStream : FileStream
    {
        public MtpTempStream(string path) : base(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 8192, FileOptions.DeleteOnClose) { }
    }
}
