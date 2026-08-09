namespace CoderCommander.Terminal.Ui;

/// <summary>
/// Whether an OSC 8 hyperlink URI is safe to hand to <c>ShellExecute</c>. Shell output is not
/// trusted input - an OSC 8 URI can come from anything printed (e.g. <c>cat untrusted.txt</c>) -
/// so this is an allowlist, not a denylist. Notably excludes <c>file:</c>: a crafted local-file
/// link handed to ShellExecute is a much larger attack surface than a browser navigation.
/// </summary>
internal static class HyperlinkPolicy
{
    private static readonly HashSet<string> AllowedSchemes =
        new(StringComparer.OrdinalIgnoreCase) { Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto };

    public static bool IsAllowed(string uri, out Uri? parsed)
    {
        parsed = null;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var candidate))
            return false;
        if (!AllowedSchemes.Contains(candidate.Scheme))
            return false;
        parsed = candidate;
        return true;
    }
}
