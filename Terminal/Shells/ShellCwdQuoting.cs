namespace CoderCommander.Terminal.Shells;

/// <summary>
/// Builds a <c>cd</c>-equivalent command line to push a path into a running shell (panel -&gt;
/// terminal cwd sync), per <see cref="ShellFamily"/>. This is command injection by construction -
/// the string this produces is written directly to the pty as if typed, followed by Enter - so
/// every path is validated first and every family's quoting is either provably closed (an escape
/// scheme with no way to break out of the string) or an outright rejection rather than a
/// best-effort escape.
/// </summary>
internal static class ShellCwdQuoting
{
    /// <summary>cmd.exe's <c>"..."</c> quoting does not reliably protect these from being
    /// re-interpreted by cmd's own command-line tokenizer (a well-known cmd.exe quirk: unlike a
    /// real shell, `&amp;`/`|`/`&lt;`/`&gt;`/`^` are handled by a layer that runs before/around
    /// argument quoting for many contexts) - rather than trying to escape them, a path containing
    /// any of these is rejected outright. `"` is included too: it is the one character that
    /// breaks the quoting this scheme depends on in the first place.</summary>
    private static readonly char[] CmdUnsafeChars = ['&', '|', '<', '>', '^', '"'];

    /// <summary>Sanity cap on path length - not a real-world limit (NTFS/long-path-aware .NET
    /// already allows much longer), just a guard against something absurd reaching a command
    /// line.</summary>
    private const int MaxPathLength = 8000;

    /// <summary>
    /// Builds a <c>cd</c> command line (including a trailing <c>\r</c> to submit it) for the given
    /// shell family. <paramref name="path"/> must already be in the form that shell expects - the
    /// Windows path for <see cref="ShellFamily.Cmd"/>/PowerShell, the already-translated POSIX
    /// path (via <see cref="WslPathMapper"/>) for <see cref="ShellFamily.Bash"/>/<see cref="ShellFamily.Wsl"/>.
    /// Returns false (nothing written) for anything that fails validation - never a best-effort
    /// partial command.
    /// </summary>
    public static bool TryBuildCd(ShellFamily family, string path, out string command)
    {
        command = "";
        if (string.IsNullOrWhiteSpace(path))
            return false;
        if (path.Length > MaxPathLength)
            return false;
        // A pty write only ever submits one logical line; any of these in the payload would
        // either submit a second, attacker/environment-controlled command or corrupt the pty's
        // own line-editing state.
        if (path.IndexOfAny(['\r', '\n', '\0']) >= 0)
            return false;

        switch (family)
        {
            case ShellFamily.Cmd:
                if (path.IndexOfAny(CmdUnsafeChars) >= 0)
                    return false;
                command = $"cd /d \"{path}\"\r";
                return true;

            case ShellFamily.WindowsPowerShell:
            case ShellFamily.PowerShellCore:
                command = $"Set-Location -LiteralPath '{path.Replace("'", "''", StringComparison.Ordinal)}'\r";
                return true;

            case ShellFamily.Bash:
            case ShellFamily.Wsl:
                command = $"cd -- '{EscapeSingleQuoted(path)}'\r";
                return true;

            default:
                return false;
        }
    }

    /// <summary>POSIX single-quote escaping: close the quote, emit an escaped literal quote,
    /// reopen the quote. Provably closed - there is no byte sequence that breaks out of the
    /// resulting string, unlike a denylist of "dangerous" characters.</summary>
    private static string EscapeSingleQuoted(string s) => s.Replace("'", "'\\''", StringComparison.Ordinal);

    /// <summary>
    /// Formats a single file/directory path for insertion into the terminal at the current cursor
    /// position (drag-and-drop, paste). The path is quoted per shell family to prevent injection.
    /// A trailing space is appended so the user can continue typing. Returns false if the path
    /// fails validation (too long, contains injection characters for cmd).
    /// </summary>
    public static bool TryFormatPathForInsertion(ShellFamily family, string path, out string formatted)
    {
        formatted = "";
        if (string.IsNullOrWhiteSpace(path))
            return false;
        if (path.Length > MaxPathLength)
            return false;
        if (path.IndexOfAny(['\r', '\n', '\0']) >= 0)
            return false;

        switch (family)
        {
            case ShellFamily.Cmd:
                if (path.IndexOfAny(CmdUnsafeChars) >= 0)
                    return false;
                formatted = $"\"{path}\" ";
                return true;

            case ShellFamily.WindowsPowerShell:
            case ShellFamily.PowerShellCore:
                formatted = $"'{path.Replace("'", "''", StringComparison.Ordinal)}' ";
                return true;

            case ShellFamily.Bash:
            case ShellFamily.Wsl:
                formatted = $"'{EscapeSingleQuoted(path)}' ";
                return true;

            default:
                return false;
        }
    }
}
