namespace CoderCommander.Terminal.Shells;

/// <summary>Broad category a <see cref="ShellDescriptor"/> belongs to - drives shell-specific
/// behavior (cwd-sync bootstrap injection, path quoting) elsewhere in the terminal subsystem.</summary>
public enum ShellFamily
{
    Cmd,
    WindowsPowerShell,
    PowerShellCore,
    Bash,
    Wsl
}

/// <summary>
/// Replaces the old two-value <c>ShellType</c> enum, which could not express pwsh vs. Windows
/// PowerShell, Git Bash, or one entry per installed WSL distribution. <see cref="Id"/> is the
/// stable identity persisted to settings (<c>"cmd"</c>, <c>"powershell"</c>, <c>"pwsh"</c>,
/// <c>"gitbash"</c>, <c>"wsl:Ubuntu-22.04"</c>).
/// </summary>
public sealed record ShellDescriptor(
    string Id,
    string DisplayNameKey,
    string? DisplayNameArg,
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    ShellFamily Family);

/// <summary>Well-known <see cref="ShellDescriptor.Id"/> values for the built-in shells.</summary>
public static class ShellIds
{
    public const string Cmd = "cmd";
    public const string WindowsPowerShell = "powershell";
    public const string PowerShellCore = "pwsh";
    public const string GitBash = "gitbash";

    /// <summary>Prefix for a per-distro WSL id: <c>"wsl:" + distroName</c>.</summary>
    public const string WslPrefix = "wsl:";

    /// <summary>Extracts the distro name from a WSL shell id (<c>"wsl:Ubuntu-22.04"</c> -&gt;
    /// <c>"Ubuntu-22.04"</c>). Falls back to the id unchanged if it's missing the expected prefix
    /// (defensive only - every id actually produced by <see cref="ShellCatalog"/> has it).</summary>
    public static string DistroNameFromShellId(string shellId) =>
        shellId.StartsWith(WslPrefix, StringComparison.Ordinal) ? shellId[WslPrefix.Length..] : shellId;
}
