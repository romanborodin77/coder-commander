using CoderCommander.Models;
using CoderCommander.Services;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace CoderCommander.FileSystem.Remote.Sftp;

/// <summary>
/// Builds an SFTP connection from a saved profile.
///
/// <para>Everything security-sensitive about the transport is decided here - which host key is
/// trusted, how the account authenticates, how long any of it may take - so those decisions are made
/// once, in one readable place.</para>
///
/// <para><b>Password authentication only, for now.</b> A private key would need somewhere to keep
/// the key file and its passphrase, and a passphrase is a second secret with the same handling
/// requirements as the password. That belongs in its own pass rather than bolted onto this one; the
/// profile model already has the field to grow into.</para>
/// </summary>
public sealed class SftpProvider : IFileSystemProvider
{
    private const int DefaultPort = 22;

    public static SftpProvider Instance { get; } = new();

    public string Scheme => "sftp";
    public string DisplayName => "SFTP (SSH)";

    public async Task<IFileSystem> ConnectAsync(ConnectionProfile profile, string? password, CancellationToken ct)
    {
        if (!Uri.TryCreate(profile.Url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Invalid SFTP address: \"{profile.Url}\"");

        if (!uri.Scheme.Equals("sftp", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SFTP needs an sftp address, not \"{uri.Scheme}\"");

        if (string.IsNullOrEmpty(profile.UserName))
            throw new InvalidOperationException("SFTP needs a user name; there is no anonymous access in SSH");

        // Uri does not know sftp's port, so it reports -1 when none is given.
        var port = uri.Port > 0 ? uri.Port : DefaultPort;
        var authority = port == DefaultPort ? uri.Host : $"{uri.Host}:{port}";

        // Built for this client and never handed out: SSH.NET documents ConnectionInfo as not
        // thread-safe and explicitly warns against sharing one between clients.
        var connectionInfo = new ConnectionInfo(uri.Host, port, profile.UserName,
            new PasswordAuthenticationMethod(profile.UserName, password ?? ""))
        {
            Timeout = RemoteLimits.ConnectTimeout,
        };

        var client = new SftpClient(connectionInfo)
        {
            // SSH.NET's default is infinite. A server that accepts the connection and then stops
            // answering would hold every later request open forever.
            OperationTimeout = RemoteLimits.RequestTimeout,
        };

        // The fingerprint the server actually presented, captured so a refusal can name it - without
        // it the user is told "not trusted" and given no way to proceed.
        var presented = "";
        client.HostKeyReceived += (_, e) =>
        {
            presented = e.FingerPrintSHA256 ?? "";

            // CanTrust defaults to true. Not assigning it here is the difference between checking
            // the server's identity and not checking it at all.
            e.CanTrust = SftpHostKeyPolicy.Matches(profile.AcceptedCertificateThumbprint, presented);

            if (e.CanTrust)
                LogService.Info($"SFTP: host key accepted for {uri.Host}");
        };

        try
        {
            await client.ConnectAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            client.Dispose();

            // A rejected host key surfaces from SSH.NET as an ordinary connection failure, so the
            // distinction has to be drawn here - and it is the one distinction that matters, because
            // the answer is "compare this fingerprint and save it", not "check your network".
            if (presented.Length > 0 && !SftpHostKeyPolicy.Matches(profile.AcceptedCertificateThumbprint, presented))
                throw new IOException(SftpHostKeyPolicy.ExplainRefusal(profile.AcceptedCertificateThumbprint, presented));

            if (ex is SshAuthenticationException)
                throw new IOException("SFTP: the server refused the user name or password");

            throw;
        }

        // The base path. An address with no path lands in the account's own home directory, which is
        // where an SSH session starts and what the user expects - not the filesystem root.
        var basePath = RemotePath.Normalize(Uri.UnescapeDataString(uri.AbsolutePath));
        var root = basePath.Length == 0
            ? (client.WorkingDirectory?.TrimEnd('/') ?? "")
            : "/" + basePath;

        var fs = new SftpFileSystem(client, authority, root);

        // Verify before reporting success. ConnectAsync above authenticated, but a base path that
        // does not exist would only be discovered on the user's first click, by which time the error
        // has lost its context.
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
