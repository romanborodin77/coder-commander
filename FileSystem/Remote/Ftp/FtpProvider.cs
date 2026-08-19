using CoderCommander.Models;

namespace CoderCommander.FileSystem.Remote.Ftp;

/// <summary>
/// Builds an FTP connection from a saved profile.
///
/// <para>The address may be written <c>ftp://host</c> or <c>ftps://host</c>. Both speak the same
/// protocol - there is no separate "FTPS" wire format, only explicit TLS negotiated with
/// <c>AUTH TLS</c> on the ordinary port - and the difference is a policy one: <c>ftps://</c> means
/// the connection must fail rather than proceed without TLS. <c>ftp://</c> still <i>attempts</i>
/// TLS and only falls back with a warning, so the common case is encrypted without anyone having to
/// know to ask for it.</para>
///
/// <para>Implicit FTPS on port 990 is not supported: it was never standardised, and a server set up
/// today uses the explicit form.</para>
/// </summary>
public sealed class FtpProvider : IFileSystemProvider
{
    private const int DefaultPort = 21;

    public static FtpProvider Instance { get; } = new();

    public string Scheme => "ftp";
    public string DisplayName => "FTP / FTPS";

    public async Task<IFileSystem> ConnectAsync(ConnectionProfile profile, string? password, CancellationToken ct)
    {
        if (!Uri.TryCreate(profile.Url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Invalid FTP address: \"{profile.Url}\"");

        var scheme = uri.Scheme;
        if (!string.Equals(scheme, "ftp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scheme, "ftps", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"FTP needs an ftp or ftps address, not \"{uri.Scheme}\"");

        // Uri knows ftp's default port but not ftps', where it reports -1.
        var port = uri.Port > 0 ? uri.Port : DefaultPort;
        var requireTls = string.Equals(scheme, "ftps", StringComparison.OrdinalIgnoreCase);

        // The authority is what app paths are addressed by, so it is built the same way for both
        // spellings - a connection is not a different place because it is encrypted.
        var authority = port == DefaultPort ? uri.Host : $"{uri.Host}:{port}";

        var pool = new FtpConnectionPool(
            () => new FtpControlConnection(uri.Host, port, profile, password, requireTls),
            RemoteLimits.MaxFtpControlConnections);

        var fs = new FtpFileSystem(pool, authority, Uri.UnescapeDataString(uri.AbsolutePath));

        // Verify before reporting success. Building the pool contacts nothing, so without this the
        // app would announce "connected" for a host that does not resolve, a wrong password or an
        // untrusted certificate - and the user would only find out on their first click, by which
        // time the error has lost its context.
        try
        {
            await fs.EnumerateAsync(RemotePath.Make(Scheme, authority), includeHidden: true, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            fs.Dispose();
            throw;
        }

        return fs;
    }
}
