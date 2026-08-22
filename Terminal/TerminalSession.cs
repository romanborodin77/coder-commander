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
/// <para>
/// Two-phase initialization: <see cref="Create"/> builds the session infrastructure (screen,
/// parser, decoder) without spawning a shell. <see cref="StartPty"/> creates the ConPTY at the
/// canvas's actual size and starts reading — this guarantees the shell's initial output is
/// generated at the correct dimensions, preventing the startup text from being pushed to
/// scrollback on the first resize.
/// </para>
/// </summary>
internal sealed class TerminalSession : IAsyncDisposable
{
    private volatile PtySession? _pty;
    private readonly Utf8ChunkDecoder _decoder = new();
    private readonly char[] _decodeScratch = new char[4096];
    private readonly VtParser _parser = new();
    private int _cols;
    private int _rows;

    /// <summary>Mutable bridge: TerminalScreen captures this at construction, and StartPty wires
    /// it to the real PTY writer. This lets VtResponder send DA/CPR responses after the PTY is
    /// created.</summary>
    private sealed class PtyWriterBridge
    {
        private Action<byte[]> _write = _ => { };
        public void SetWriter(Action<byte[]> writer) => _write = writer;
        public void Write(byte[] data) => _write(data);
    }

    private readonly PtyWriterBridge _ptyWriterBridge = new();

    private readonly ShellDescriptor _shell;
    private readonly Func<string, string?>? _posixCwdTranslator;
    private readonly int _scrollbackLines;

    private volatile string _currentPath = "";
    private volatile bool _isExited;

    /// <summary>Set by <see cref="SuppressNextEcho"/>, consumed incrementally by
    /// <see cref="FeedToParser"/> - guarded by <see cref="Screen"/>'s own <c>SyncRoot</c>, the same
    /// lock <see cref="OnPtyOutput"/> already holds for every Screen/parser mutation.</summary>
    private string? _suppressedEchoRemaining;

    public Guid Id { get; } = Guid.NewGuid();
    public ShellDescriptor Shell => _shell;
    public TerminalScreen Screen { get; }
    public string CurrentPath { get => _currentPath; private set => _currentPath = value; }
    public bool IsExited { get => _isExited; private set => _isExited = value; }

    /// <summary>Tab display name - starts from the shell's localized display name, user-renamable
    /// afterward (right-click a tab header).</summary>
    public string Name { get; set; }

    /// <summary>Raised when the shell process exits on its own (not via <see cref="DisposeAsync"/>).</summary>
    public event Action<int>? Exited;

    private TerminalSession(ShellDescriptor shell, TerminalScreen screen, PtyWriterBridge ptyWriterBridge,
        string workingDirectory, int cols, int rows, int scrollbackLines, Func<string, string?>? posixCwdTranslator)
    {
        _shell = shell;
        Screen = screen;
        _ptyWriterBridge = ptyWriterBridge;
        CurrentPath = workingDirectory;
        Name = FormatDisplayName(shell);
        _cols = cols;
        _rows = rows;
        _scrollbackLines = scrollbackLines;
        _posixCwdTranslator = posixCwdTranslator;

        Screen.CwdReported += OnCwdReported;
    }

    /// <summary>Phase 1: create the session infrastructure (screen, parser, decoder) without
    /// spawning a shell. The PTY is created later by <see cref="StartPty"/> once the canvas
    /// reports its actual dimensions.</summary>
    public static TerminalSession Create(ShellDescriptor shell, string workingDirectory,
        int scrollbackLines)
    {
        // WSL's OSC 7 payload is a POSIX path ($(pwd) inside the distro), not a Windows one.
        // Git-for-Windows Bash also reports a POSIX path via OSC 7, using the /c/... convention.
        Func<string, string?>? posixCwdTranslator = null;
        if (shell.Family == ShellFamily.Wsl)
        {
            var mapper = new WslPathMapper(ShellIds.DistroNameFromShellId(shell.Id));
            posixCwdTranslator = posixPath => mapper.TryToWindows(posixPath, out var winPath) ? winPath : null;
        }
        else if (shell.Family == ShellFamily.Bash)
        {
            var mapper = new BashPathMapper();
            posixCwdTranslator = posixPath => mapper.TryToWindows(posixPath, out var winPath) ? winPath : null;
        }

        // Initial size 80x24 — will be corrected by StartPty before any output is read.
        const int initialCols = 80;
        const int initialRows = 24;
        var bridge = new PtyWriterBridge();
        var screen = new TerminalScreen(initialRows, initialCols, scrollbackLines,
            bridge.Write, posixCwdTranslator);

        return new TerminalSession(shell, screen, bridge, workingDirectory,
            initialCols, initialRows, scrollbackLines, posixCwdTranslator);
    }

    /// <summary>Phase 2: spawn the ConPTY at the canvas's actual size and start reading. Must be
    /// called after the canvas has been created and sized — this guarantees the shell's initial
    /// output (version string, copyright, prompt) is generated at the correct dimensions and is
    /// never pushed to scrollback on a subsequent resize.</summary>
    public void StartPty(int cols, int rows)
    {
        if (_pty != null) return; // already started — guard against double-call
        // DisposeAsync may have already run (closing the tab before BeginInvoke fired).
        // Without this check, we'd create a ConPTY that nobody will ever dispose — a leaked
        // process that survives until the app exits.
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0) return;

