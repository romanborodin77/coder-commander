namespace CoderCommander.Utils;

/// <summary>
/// Validates working-directory paths for terminal sessions. Shell availability/discovery now
/// lives in <see cref="CoderCommander.Terminal.Shells.ShellCatalog"/>, which resolves every
/// built-in shell through an absolute, known install location rather than scanning %PATH% (a
/// poisoned or user-writable PATH entry must not be able to substitute a different binary for
/// "PowerShell").
/// </summary>
public static class ShellValidator
{
    /// <summary>Check if a directory path is accessible.</summary>
    public static bool IsPathAccessible(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Ensure a directory is valid, or use default.</summary>
    public static string ValidateOrDefaultPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsPathAccessible(path))
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return path;
    }
}
