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
/// <para>
/// Also injects OSC 133 shell-integration prompt marks (A = prompt drawing, B = prompt idle/ready
/// for input, C = command just started) alongside the cwd report, which is what lets
/// <c>Terminal.Screen.TerminalScreen.IsAtIdlePrompt</c> replace the old "cursor sits in column 0"
/// heuristic with the shell's own word on whether it's safe to type a programmatic <c>cd</c> right
/// now. Every family gets A/B (they piggyback on the same prompt-render hook as the cwd report);
/// C needs a genuine preexec-style hook and isn't available everywhere - bash/WSL get it via
/// <c>PS0</c> (bash 4.4+), PowerShell via a PSReadLine Enter key handler, and cmd.exe doesn't get
/// it at all (no such hook exists), so <c>IsAtIdlePrompt</c> stays true for the whole time a
/// command runs under cmd - a documented, accepted v1 gap, not a bug to chase.
/// </para>
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
        // 133;A (prompt about to draw) wraps the existing 9;9 cwd report; 133;B (prompt fully
        // drawn, shell idle) follows the visible prompt text itself. cmd has no hook equivalent to
        // bash's PS0/PowerShell's Enter key handler below, so 133;C (command started) is never
        // emitted for this family - TerminalScreen.IsAtIdlePrompt stays true for the whole time a
        // command is running under cmd.exe. Documented, accepted v1 gap (see class doc comment).
        return new Dictionary<string, string>
        {
            ["PROMPT"] = $"\u001b]133;A\u0007\u001b]9;9;$P\u0007{visiblePrompt}\u001b]133;B\u0007"
        };
    }

    // Empty OSC 7 host ("file://" + path, not "file://" + host + path) is deliberate: CwdReport.
    // TryParseOsc7 only rejects a NON-empty, mismatched host, precisely so this trusted local
    // bootstrap never needs a WSL distro's hostname (which need not equal Environment.MachineName)
    // to be right. 133;A/133;B bracket it - PROMPT_COMMAND runs immediately before bash displays
    // PS1, so by the time it finishes the shell is at an idle, editable prompt.
    private const string BashPromptCommand = "printf '\\033]133;A\\007\\033]7;file://%s\\007\\033]133;B\\007' \"$(pwd)\"";

    // 133;C (command started) via PS0 (bash 4.4+), not a DEBUG trap: PS0 evaluates exactly once,
    // right after a complete interactively-submitted command line is read and before it executes -
    // a DEBUG trap instead fires for every simple command, including the printf inside
    // PROMPT_COMMAND itself, which would immediately flip the phase back out of Idle. \e/\a are
    // PS0's own backslash escapes for ESC/BEL (same expansion PS1 gets), so no printf/subshell
    // is needed here.
    private const string BashCommandStartMark = "\\e]133;C\\a";

    private static IReadOnlyDictionary<string, string> BuildBashEnvironment() => new Dictionary<string, string>
    {
        ["PROMPT_COMMAND"] = BashPromptCommand,
        ["PS0"] = BashCommandStartMark
    };

    private static IReadOnlyDictionary<string, string> BuildWslEnvironment()
    {
        // Read existing WSLENV to preserve user-configured forwarding (DISPLAY, WAYLAND_DISPLAY, etc.)
        var existing = Environment.GetEnvironmentVariable("WSLENV") ?? "";
        // A plain substring check would false-positive on a variable whose name merely contains
        // "PROMPT_COMMAND"/"PS0" (e.g. a user's own MY_PROMPT_COMMAND_VAR/u) and skip adding ours -
        // WSLENV is a ':'-separated "VAR/flags" list, so compare the name portion of each entry
        // exactly instead.
        var forwarded = existing.Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Split('/')[0])
            .ToHashSet(StringComparer.Ordinal);

        var toAdd = new[] { "PROMPT_COMMAND", "PS0" }.Where(name => !forwarded.Contains(name));
        // Governs which Windows-side environment variables wsl.exe imports into the Linux session -
        // without this, neither variable crosses the boundary. "/u" = Windows -> Linux only,
        // untranslated (both are shell snippets, not paths, so no "p" translation flag).
        var wslenv = string.Join(':', new[] { existing }.Where(s => s.Length > 0)
            .Concat(toAdd.Select(name => $"{name}/u")));

        return new Dictionary<string, string>
        {
            ["PROMPT_COMMAND"] = BashPromptCommand,
            ["PS0"] = BashCommandStartMark,
            ["WSLENV"] = wslenv
        };
    }

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
                [Console]::Out.Write([char]27 + "]133;A" + [char]7 + [char]27 + "]9;9;" + $PWD.Path + [char]7)
            } catch {}
            $__ccPromptText = & $__ccPrevPrompt
            try {
                [Console]::Out.Write([char]27 + "]133;B" + [char]7)
            } catch {}
            $__ccPromptText
        }
        # 133;C (command started): PSReadLine's own Enter handler is the only reliable "the user just
        # submitted a command line" hook PowerShell has - there is no PS0-equivalent preexec here.
        # PSReadLine is loaded by the host automatically even under -NoProfile, but wrapped in try/catch
        # anyway in case a restricted environment has it unavailable - silently skipping 133;C just
        # means IsAtIdlePrompt stays true through a running command for this session (same accepted
        # gap as cmd.exe), not a startup failure.
        try {
            Set-PSReadLineKeyHandler -Key Enter -ScriptBlock {
                [Console]::Out.Write([char]27 + "]133;C" + [char]7)
                [Microsoft.PowerShell.PSConsoleReadLine]::AcceptLine()
            }
        } catch {}
        """;
}