        var loadProfile = SettingsService.Load().TerminalLoadShellProfile;
        var arguments = _shell.Arguments.Concat(ShellBootstrap.BuildExtraArguments(_shell.Family, loadProfile)).ToList();

        var environment = new Dictionary<string, string>(ExtraEnvironment);
        foreach (var (key, value) in ShellBootstrap.BuildEnvironment(_shell.Family))
            environment[key] = value;

        var pty = PtySession.Start(
            _shell.ExecutablePath, arguments, CurrentPath, environment,
            (short)cols, (short)rows, ExcludedEnvironment);

        _pty = pty;
        _ptyWriterBridge.SetWriter(bytes => pty.Write(bytes));
        _cols = cols;
        _rows = rows;

        // Update screen to actual size (no scrollback push — screen is still empty).
        lock (Screen.SyncRoot)
            Screen.Resize(rows, cols);

        pty.OutputReceived += OnPtyOutput;
        pty.Exited += OnPtyExited;
        pty.BeginReading();
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

    /// <summary>Environment keys that must never reach an interactive shell the user can inspect
    /// (e.g. via "set"/"$env:") - the UI-automation test flag and the test sandbox's data-directory
    /// override chief among them.</summary>
    private static readonly IReadOnlyCollection<string> ExcludedEnvironment =
        [Services.DiagnosticCommandChannel.EnvironmentVariable, Services.DataDirectory.OverrideEnvironmentVariable];

    /// <summary>Sends raw bytes (already VT-encoded) to the shell's stdin.</summary>
    public void SendInput(ReadOnlySpan<byte> bytes) => _pty?.Write(bytes);

    /// <summary>
    /// Arms echo suppression for the next <paramref name="text"/> characters the shell echoes
    /// back - used by the panel-&gt;terminal cwd push (<c>EmbeddedTerminalPanel.TrySendCd</c>) so a
    /// <c>cd /d "..."</c> typed on the user's behalf doesn't visibly interrupt whatever they were
    /// looking at every time they click a different folder. Call this immediately before
    /// <see cref="SendInput"/> writes the corresponding command, with the command's own visible
    /// text (no trailing CR - the shell's own newline handling for Enter is left alone, so only
    /// the literal typed-looking text disappears, not the following blank line/new prompt).
    ///
    /// <para><b>Best-effort, not a real PTY feature.</b> A real console's echo is controlled by the
    /// child process reading with <c>ENABLE_ECHO_INPUT</c>, not by us as the "typist" - there is no
    /// way to tell the shell "don't echo this one line" without its cooperation. This instead
    /// matches the echoed characters as they stream back in <see cref="FeedToParser"/> and simply
    /// never hands them to the parser, so they never reach the visible screen buffer. If what comes
    /// back doesn't match (a race with real output, a shell that doesn't echo verbatim), matching
    /// stops immediately and whatever didn't match renders normally - a failed match degrades to
    /// today's fully-visible behavior, never to corrupted or dropped legitimate output.</para>
    /// </summary>
    public void SuppressNextEcho(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (Screen.SyncRoot)
            _suppressedEchoRemaining = text;
    }

    public void Resize(int cols, int rows)
    {
        if (cols == _cols && rows == _rows) return;
        _cols = cols;
        _rows = rows;
        // Called from the UI thread, races the reader thread's OnPtyOutput mutating the same
        // buffers - both must go through Screen.SyncRoot.
        lock (Screen.SyncRoot)
            Screen.Resize(rows, cols);
        _pty?.Resize((short)cols, (short)rows);
    }

    private void OnPtyOutput(ReadOnlyMemory<byte> bytes)
    {
        // The UI thread reads Screen concurrently (painting, cursor, scrollback) - every mutation
        // here must go through Screen.SyncRoot, which the UI layer takes around its own reads.
        lock (Screen.SyncRoot)
            _decoder.Decode(bytes.Span, _decodeScratch, FeedToParser);
    }

    /// <summary>Consumes a prefix of <paramref name="chars"/> that matches
    /// <see cref="_suppressedEchoRemaining"/> (see <see cref="SuppressNextEcho"/>) without handing
    /// it to <see cref="_parser"/>, then parses whatever's left normally. Always called with
    /// <see cref="Screen"/>.SyncRoot already held (from <see cref="OnPtyOutput"/>).</summary>
    private void FeedToParser(ReadOnlySpan<char> chars)
    {
        if (_suppressedEchoRemaining is { Length: > 0 } expected)
        {
            var matchLen = Math.Min(expected.Length, chars.Length);
            if (chars[..matchLen].SequenceEqual(expected.AsSpan(0, matchLen)))
            {
                _suppressedEchoRemaining = matchLen < expected.Length ? expected[matchLen..] : null;
                chars = chars[matchLen..];
            }
            else
            {
                // Doesn't match what we expected to see echoed back - give up rather than risk
                // swallowing real output; everything from here on renders normally.
                _suppressedEchoRemaining = null;
            }
        }

        if (!chars.IsEmpty)
            _parser.Parse(chars, Screen);
    }

    private void OnCwdReported(string path) => CurrentPath = path;

    private void OnPtyExited(int exitCode)
    {
        IsExited = true;
        Exited?.Invoke(exitCode);
    }

    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Screen.CwdReported -= OnCwdReported;
        if (_pty != null)
        {
            _pty.OutputReceived -= OnPtyOutput;
            _pty.Exited -= OnPtyExited;
            await _pty.DisposeAsync().ConfigureAwait(false);
        }
    }
}
