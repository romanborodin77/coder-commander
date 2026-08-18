using System.Runtime.InteropServices;

namespace CoderCommander.FileSystem.Remote.Smb;

/// <summary>
/// <see cref="IFileSystem"/> over a UNC share, translating between <c>smb://host/path</c> (the app's
/// internal <see cref="RemotePath"/> form) and <c>\\host\path</c> (Windows UNC) at every I/O boundary.
///
/// <para>All file operations delegate to a <see cref="LocalFileSystem"/> instance — Windows' own
/// redirector handles SMB I/O natively, so there is no protocol layer to reimplement. This class
/// exists solely to (a) translate paths so the rest of the app sees <c>smb://</c> URLs (consistent
/// with WebDAV/FTP/SFTP) and (b) call <c>WNetCancelConnection2</c> on <see cref="Dispose"/> to close
/// the credential-bearing network connection opened by <see cref="SmbProvider"/>.</para>
///
/// <para><b>CAPABILITIES</b>: claims everything <see cref="FileSystemCapabilities.Local"/> offers
/// <b>except</b> <see cref="FileSystemCapabilities.NativePaths"/> — UNC paths are native, but the
/// paths this filesystem exposes (<c>smb://host/path</c>) are not, and side-channel <c>System.IO</c>
/// calls on them (e.g. <c>File.SetLastWriteTimeUtc</c>) fail. Operations that check NativePaths
/// (timestamp stamping in UnpackOperation, wipe, folder-size via DirectoryInfo) are correctly
/// skipped for SMB. <see cref="FileSystemCapabilities.RecycleBin"/> is absent for the same reason
/// as network shares in general — <c>SHFileOperation</c> deletes permanently on UNC.</para>
/// </summary>
internal sealed class SmbFileSystem : IFileSystem, IDisposable
{
    private readonly LocalFileSystem _local = new();
    private readonly string _host;
    private readonly string _uncRoot;
    private bool _disposed;

    public string Name => "SMB";
    public FileSystemCapabilities Capabilities =>
        FileSystemCapabilities.FileWatch | FileSystemCapabilities.GitStatus |
        FileSystemCapabilities.Writable | FileSystemCapabilities.Deletable;

    /// <param name="host">Server name, e.g. <c>NAS1</c> — used as the <see cref="RemotePath"/> authority.</param>
    /// <param name="uncRoot">UNC root the connection was opened against, e.g. <c>\\NAS1</c> or
    /// <c>\\NAS1\share</c>. Stored for <see cref="Dispose"/>.</param>
    internal SmbFileSystem(string host, string uncRoot)
    {
        _host = host;
        _uncRoot = uncRoot;
    }

    // ── Path translation ──

    /// <summary><c>smb://host/share/path</c> → <c>\\host\share\path</c>.</summary>
    private string ToUnc(string smbPath)
    {
        var body = RemotePath.BodyOf(smbPath); // "host/share/path"
        return "\\\\" + body.Replace('/', '\\');
    }

    /// <summary><c>\\host\share\path</c> → <c>smb://host/share/path</c>.</summary>
    private string ToSmb(string uncPath)
    {
        var body = uncPath.StartsWith("\\\\", StringComparison.Ordinal)
            ? uncPath[2..]
            : uncPath;
        // Strip the leading host segment — RemotePath.Make already injects _host as the authority.
        var slash = body.IndexOf('\\', StringComparison.Ordinal);
        var tail = slash >= 0 ? body[(slash + 1)..] : "";
        return RemotePath.Make("smb", _host, tail.Replace('\\', '/'));
    }

    /// <summary>Creates a new <see cref="FileEntry"/> with <see cref="FileEntry.FullPath"/> translated
    /// from UNC to <c>smb://</c>. <see cref="FileEntry.Name"/> is derived from <c>FullPath</c> in the
    /// constructor via <c>Path.GetFileName</c>, which on Windows splits on both <c>\</c> and <c>/</c>,
    /// so the name comes out correct for <c>smb://</c> paths too.</summary>
    private FileEntry TranslateEntry(FileEntry e)
    {
        if (string.IsNullOrEmpty(e.FullPath)) return e;
        return new FileEntry(
            ToSmb(e.FullPath), e.IsDirectory, e.Exists, e.Size, e.Attributes,
            e.CreatedTimeUtc, e.LastWriteTimeUtc, e.LastAccessTimeUtc);
    }

    // ── IFileSystem delegation ──

    public async Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default)
    {
        var entries = await _local.EnumerateAsync(ToUnc(path), includeHidden, ct).ConfigureAwait(false);
        return entries.Select(TranslateEntry).ToList();
    }

    public async Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default)
    {
        var entries = await _local.EnumerateDeepAsync(ToUnc(path), includeHidden, ct).ConfigureAwait(false);
        return entries.Select(TranslateEntry).ToList();
    }

    public async Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default)
    {
        var entry = await _local.GetFileInfoAsync(ToUnc(path), ct).ConfigureAwait(false);
        return entry is null ? null : TranslateEntry(entry);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
        _local.ExistsAsync(ToUnc(path), ct);

    public Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
        _local.CopyFileAsync(ToUnc(source), ToUnc(destination), overwrite, ct);

    public Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
        _local.MoveAsync(ToUnc(source), ToUnc(destination), overwrite, ct);

    public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default) =>
        _local.DeleteAsync(ToUnc(path), recursive, ct);

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default) =>
        _local.CreateDirectoryAsync(ToUnc(path), ct);

    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) =>
        _local.SetAttributesAsync(ToUnc(path), attributes, ct);

    public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default) =>
        _local.GetDriveSpaceAsync(ToUnc(path), ct);

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default) =>
        _local.OpenReadAsync(ToUnc(path), ct);

    public Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default) =>
        _local.CopyFromStreamAsync(ToUnc(destinationPath), source, ct);

    public string GetRootPath(string path) =>
        RemotePath.Make("smb", _host);

    // ── IDisposable ──

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetCancelConnection2(string lpName, int dwFlags, bool fForce);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WNetCancelConnection2(_uncRoot, 0, fForce: true);
    }
}
