using System.Text;

namespace CoderCommander.Terminal.Vt;

/// <summary>
/// Parses the two OSC sequences a shell uses to report its current working directory: OSC 7 (a
/// <c>file://</c> URL - used by Git Bash/WSL/bash generally) and the ConEmu-originated OSC 9;9
/// (a raw Windows path - needed because cmd.exe's <c>PROMPT</c> mini-language can't build a
/// proper <c>file://</c> URL). Wiring these into each shell's prompt happens in a later phase;
/// this is just the parsing/validation side.
/// </summary>
internal static class CwdReport
{
    /// <summary>Parses an OSC 7 payload of the form "file://host/path". Only host "",
    /// "localhost", or this machine's own name is accepted - a remote host reporting a path
    /// (e.g. over SSH) must never be allowed to navigate the local file panel.</summary>
    public static bool TryParseOsc7(ReadOnlySpan<char> payload, out string windowsPath) =>
        TryParseOsc7(payload, posixPathTranslator: null, out windowsPath);

    /// <summary>Same as <see cref="TryParseOsc7(ReadOnlySpan{char},out string)"/>, but for a shell
    /// whose OSC 7 payload is a POSIX path rather than a Windows one (WSL: <c>$(pwd)</c> reports
    /// e.g. <c>/mnt/c/Work</c>, not <c>C:\Work</c>). <paramref name="posixPathTranslator"/> is
    /// tried first on the decoded path; if it returns a translated Windows path that resolves to a
    /// real, existing directory, that wins - otherwise this falls back to treating the payload as
    /// a plain Windows path, same as the single-argument overload.</summary>
    public static bool TryParseOsc7(ReadOnlySpan<char> payload, Func<string, string?>? posixPathTranslator, out string windowsPath)
    {
        windowsPath = "";
        const string prefix = "file://";
        if (!payload.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = payload[prefix.Length..];
        var slashIndex = rest.IndexOf('/');
        var host = slashIndex < 0 ? rest.ToString() : rest[..slashIndex].ToString();
        var pathPart = slashIndex < 0 ? "" : rest[slashIndex..].ToString();

        if (host.Length > 0 &&
            !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            return false;

        var decoded = PercentDecode(pathPart);

        if (posixPathTranslator != null)
        {
            var translated = posixPathTranslator(decoded);
            if (translated != null && TryNormalizeToWindowsPath(translated, out windowsPath))
                return true;
        }

        return TryNormalizeToWindowsPath(decoded, out windowsPath);
    }

    /// <summary>Parses an OSC 9;9 payload - the raw path portion after the "9;9;" prefix has
    /// already been stripped by the dispatch layer.</summary>
    public static bool TryParseOsc9_9(ReadOnlySpan<char> pathPayload, out string windowsPath) =>
        TryNormalizeToWindowsPath(pathPayload.Trim().ToString(), out windowsPath);

    private static bool TryNormalizeToWindowsPath(string candidate, out string windowsPath)
    {
        windowsPath = "";
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        // "/C:/Work" (always forward-slash, from a file:// URL) -> "C:/Work"
        var normalized = candidate;
        if (normalized.Length >= 3 && normalized[0] == '/' && char.IsLetter(normalized[1]) && normalized[2] == ':')
            normalized = normalized[1..];

        normalized = normalized.Replace('/', '\\');

        if (normalized.IndexOfAny(['\0', '\r', '\n']) >= 0)
            return false;
        if (normalized.Length > 32_000)
            return false;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(normalized);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }

        if (!Directory.Exists(fullPath))
            return false;

        windowsPath = fullPath;
        return true;
    }

    private static string PercentDecode(ReadOnlySpan<char> input)
    {
        if (input.IndexOf('%') < 0)
            return input.ToString();

        var bytes = new List<byte>(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] == '%' && i + 2 < input.Length && Uri.IsHexDigit(input[i + 1]) && Uri.IsHexDigit(input[i + 2]))
            {
                bytes.Add((byte)((Uri.FromHex(input[i + 1]) << 4) | Uri.FromHex(input[i + 2])));
                i += 2;
            }
            else
            {
                Span<char> oneChar = [input[i]];
                Span<byte> encoded = stackalloc byte[4];
                var n = Encoding.UTF8.GetBytes(oneChar, encoded);
                for (var j = 0; j < n; j++)
                    bytes.Add(encoded[j]);
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
