using CoderCommander.Models;

namespace CoderCommander.Utils;

/// <summary>
/// Validates shell availability and accessibility.
/// </summary>
public static class ShellValidator
{
    /// <summary>Check if a shell executable is available in the system PATH.</summary>
    public static bool IsShellAvailable(ShellType shellType)
    {
        var exe = shellType.GetExecutableName();
        var pathEnv = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(pathEnv))
            return false;

        return pathEnv
            .Split(';')
            .Select(p => Path.Combine(p, exe))
            .Any(p => File.Exists(p));
    }

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

    /// <summary>Get all available shells on this system.</summary>
    public static List<ShellType> GetAvailableShells()
    {
        var available = new List<ShellType>();

        foreach (ShellType shellType in Enum.GetValues(typeof(ShellType)))
        {
            if (IsShellAvailable(shellType))
                available.Add(shellType);
        }

        return available;
    }

    /// <summary>Ensure a directory is valid, or use default.</summary>
    public static string ValidateOrDefaultPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsPathAccessible(path))
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return path;
    }
}
