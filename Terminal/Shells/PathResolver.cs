namespace CoderCommander.Terminal.Shells;

/// <summary>
/// "Which"-style command resolution for user-configured custom shells
/// (<c>AppSettings.TerminalCustomShells</c>). The built-in shells (cmd, Windows PowerShell, pwsh,
/// Git Bash) deliberately do NOT go through this - <see cref="ShellCatalog"/> resolves them via
/// absolute, known install locations instead, specifically so a poisoned or user-writable PATH
/// entry can never substitute a different binary for one of them. This class exists only for the
/// custom-shell escape hatch, where the user is explicitly choosing what to run.
/// </summary>
internal static class PathResolver
{
    /// <summary>
    /// Resolves <paramref name="command"/> the way cmd.exe's own command lookup does: honors
    /// PATHEXT for an extension-less name, expands <c>%VAR%</c> references in each PATH entry,
    /// and - deliberately, unlike a shell's own builtin search - never searches the current
    /// working directory. Returns null if nothing matched.
    /// </summary>
    public static string? Which(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        if (Path.IsPathRooted(command))
            return File.Exists(command) ? command : TryWithPathExt(command);

        var pathExt = SplitPathExt();
        var hasExtension = Path.HasExtension(command);
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var rawEntry in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string dir;
            try
            {
                dir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rawEntry.Trim().Trim('"')));
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
            {
                continue; // a malformed PATH entry shouldn't abort the whole search
            }

            if (hasExtension)
            {
                var candidate = Path.Combine(dir, command);
                if (File.Exists(candidate))
                    return candidate;
                continue;
            }

            foreach (var ext in pathExt)
            {
                var candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string? TryWithPathExt(string rootedPathWithoutExtension)
    {
        if (Path.HasExtension(rootedPathWithoutExtension))
            return null;

        foreach (var ext in SplitPathExt())
        {
            var candidate = rootedPathWithoutExtension + ext;
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static string[] SplitPathExt() =>
        (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
        .Split(';', StringSplitOptions.RemoveEmptyEntries);
}
