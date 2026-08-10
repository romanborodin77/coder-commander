namespace CoderCommander.FileSystem.Remote.Sftp;

/// <summary>
/// Whether to trust the key an SSH server presents.
///
/// <para><b>This is the whole of SSH's authentication of the server.</b> SSH has no certificate
/// authorities: the only thing standing between a client and an impostor is that the client
/// recognises the host key. SSH.NET makes that easy to get catastrophically wrong -
/// <c>HostKeyEventArgs.CanTrust</c> defaults to <c>true</c>, so a client that simply does not
/// subscribe to <c>HostKeyReceived</c> accepts every key from every host, forever. That is not a
/// weaker check; it is no check, and it is indistinguishable from working until someone is in the
/// middle.</para>
///
/// <para><b>No trust on first use.</b> The familiar OpenSSH behaviour - accept an unknown key,
/// remember it, warn if it ever changes - is a deliberate trade: it protects every connection after
/// the first, and nothing on the first. This app refuses instead and shows the fingerprint, so that
/// accepting a key is something the user did on purpose after comparing it with the server's own
/// records. It costs one deliberate step and removes the window entirely.</para>
///
/// <para>Kept pure so the comparison - which is the part that must not be lenient - is testable
/// without a server.</para>
/// </summary>
public static class SftpHostKeyPolicy
{
    /// <summary>What OpenSSH prints in front of a SHA-256 fingerprint, and therefore what a user
    /// pasting one will bring along with it.</summary>
    private const string Sha256Prefix = "SHA256:";

    /// <summary>
    /// Reduces a fingerprint to the form comparisons are made in: the bare base64 body, with no
    /// algorithm prefix, no padding and no surrounding whitespace.
    ///
    /// <para>Base64 is case-sensitive, so case is <b>not</b> folded - two fingerprints differing
    /// only in case are two different keys, and treating them as one would accept a key the user
    /// never saw.</para>
    /// </summary>
    public static string Normalize(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint)) return "";

        var text = fingerprint.Trim();
        if (text.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase))
            text = text[Sha256Prefix.Length..];

        // The padding is optional in every place these are printed, so it cannot be part of the
        // comparison.
        return text.TrimEnd('=').Trim();
    }

    /// <summary>
    /// Whether a fingerprint the user pasted is an MD5 one - the colon-separated hex form older
    /// tools print.
    ///
    /// <para>Accepted nowhere. MD5 is not collision-resistant, and a fingerprint is precisely the
    /// place where producing a second key with the same one would be worth the effort. Detected
    /// rather than merely failing to match, so the user is told to use the SHA-256 form instead of
    /// being left wondering why a fingerprint they copied correctly is rejected.</para>
    /// </summary>
    public static bool LooksLikeMd5(string? fingerprint)
    {
        var text = Normalize(fingerprint);
        if (text.Length == 0) return false;
        if (text.StartsWith("MD5:", StringComparison.OrdinalIgnoreCase)) return true;

        // 16 bytes as "aa:bb:...", i.e. 15 colons.
        return text.Count(c => c == ':') >= 8;
    }

    /// <summary>Whether the key the server presented is the one this profile accepted.</summary>
    public static bool Matches(string? pinned, string? actual)
    {
        var expected = Normalize(pinned);
        if (expected.Length == 0) return false;
        if (LooksLikeMd5(pinned)) return false;

        return string.Equals(expected, Normalize(actual), StringComparison.Ordinal);
    }

    /// <summary>The reason a key was refused, ready to show. Includes the fingerprint the server
    /// actually presented, because refusing without it leaves the user no way to proceed except to
    /// find it somewhere else.</summary>
    public static string ExplainRefusal(string? pinned, string actual)
    {
        if (LooksLikeMd5(pinned))
            return $"The saved fingerprint is an MD5 one, which is not accepted. Replace it with the SHA-256 fingerprint: SHA256:{Normalize(actual)}";

        if (Normalize(pinned).Length == 0)
            return $"This server's identity has not been accepted yet. If SHA256:{Normalize(actual)} matches the server's own published fingerprint, enter it in the connection's settings.";

        return $"The server presented a different key than the one accepted for this connection (SHA256:{Normalize(actual)}). Either the server was rebuilt, or something is answering in its place.";
    }
}
