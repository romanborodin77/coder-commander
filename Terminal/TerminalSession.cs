using CoderCommander.Services;
using CoderCommander.Terminal.Native;
using CoderCommander.Terminal.Screen;
using CoderCommander.Terminal.Shells;
using CoderCommander.Terminal.Vt;

namespace CoderCommander.Terminal;

/// <summary>
/// Orchestrates one terminal tab end to end: spawns the shell via <see cref="PtySession"/>,
/// decodes its UTF-8 output with a persistent <see cref="Utf8ChunkDecoder"/>, feeds the decoded
/// characters through <see cref="VtParser"/> into a <see cref="TerminalScreen"/>, and exposes the
/// result for a UI layer to render. Deliberately free of any WinForms dependency - everything here
/// runs correctly headless, which is what makes it directly unit-testable.
/// </summary>
internal sealed class TerminalSession : IAsyncDisposable
{
    private readonly PtySession _pty;
    private readonly Utf8ChunkDecoder _decoder = new();
    private readonly char[] _decodeScratch = new char[4096];
    private readonly VtParser _parser = new();
    private int _cols;
    private int _rows;

    public Guid Id { get; } = Guid.NewGuid();
    public ShellDescriptor Shell { get; }
    public TerminalScreen Screen { get; }
    public string CurrentPath { get; private set; }
    public bool IsExited { get; private set; }

    /// <summary>Tab display name - starts from the shell's localized display name, user-renamable
    /// afterward (right-click a tab header).</summary>
    public string Name { get; set; }

    /// <summary>
    /// Raised on the pty's dedicated reader thread - NEVER the UI thread - every time a chunk of
    /// output has been parsed into <see cref="Screen"/>. Subscribers that touch WinForms controls
    /// MUST marshal via <c>Control.BeginInvoke</c>, never a synchronous <c>Invoke</c> - the same
    /// deadlock-on-teardown hazard <see cref="PtySession"/> itself documents applies here.
    /// </summary>
    public event Action? OutputArrived;

    /// <summary>Raised when the shell process exits on its own (not via <see cref="DisposeAsync"/>).</summary>
    public event Action<int>? Exited;

    private TerminalSession(PtySession pty, ShellDescriptor shell, TerminalScreen screen, string workingDirectory, int cols, int rows)
    {
        _pty = pty;
        Shell = shell;
        Screen = screen;
        CurrentPath = workingDirectory;
        Name = FormatDisplayName(shell);
        _cols = cols;
        _rows = rows;

        _pty.OutputReceived += OnPtyOutput;
        _pty.Exited += OnPtyExited;
        Screen.CwdReported += OnCwdReported;
    }

    private static string FormatDisplayName(ShellDescriptor shell)
    {
        var l = LocalizationService.Current;
        return shell.DisplayNameArg != null
            ? l.GetString(shell.DisplayNameKey, shell.DisplayNameArg)
            : l.GetString(shell.DisplayNameKey);
    }

    /// <summary>Environment variables layered onto the shell's inherited environment. TERM/COLORTERM
    /// tell shell-side tools (git, ls --color, PSReadLine) full VT capability is available.
    /// WT_SESSION is deliberately never set - some tools branch on its presence to assume Windows
    /// Terminal-specific behavior this app doesn't implement.</summary>
    private static readonly IReadOnlyDictionary<string, string> ExtraEnvironment = new Dictionary<string, string>
    {
        ["TERM"] = "xterm-256color",
        ["COLORTERM"] = "truecolor",
    };

    public static TerminalSession Start(ShellDescriptor shell, string workingDirectory, int cols, int rows, int scrollbackLines)
    {
        var loadProfile = SettingsService.Load().TerminalLoadShellProfile;
        var arguments = shell.Arguments.Concat(ShellBootstrap.BuildExtraArguments(shell.Family, loadProfile)).ToList();

        // ShellBootstrap's entries (cwd-report PROMPT/PROMPT_COMMAND injection) are layered on top
        // of the base TERM/COLORTERM set; a family with nothing to add contributes an empty dict.
        var environment = new Dictionary<string, string>(ExtraEnvironment);
        foreach (var (key, value) in ShellBootstrap.BuildEnvironment(shell.Family))
            environment[key] = value;

        var pty = PtySession.Start(
            shell.ExecutablePath, arguments, workingDirectory, environment,
            (short)cols, (short)rows);

        // WSL's OSC 7 payload is a POSIX path ($(pwd) inside the distro), not a Windows one - the
        // plain CwdReport interpretation would just backslash-replace it and fail Directory.Exists
        // every time, silently breaking cwd sync for every WSL tab.
        Func<string, string?>? posixCwdTranslator = null;
        if (shell.Family == ShellFamily.Wsl)
        {
            var mapper = new WslPathMapper(ShellIds.DistroNameFromShellId(shell.Id));
            posixCwdTranslator = posixPath => mapper.TryToWindows(posixPath, out var winPath) ? winPath : null;
        }

        var screen = new TerminalScreen(rows, cols, scrollbackLines, bytes => pty.Write(bytes), posixCwdTranslator);
        var session = new TerminalSession(pty, shell, screen, workingDirectory, cols, rows);
        pty.BeginReading();
        return session;
    }

    /// <summary>Sends raw bytes (already VT-encoded) to the shell's stdin.</summary>
    public void SendInput(ReadOnlySpan<byte> bytes) => _pty.Write(bytes);

    public void Resize(int cols, int rows)
    {
        if (cols == _cols && rows == _rows) return;
        _cols = cols;
        _rows = rows;
        // Called from the UI thread, races the reader thread's OnPtyOutput mutating the same
        // buffers - both must go through Screen.SyncRoot.
        lock (Screen.SyncRoot)
            Screen.Resize(rows, cols);
        _pty.Resize((short)cols, (short)rows);
    }

    private void OnPtyOutput(ReadOnlyMemory<byte> bytes)
    {
        // The UI thread reads Screen concurrently (painting, cursor, scrollback) - every mutation
        // here must go through Screen.SyncRoot, which the UI layer takes around its own reads.
        lock (Screen.SyncRoot)
            _decoder.Decode(bytes.Span, _decodeScratch, chars => _parser.Parse(chars, Screen));
        OutputArrived?.Invoke();
    }

    private void OnCwdReported(string path) => CurrentPath = path;

    private void OnPtyExited(int exitCode)
    {
        IsExited = true;
        Exited?.Invoke(exitCode);
    }

    public async ValueTask DisposeAsync()
    {
        _pty.OutputReceived -= OnPtyOutput;
        _pty.Exited -= OnPtyExited;
        Screen.CwdReported -= OnCwdReported;
        await _pty.DisposeAsync().ConfigureAwait(false);
    }
}
