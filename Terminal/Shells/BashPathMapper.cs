namespace CoderCommander.Terminal.Shells;

/// <summary>
/// Maps paths between Windows and Git-for-Windows Bash's filesystem view, for cwd
/// synchronization. <c>C:\Work</c> &lt;-&gt; <c>/c/Work</c> (drive letter lowercased, no
/// <c>/mnt/</c> prefix — Git-for-Windows uses a flat <c>/&lt;drive&gt;/...</c> mount convention,
/// unlike WSL's <c>/mnt/&lt;drive&gt;/...</c>).
///
/// <para>Also handles <c>/mnt/c/...</c> paths in case the bash instance is running inside a
/// WSL-flavoured environment rather than native Git-for-Windows — the <c>/mnt/</c> form is
/// tried first and falls back to the flat <c>/c/...</c> form, so both conventions round-trip
/// correctly regardless of which bash variant produced the path.</para>
///
/// <para>Paths outside any drive-letter mount (e.g. <c>/usr/share</c>, <c>/tmp</c>) have no
/// meaningful Windows-side representation and are rejected — the bash shell's own virtual
/// filesystem is not accessible from Windows explorer, so a <c>cd</c> push targeting such a
/// path is impossible to translate. This matches <see cref="WslPathMapper"/>'s treatment of
/// <c>/proc</c>/<c>/sys</c>/<c>/dev</c>.</para>
/// </summary>
internal sealed class BashPathMapper
{
    /// <summary>Git-for-Windows mount root: <c>/c/</c>, <c>/d/</c>, etc. — no <c>/mnt/</c> prefix.</summary>
    private const string FlatMountRoot = "/";

    /// <summary>WSL-style mount root, accepted on input (bash running under WSL).</summary>
    private const string WslMountRoot = "/mnt/";

    /// <summary>Windows path -&gt; Git-for-Windows Bash POSIX view. Only succeeds for a path on a
    /// drive letter; a UNC or relative path returns false (Bash's <c>cd</c> can't reach a UNC
    /// without an explicit mount point).</summary>
    public bool TryToPosix(string windowsPath, out string posixPath)
    {
        posixPath = "";
        if (string.IsNullOrWhiteSpace(windowsPath))
            return false;

        var full = windowsPath.Replace('/', '\\');
        if (full.Length < 2 || full[1] != ':' || !char.IsLetter(full[0]))
            return false;

        var drive = char.ToLowerInvariant(full[0]);
        var rest = full.Length > 2 ? full[2..].Replace('\\', '/').TrimStart('/') : "";
        posixPath = rest.Length == 0 ? $"/{drive}" : $"/{drive}/{rest}";
        return true;
    }

    /// <summary>Bash POSIX path -&gt; a Windows path: under either mount convention
    /// (<c>/c/...</c> or <c>/mnt/c/...</c>), the real drive-letter path; otherwise false, since
    /// Git-for-Windows' internal paths (<c>/usr/</c>, <c>/tmp/</c>, etc.) have no Windows
    /// equivalent — unlike WSL there's no <c>\\wsl.localhost\</c> UNC fallback for a
    /// Git-for-Windows virtual filesystem.</summary>
    public bool TryToWindows(string posixPath, out string windowsPath)
    {
        windowsPath = "";
        if (string.IsNullOrWhiteSpace(posixPath))
            return false;

        var path = posixPath.Replace('\\', '/');

        // Try WSL-style /mnt/c/... first (bash might be running under WSL).
        if (path.StartsWith(WslMountRoot, StringComparison.Ordinal))
        {
            var rest = path[WslMountRoot.Length..];
            if (TryParseDriveLetter(rest, out var drive, out var tail))
            {
                windowsPath = tail.Length == 0 ? $"{drive}:\\" : $"{drive}:\\{tail}";
                return true;
            }
        }

        // Virtual filesystems with no Windows equivalent — checked before the flat mount form
        // so that /usr, /home, /tmp etc. are rejected rather than misread as drive-letter paths
        // (e.g. /usr → U:\sr\share, a nonsensical path).
        if (path.StartsWith("/proc", StringComparison.Ordinal) ||
            path.StartsWith("/sys", StringComparison.Ordinal) ||
            path.StartsWith("/dev", StringComparison.Ordinal) ||
            path.StartsWith("/usr", StringComparison.Ordinal) ||
            path.StartsWith("/home", StringComparison.Ordinal) ||
            path.StartsWith("/tmp", StringComparison.Ordinal) ||
            path.StartsWith("/var", StringComparison.Ordinal) ||
            path.StartsWith("/etc", StringComparison.Ordinal) ||
            path.StartsWith("/bin", StringComparison.Ordinal) ||
            path.StartsWith("/sbin", StringComparison.Ordinal) ||
            path.StartsWith("/lib", StringComparison.Ordinal) ||
            path.StartsWith("/opt", StringComparison.Ordinal) ||
            path.StartsWith("/boot", StringComparison.Ordinal) ||
            path.StartsWith("/root", StringComparison.Ordinal))
            return false;

        // Git-for-Windows flat /c/... form. Require the drive letter to be followed by
        // end-of-string or '/' — /usr/share must NOT match here (u is a letter, but usr is not a drive).
        if (path.Length >= 2 && path[0] == FlatMountRoot[0] && char.IsLetter(path[1]) &&
            (path.Length == 2 || path[2] == '/'))
        {
            var rest = path[1..];
            if (TryParseDriveLetter(rest, out var drive, out var tail))
            {
                windowsPath = tail.Length == 0 ? $"{drive}:\\" : $"{drive}:\\{tail}";
                return true;
            }
        }

        return false;
    }

    /// <summary>Given the portion after the mount root (e.g. <c>c/Work</c> or <c>c</c>),
    /// extracts the uppercase drive letter and the remaining path with backslashes.</summary>
    private static bool TryParseDriveLetter(string rest, out char drive, out string tail)
    {
        drive = '\0';
        tail = "";
        if (rest.Length == 0 || !char.IsLetter(rest[0]))
            return false;

        drive = char.ToUpperInvariant(rest[0]);
        tail = rest.Length > 1 ? rest[1..].Replace('/', '\\').TrimStart('\\') : "";
        return true;
    }
}
