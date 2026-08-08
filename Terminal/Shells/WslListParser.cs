using System.Text;

namespace CoderCommander.Terminal.Shells;

/// <summary>
/// Parses the raw stdout bytes of <c>wsl.exe --list --quiet</c> (falling back to the
/// non-<c>--quiet</c> form's <c>" (Default)"</c> suffix, stripped identically, since older
/// <c>wsl.exe</c> builds don't support <c>--quiet</c>).
/// <para>
/// <b>Why this needs its own tested unit:</b> <c>wsl.exe</c> writes its redirected console output
/// as UTF-16LE (typically with a BOM, sometimes with trailing NUL padding). Decoding it as
/// UTF-8/ASCII instead - the naive <c>Encoding.UTF8.GetString(bytes)</c> a caller would reach for
/// by default - yields one distro name per interleaved NUL byte, which silently produces garbage
/// distro entries instead of an obvious failure.
/// </para>
/// </summary>
internal static class WslListParser
{
    private const string DefaultSuffix = " (Default)";

    public static IReadOnlyList<string> Parse(byte[] rawOutput)
    {
        if (rawOutput.Length == 0)
            return Array.Empty<string>();

        var text = Decode(rawOutput);
        var result = new List<string>();

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim('\0', ' ');
            if (line.Length == 0)
                continue;

            if (line.EndsWith(DefaultSuffix, StringComparison.Ordinal))
                line = line[..^DefaultSuffix.Length];

            line = line.Trim();
            if (line.Length > 0 && !result.Contains(line, StringComparer.OrdinalIgnoreCase))
                result.Add(line);
        }

        return result;
    }

    private static string Decode(byte[] raw)
    {
        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
            return Encoding.Unicode.GetString(raw, 2, raw.Length - 2);
        if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(raw, 2, raw.Length - 2);

        // No BOM. wsl.exe's redirected output is UTF-16LE far more often than not on real
        // installs, but sniff for the classic "lots of interleaved NULs in the ASCII range"
        // UTF-16 signature before committing to it, so genuinely UTF-8 output (an older wsl.exe,
        // or a test feeding this something else on purpose) still decodes sensibly instead of
        // being force-fed through the wrong decoder.
        var sampleLen = Math.Min(raw.Length, 64);
        var nulCount = 0;
        for (var i = 0; i < sampleLen; i++)
            if (raw[i] == 0)
                nulCount++;

        return nulCount > sampleLen / 4
            ? Encoding.Unicode.GetString(raw)
            : Encoding.UTF8.GetString(raw);
    }
}
