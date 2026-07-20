namespace CoderCommander.Models;

/// <summary>
/// Supported shell types for terminal sessions.
/// </summary>
public enum ShellType
{
    /// <summary>Windows Command Prompt (cmd.exe)</summary>
    Cmd = 0,

    /// <summary>Windows PowerShell (powershell.exe)</summary>
    PowerShell = 1
}

/// <summary>
/// Extensions for ShellType enum.
/// </summary>
public static class ShellTypeExtensions
{
    /// <summary>Get the executable name for the shell type.</summary>
    public static string GetExecutableName(this ShellType type) =>
        type == ShellType.Cmd ? "cmd.exe" : "powershell.exe";

    /// <summary>Get the display name for the shell type.</summary>
    public static string GetDisplayName(this ShellType type) =>
        type == ShellType.Cmd ? "Command Prompt" : "PowerShell";

    /// <summary>Parse shell type from string.</summary>
    public static ShellType Parse(string? value) =>
        value switch
        {
            "PowerShell" => ShellType.PowerShell,
            "cmd.exe" => ShellType.Cmd,
            _ => ShellType.Cmd
        };

    /// <summary>Convert to string for serialization.</summary>
    public static string ToSerializableString(this ShellType type) =>
        type == ShellType.Cmd ? "cmd.exe" : "PowerShell";
}
