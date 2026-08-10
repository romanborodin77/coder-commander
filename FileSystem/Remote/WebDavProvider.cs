using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.FileSystem.Remote;

/// <summary>
/// Builds a WebDAV connection from a saved profile.
///
/// Everything security-sensitive about the transport lives here - TLS trust, credentials, redirect
/// policy, timeouts - so those decisions are made once, in one readable place, rather than being
/// scattered through the filesystem implementation.
/// </summary>
public sealed class WebDavProvider : IFileSystemProvider
{
    public static WebDavProvider Instance { get; } = new();

    public string Scheme => "dav";
    public string DisplayName => "WebDAV";

    public Task<IFileSystem> ConnectAsync(ConnectionProfile profile, string? password, CancellationToken ct)
    {
        if (!Uri.TryCreate(profile.Url, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException($"Invalid WebDAV address: \"{profile.Url}\"");

        if (baseUri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"WebDAV needs an http or https address, not \"{baseUri.Scheme}\"");

        if (baseUri.Scheme == "http")
        {
            // Not refused - a WebDAV server on a private network without TLS is a legitimate
            // setup - but it must not pass silently: credentials cross the wire in the clear.
            LogService.Warning($"WebDAV: connecting to {baseUri.Host} without TLS; credentials are sent unencrypted");
        }

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = RemoteLimits.MaxRedirects,
            ConnectTimeout = RemoteLimits.ConnectTimeout,
            // Send the Authorization header up front instead of waiting for a 401 challenge.
            // Without it every request costs two round trips, and some servers answer 401 to
            // PROPFIND without a challenge the client can act on.
            PreAuthenticate = true,
        };

        if (!string.IsNullOrEmpty(profile.UserName))
        {
            // NetworkCredential lets the handler negotiate whatever the server asks for - Basic,
            // Digest, NTLM - rather than hard-coding Basic and failing against anything else.
            handler.Credentials = new NetworkCredential(profile.UserName, password ?? "");
        }

        handler.SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = MakeCertificateValidator(profile),
        };

        var http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = RemoteLimits.RequestTimeout,
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CoderCommander");

        var authority = baseUri.IsDefaultPort ? baseUri.Host : $"{baseUri.Host}:{baseUri.Port}";
        return Task.FromResult<IFileSystem>(new WebDavFileSystem(http, baseUri, authority));
    }

    /// <summary>
    /// TLS trust policy.
    ///
    /// A normally-trusted chain is accepted. Anything else is rejected <b>unless</b> the profile
    /// pins that exact certificate by SHA-256 thumbprint, which the user had to accept explicitly.
    ///
    /// The alternative people reach for - a callback returning <c>true</c> - is not a weaker
    /// version of this, it is the absence of TLS authentication: it accepts any certificate from
    /// any host forever, so an attacker in the middle needs only to present one. Pinning keeps the
    /// self-signed-certificate case working while still refusing a substituted certificate.
    /// </summary>
    private static RemoteCertificateValidationCallback MakeCertificateValidator(ConnectionProfile profile)
    {
        var pinned = profile.AcceptedCertificateThumbprint?.Replace(":", "").Trim() ?? "";

        return (_, certificate, _, errors) =>
        {
            if (errors == SslPolicyErrors.None) return true;
            if (pinned.Length == 0 || certificate is null)
            {
                LogService.Warning($"WebDAV: TLS validation failed ({errors}) and no certificate is pinned for this connection");
                return false;
            }

            var actual = ComputeThumbprint(certificate);
            var matches = string.Equals(actual, pinned, StringComparison.OrdinalIgnoreCase);
            if (!matches)
                LogService.Warning("WebDAV: server certificate does not match the one accepted for this connection");
            return matches;
        };
    }

    /// <summary>SHA-256 of the raw certificate. SHA-1 - what <c>X509Certificate2.Thumbprint</c>
    /// still returns - is not collision-resistant, and a pin is exactly the place where a
    /// collision would be worth manufacturing.</summary>
    internal static string ComputeThumbprint(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
}
