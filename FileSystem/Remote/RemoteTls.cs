using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.FileSystem.Remote;

/// <summary>
/// The one TLS trust decision every remote provider shares.
///
/// <para>A normally-trusted chain is accepted. Anything else is rejected <b>unless</b> the profile
/// pins that exact certificate by SHA-256 thumbprint, which the user had to accept explicitly.</para>
///
/// <para>The alternative people reach for - a callback returning <c>true</c> - is not a weaker
/// version of this, it is the absence of TLS authentication: it accepts any certificate from any
/// host forever, so an attacker in the middle needs only to present one. Pinning keeps the
/// self-signed-certificate case working while still refusing a substituted certificate.</para>
///
/// <para>It lives here, apart from any one protocol, because the moment a second provider needs it
/// the tempting shortcut is to write a second, laxer copy. There is one copy, and every provider
/// that speaks TLS uses it.</para>
/// </summary>
public static class RemoteTls
{
    /// <param name="protocol">Named in the log line only, so a failure says which connection it
    /// came from without the caller having to build its own message.</param>
    public static RemoteCertificateValidationCallback MakeCertificateValidator(ConnectionProfile profile, string protocol)
    {
        var pinned = profile.AcceptedCertificateThumbprint?.Replace(":", "").Trim() ?? "";

        return (_, certificate, _, errors) =>
        {
            if (errors == SslPolicyErrors.None) return true;
            if (pinned.Length == 0 || certificate is null)
            {
                LogService.Warning($"{protocol}: TLS validation failed ({errors}) and no certificate is pinned for this connection");
                return false;
            }

            var actual = ComputeThumbprint(certificate);
            var matches = string.Equals(actual, pinned, StringComparison.OrdinalIgnoreCase);
            if (!matches)
                LogService.Warning($"{protocol}: server certificate does not match the one accepted for this connection");
            return matches;
        };
    }

    /// <summary>SHA-256 of the raw certificate. SHA-1 - what <c>X509Certificate2.Thumbprint</c>
    /// still returns - is not collision-resistant, and a pin is exactly the place where a collision
    /// would be worth manufacturing.</summary>
    public static string ComputeThumbprint(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
}
