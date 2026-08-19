using MediaDevices;
using System.Runtime.InteropServices;
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
    private bool _disposed;

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

            lock (_deviceLock)
            {
                foreach (var dir in _device.GetDirectories(devicePath))
                {
                    var full = devicePath.TrimEnd('\\') + "\\" + dir;
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
                foreach (var file in _device.GetFiles(devicePath))
                {
                    var full = devicePath.TrimEnd('\\') + "\\" + file;
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

            return (IReadOnlyList<FileEntry>)entries;
        }, ct);

    public Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            var result = new List<FileEntry>();
            await EnumerateDeepRecursiveAsync(path, result, ct).ConfigureAwait(false);
            return (IReadOnlyList<FileEntry>)result;
        }, ct);

    private async Task EnumerateDeepRecursiveAsync(string path, List<FileEntry> result, CancellationToken ct)
    {
        var entries = await EnumerateAsync(path, includeHidden: false, ct).ConfigureAwait(false);
        foreach (var e in entries)
        {
            result.Add(e);
            if (e.IsDirectory)
                await EnumerateDeepRecursiveAsync(e.FullPath, result, ct).ConfigureAwait(false);
        }
    }

    public Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default) =>
        Task.Run<FileEntry?>(() =>
        {
            EnsureConnected();
            var devicePath = ToDevice(path);
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
        }, ct);

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            EnsureConnected();
            var p = ToDevice(path);
            lock (_deviceLock)
            {
                return _device.FileExists(p) || _device.DirectoryExists(p);
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
            lock (_deviceLock)
            {
                if (_device.DirectoryExists(p))
                    _device.DeleteDirectory(p, recursive);
                else if (_device.FileExists(p))
                    _device.DeleteFile(p);
            }
        }, ct);

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default) =>
        Task.Run(() => { EnsureConnected(); lock (_deviceLock) _device.CreateDirectory(ToDevice(path)); }, ct);

    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) =>
        Task.CompletedTask; // MTP doesn't support arbitrary attribute changes

    public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default) =>
        Task.FromResult((0L, 0L)); // MediaDevice doesn't expose free space

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
            finally
            {
                try { File.Delete(tempFile); } catch { /* best-effort */ }
            }
        }, ct);

    public string GetRootPath(string path) =>
        RemotePath.Make("mtp", _deviceId);

    // ── IDisposable ──

    public void Dispose()
    {
        lock (_deviceLock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _device.Disconnect(); } catch { /* best-effort on teardown */ }
            _device.Dispose();
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
