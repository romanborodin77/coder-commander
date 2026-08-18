using System.Runtime.InteropServices;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.FileSystem.Remote.Smb;

/// <summary>
/// Builds an SMB connection from a saved profile by calling the Windows Networking API
/// (<c>WNetAddConnection2</c>) with alternate credentials, then returning an
/// <see cref="SmbFileSystem"/> that delegates to <see cref="LocalFileSystem"/> over the UNC path.
///
/// <para>UNC paths (<c>\\server\share</c>) already work through <see cref="LocalFileSystem"/> under
/// the current Windows identity. This provider exists for two reasons the default identity can't
/// cover: (a) connecting with a different username/password and (b) establishing the connection
/// explicitly so <see cref="Services.ConnectionManager"/> can track its lifecycle and disconnect
/// (<c>WNetCancelConnection2</c>) when the user closes it.</para>
///
/// <para>The profile's <see cref="ConnectionProfile.Url"/> is a UNC path (<c>\\server</c> or
/// <c>\\server\share</c>). Internally the filesystem uses <c>smb://server/share/path</c> — the same
/// <see cref="RemotePath"/> convention as WebDAV/FTP/SFTP — so navigation, path arithmetic and
/// connection matching all work identically.</para>
/// </summary>
public sealed class SmbProvider : IFileSystemProvider
{
    public static SmbProvider Instance { get; } = new();

    public string Scheme => "smb";
    public string DisplayName => "SMB";

    public async Task<IFileSystem> ConnectAsync(ConnectionProfile profile, string? password, CancellationToken ct)
    {
        var uncRoot = NormalizeUnc(profile.Url);
        if (uncRoot.Length < 2 || uncRoot[0] != '\\' || uncRoot[1] != '\\')
            throw new InvalidOperationException($"SMB URL must be a UNC path (\\\\server\\share), not \"{profile.Url}\"");

        // Extract host for RemotePath authority. Uri.TryCreate parses UNC as file:// URIs.
        var host = ExtractHost(uncRoot);

        // Establish the network connection with alternate credentials. CONNECT_TEMPORARY: no
        // drive letter, no persistence in the user's profile — the connection lives exactly as
        // long as this filesystem instance.
        var remoteName = uncRoot.TrimEnd('\\');
        var nr = new NETRESOURCE
        {
            dwType = RESOURCETYPE_DISK,
            lpRemoteName = remoteName,
            lpLocalName = null!,
            lpComment = null!,
            lpProvider = null!
        };
        var result = WNetAddConnection2(
            ref nr,
            password,
            profile.UserName.Length > 0 ? profile.UserName : null,
            ConnectTemporary);
        if (result != 0)
        {
            var msg = $"SMB connection to \"{remoteName}\" failed (Win32 error {result}).";
            if (result == ErrorLogonFailure) msg += " The user name or password is incorrect.";
            else if (result == ErrorBadNetName) msg += " The network name cannot be found.";
            throw new InvalidOperationException(msg);
        }

        // Verify the root is reachable before reporting success — WNetAddConnection2 can return
        // success for a connection that still can't be enumerated (e.g. permission issues at the
        // share level). This matches what every other provider does (WebDavProvider enumerates the
        // root before returning).
        SmbFileSystem fs;
        try
        {
            fs = new SmbFileSystem(host, remoteName);
            await fs.EnumerateAsync(RemotePath.Make(Scheme, host), includeHidden: true, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            WNetCancelConnection2(remoteName, 0, fForce: true);
            throw;
        }

        return fs;
    }

    /// <summary>Normalizes a UNC path: ensures leading <c>\\</c>, strips trailing backslashes.</summary>
    private static string NormalizeUnc(string url)
    {
        var u = url.Replace('/', '\\').TrimEnd('\\');
        if (!u.StartsWith("\\\\", StringComparison.Ordinal))
            u = "\\\\" + u;
        return u;
    }

    /// <summary>Extracts the host (server) component from a UNC path like <c>\\server\share</c>.</summary>
    private static string ExtractHost(string unc)
    {
        var stripped = unc[2..]; // drop leading \\
        var slash = stripped.IndexOf('\\', StringComparison.Ordinal);
        return slash < 0 ? stripped : stripped[..slash];
    }

    // ── P/Invoke: mpr.dll ──

    private const int ConnectTemporary = 0x00000004;
    private const int ErrorLogonFailure = 1326;
    private const int ErrorBadNetName = 67;
    private const int RESOURCETYPE_DISK = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string lpLocalName;
        public string lpRemoteName;
        public string lpComment;
        public string lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetAddConnection2(
        ref NETRESOURCE lpNetResource,
        string? lpPassword,
        string? lpUserName,
        int dwFlags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetCancelConnection2(
        string lpName,
        int dwFlags,
        bool fForce);
}
