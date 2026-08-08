namespace CoderCommander.Terminal.Shells;

/// <summary>
/// Maps paths between Windows and one WSL distribution's filesystem view, for cwd
/// synchronization. <c>C:\Work</c> &lt;-&gt; <c>/mnt/c/Work</c> (drive letter lowercased) via the
/// distro's automount root (default <c>/mnt/</c>, overridable per distro - some installs remap
/// it via <c>/etc/wsl.conf</c>'s <c>[automount] root=</c> setting). Anything outside the automount
/// root maps to the distro's <c>\\wsl.localhost\&lt;distro&gt;\...</c> UNC form, except a handful
/// of virtual filesystems (<c>/proc</c>, <c>/sys</c>, <c>/dev</c>) that have no meaningful
/// Windows-side representation at all and are rejected outright rather than producing a UNC path
/// guaranteed to fail every subsequent existence check.
/// </summary>
internal sealed class WslPathMapper
{
    private readonly string _distro;
    private readonly string _automountRoot;

    public WslPathMapper(string distro, string automountRoot = "/mnt/")
    {
        _distro = distro;
        _automountRoot = automountRoot.EndsWith('/') ? automountRoot : automountRoot + "/";
    }

    /// <summary>Windows path -&gt; this distro's POSIX view of it. Only succeeds for a path on a
    /// drive letter (anything under the automount root); a UNC or relative path returns false.</summary>
    public bool TryToWsl(string windowsPath, out string wslPath)
    {
        wslPath = "";
        if (string.IsNullOrWhiteSpace(windowsPath))
            return false;

        var full = windowsPath.Replace('/', '\\');
        if (full.Length < 2 || full[1] != ':' || !char.IsLetter(full[0]))
            return false;

        var drive = char.ToLowerInvariant(full[0]);
        var rest = full.Length > 2 ? full[2..].Replace('\\', '/').TrimStart('/') : "";
        wslPath = rest.Length == 0 ? $"{_automountRoot}{drive}" : $"{_automountRoot}{drive}/{rest}";
        return true;
    }

    /// <summary>This distro's POSIX path -&gt; a Windows path: under the automount root, the real
    /// drive-letter path; otherwise a <c>\\wsl.localhost\&lt;distro&gt;\...</c> UNC path, or false
    /// for a virtual filesystem path with no Windows-side equivalent.</summary>
    public bool TryToWindows(string wslPath, out string windowsPath)
    {
        windowsPath = "";
        if (string.IsNullOrWhiteSpace(wslPath))
            return false;

        var path = wslPath.Replace('\\', '/');

        if (path.StartsWith(_automountRoot, StringComparison.Ordinal))
        {
            var rest = path[_automountRoot.Length..];
            if (rest.Length > 0 && char.IsLetter(rest[0]) && (rest.Length == 1 || rest[1] == '/'))
            {
                var drive = char.ToUpperInvariant(rest[0]);
                var tail = rest.Length > 1 ? rest[2..].Replace('/', '\\') : "";
                windowsPath = tail.Length == 0 ? $"{drive}:\\" : $"{drive}:\\{tail}";
                return true;
            }
        }

        if (path.StartsWith("/proc", StringComparison.Ordinal) ||
            path.StartsWith("/sys", StringComparison.Ordinal) ||
            path.StartsWith("/dev", StringComparison.Ordinal))
            return false;

        windowsPath = $"\\\\wsl.localhost\\{_distro}{path.Replace('/', '\\')}";
        return true;
    }
}
