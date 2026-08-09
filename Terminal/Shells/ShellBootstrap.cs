using System.Text;

namespace CoderCommander.Terminal.Shells;

/// <summary>
/// Injects the shell-side "report your cwd via OSC 7/9;9 on every prompt" bootstrap, so
/// <c>TerminalScreen.CwdReported</c> fires without any command-line parsing/polling. Two
/// mechanisms depending on family: an environment variable the shell's own prompt machinery
/// already reads (cmd's <c>PROMPT</c>, bash's <c>PROMPT_COMMAND</c>), or an extra startup argument
/// (PowerShell's <c>-EncodedCommand</c> wrapping <c>$function:prompt</c>). If the shell's prompt
/// never actually runs this (a completely silent/customized profile that overwrites
/// <c>PROMPT</c>/<c>PROMPT_COMMAND</c> again afterward), cwd sync for that tab just never fires -
/// there is no fallback guessing, by design.
/// </summary>
internal static class ShellBootstrap
{
    /// <summary>Extra environment variables to layer on top of the shell's inherited environment
    /// (on top of <c>TerminalSession</c>'s own TERM/COLORTERM). Empty for a family that uses
    /// <see cref="BuildExtraArguments"/> instead.</summary>
    public static IReadOnlyDictionary<string, string> BuildEnvironment(ShellFamily family) => family switch
    {
        ShellFamily.Cmd => BuildCmdEnvironment(),
        ShellFamily.Bash => BuildBashEnvironment(),
        ShellFamily.Wsl => BuildWslEnvironment(),
        _ => EmptyEnvironment
    };

    /// <summary>Extra arguments to append after <see cref="ShellDescriptor.Arguments"/>. Empty for
    /// a family that uses <see cref="BuildEnvironment"/> instead. <paramref name="loadProfile"/>
    /// only affects PowerShell families - <c>AppSettings.TerminalLoadShellProfile</c>.</summary>
    public static IReadOnlyList<string> BuildExtraArguments(ShellFamily family, bool loadProfile) => family switch
    {
        ShellFamily.WindowsPowerShell or ShellFamily.PowerShellCore => BuildPowerShellArguments(loadProfile),
        _ => Array.Empty<string>()
    };

    private static readonly IReadOnlyDictionary<string, string> EmptyEnvironment = new Dictionary<string, string>();

    private static IReadOnlyDictionary<string, string> BuildCmdEnvironment()
    {
        // cmd's PROMPT mini-language has a $-token for ESC ($E) but not for a raw BEL - simplest
        // to embed both control characters literally in the value; cmd emits anything that isn't
        // one of its own $-tokens verbatim. $P is cmd's own "current drive and path" token, so the
        // reported path is always live, not a value baked in once at spawn time. Prepended before
        // whatever PROMPT the user already had (or cmd's own "$P$G" default), never replacing it.
        var existing = Environment.GetEnvironmentVariable("PROMPT");
        var visiblePrompt = string.IsNullOrEmpty(existing) ? "$P$G" : existing;
        return new Dictionary<string, string>
        {
            ["PROMPT"] = $"\u001b]9;9;$P\u0007{visiblePrompt}"
        };
    }

    // Empty OSC 7 host ("file://" + path, not "file://" + host + path) is deliberate: CwdReport.
    // TryParseOsc7 only rejects a NON-empty, mismatched host, precisely so this trusted local
    // bootstrap never needs a WSL distro's hostname (which need not equal Environment.MachineName)
    // to be right.
    private const string BashPromptCommand = "printf '\\033]7;file://%s\\007' \"$(pwd)\"";

    private static IReadOnlyDictionary<string, string> BuildBashEnvironment() => new Dictionary<string, string>
    {
        ["PROMPT_COMMAND"] = BashPromptCommand
    };

    private static IReadOnlyDictionary<string, string> BuildWslEnvironment() => new Dictionary<string, string>
    {
        ["PROMPT_COMMAND"] = BashPromptCommand,
        // Governs which Windows-side environment variables wsl.exe imports into the Linux session -
        // without this, PROMPT_COMMAND never crosses the boundary. "/u" = Windows -> Linux only,
        // untranslated (PROMPT_COMMAND is a shell snippet, not a path, so no "p" translation flag).
        ["WSLENV"] = "PROMPT_COMMAND/u"
    };

    private static readonly string EncodedBootstrapScript =
        Convert.ToBase64String(Encoding.Unicode.GetBytes(PowerShellBootstrapScript));

    private static IReadOnlyList<string> BuildPowerShellArguments(bool loadProfile)
    {
        // -NoExit: stay interactive after running this. -NoProfile is what disabled PSReadLine/
        // tab-completion in the pre-rewrite pipe-based terminal when always applied - here it's
        // opt-in (AppSettings.TerminalLoadShellProfile) rather than baked in, since a heavy
        // profile (oh-my-posh, Starship) measurably slows down opening a tab and some users would
        // rather trade the customized prompt for that. When profiles load, this bootstrap only
        // wraps whatever prompt function they left behind - it never replaces the whole prompt.
        return loadProfile
            ? ["-NoExit", "-EncodedCommand", EncodedBootstrapScript]
            : ["-NoExit", "-NoProfile", "-EncodedCommand", EncodedBootstrapScript];
    }

    private const string PowerShellBootstrapScript = """
        $__ccPrevPrompt = $function:prompt
        function global:prompt {
            try {
                $__ccPath = $executionContext.SessionState.Path.CurrentLocation.Path
                [Console]::Out.Write("`e]9;9;$__ccPath`a")
            } catch {}
            & $__ccPrevPrompt
        }
        """;
}
