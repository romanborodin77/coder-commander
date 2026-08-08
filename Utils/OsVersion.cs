namespace CoderCommander.Utils;

/// <summary>OS-version gating for features with a hard Windows-build floor.</summary>
public static class OsVersion
{
    /// <summary>ConPTY (<c>CreatePseudoConsole</c>) was introduced in Windows 10 version 1809,
    /// build 17763. Older builds have no pseudo console API at all - the terminal panel falls
    /// back to a localized "unsupported OS version" message instead of attempting to spawn a
    /// shell.</summary>
    public const int MinConPtyBuild = 17763;

    public static bool IsConPtySupported => Environment.OSVersion.Version.Build >= MinConPtyBuild;
}
