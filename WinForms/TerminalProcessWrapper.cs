using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Utils;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CoderCommander.WinForms;

/// <summary>
/// Wrapper around a terminal process (cmd.exe or powershell.exe) with I/O redirection.
/// Handles process lifecycle, command execution, and output streaming.
/// </summary>
public sealed class TerminalProcessWrapper : IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    // cmd.exe reads/writes its redirected (piped) stdin/stdout using the system's real OEM
    // codepage, NOT whatever chcp is run inside the session - chcp only affects a real
    // console device, which a fully-redirected child process doesn't have. Forcing UTF-8 via
    // chcp 65001 on redirected pipes was tested and silently does nothing for input, and
    // corrupts output. PowerShell is unaffected by this: it sets [Console]::InputEncoding/
    // OutputEncoding itself via managed code, which works correctly over pipes.
    private static readonly Encoding CmdOemEncoding = Encoding.GetEncoding((int)GetOEMCP());

    // Recent Windows builds emit a UTF-8 BOM (EF BB BF) once at the very start of a redirected
    // cmd.exe session's stdout, regardless of the OEM codepage actually used for the rest of
    // the stream. Decoded through CmdOemEncoding this shows up as a few garbage characters
    // before the first prompt. Precompute what that garbage looks like so it can be stripped.
    private static readonly string CmdBomArtifact = CmdOemEncoding.GetString(new byte[] { 0xEF, 0xBB, 0xBF });

    private Process? _process;
    private StreamWriter? _processInput;
    private CancellationTokenSource _readCts = new();
    private readonly object _lockObj = new();
    private bool _disposed;
    private bool _stdoutBomChecked;

    // Neither shell can be told to stop echoing typed input back into a redirected pipe
    // (cmd.exe's "@echo off" corrupts non-ASCII I/O over pipes - see CmdOemEncoding above;
    // PowerShell has no equivalent switch at all). Instead we track the exact text of the
    // last command we sent and strip the shell's own "prompt+command" echo line for it from
    // the next output, so the panel shows only real command output. This has to scan across
    // read chunks and across more than one line: a chunk boundary can land mid-line, and a
    // shell can flush its idle prompt as its own earlier write before our command's echo
    // arrives as a separate chunk with no newline of its own preceding it.
    private string? _pendingEchoCommand;
    private readonly StringBuilder _echoScanBuffer = new();
    private readonly StringBuilder _echoScanKept = new();
    private int _echoScanLinesSeen;

    /// <summary>Gets the shell type this wrapper manages.</summary>
    public ShellType ShellType { get; }
    /// <summary>Gets or sets the current working directory of the terminal session.</summary>
    public string CurrentPath { get; set; }
    /// <summary>Gets whether the underlying process is alive and not disposed.</summary>
    public bool IsRunning => _process != null && !_disposed && !_process.HasExited;

    /// <summary>Raised when the process writes to standard output.</summary>
    public event EventHandler<string>? OutputReceived;
    /// <summary>Raised when the process writes to standard error.</summary>
    public event EventHandler<string>? ErrorReceived;
    /// <summary>Raised when the process exits (normally or due to termination).</summary>
    public event EventHandler? ProcessExited;
    /// <summary>Raised when the process state changes (start, terminate).</summary>
    public event EventHandler? StateChanged;

    /// <summary>Initialize terminal process wrapper.</summary>
    public TerminalProcessWrapper(ShellType shellType, string workingDirectory = "")
    {
        ShellType = shellType;
        CurrentPath = string.IsNullOrEmpty(workingDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : workingDirectory;
    }

    /// <summary>Start the terminal process.</summary>
    public bool Start()
    {
        if (_disposed || IsRunning)
            return false;

        try
        {
            // Validate shell availability
            if (!ShellValidator.IsShellAvailable(ShellType))
            {
                LogService.Error($"{ShellType.GetExecutableName()} is not available");
                ErrorReceived?.Invoke(this, $"Error: {ShellType.GetExecutableName()} not found in PATH\r\n");
                return false;
            }

            // Validate working directory
            CurrentPath = ShellValidator.ValidateOrDefaultPath(CurrentPath);

            // PowerShell manages its own UTF-8 console encoding (set via -Command below) and
            // works correctly over redirected pipes. cmd.exe does not respect chcp for
            // redirected pipes at all - it always reads/writes using the system's real OEM
            // codepage, so we must match that instead of forcing UTF-8.
            var ioEncoding = ShellType == ShellType.PowerShell
                ? Encoding.GetEncoding("utf-8", new EncoderReplacementFallback("?"), new DecoderReplacementFallback("?"))
                : CmdOemEncoding;

            var psi = new ProcessStartInfo
            {
                FileName = ShellType.GetExecutableName(),
                WorkingDirectory = CurrentPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = ioEncoding,
                StandardErrorEncoding = ioEncoding
            };

            if (ShellType == ShellType.PowerShell)
            {
                // PowerShell: set both InputEncoding and OutputEncoding to UTF-8
                psi.Arguments = "-NoExit -NoProfile -Command \"[Console]::InputEncoding = [System.Text.Encoding]::UTF8; [Console]::OutputEncoding = [System.Text.Encoding]::UTF8\"";
            }
            else if (ShellType == ShellType.Cmd)
            {
                // cmd.exe: no arguments needed beyond keeping the session interactive.
                // Deliberately NOT using "@echo off" here: on redirected pipes it corrupts
                // non-ASCII (e.g. Cyrillic) input/output - empirically confirmed. Without it,
                // cmd.exe shows its normal "path>command" prompt/echo, which is standard
                // terminal behavior anyway.
                psi.Arguments = "/K";
            }

            _process = Process.Start(psi);
            if (_process == null)
                return false;

            // Match the writer's encoding to the shell (UTF-8 no-BOM for PowerShell, the
            // system OEM codepage for cmd.exe - see ioEncoding above).
            _processInput = new StreamWriter(_process.StandardInput.BaseStream,
                ShellType == ShellType.PowerShell ? new UTF8Encoding(false) : ioEncoding)
            {
                AutoFlush = false  // We'll flush manually after each command
            };

            // Start() can be called again after a prior Terminate() on the same wrapper (session
            // restart), so the field can already hold a CTS from a previous run - dispose it
            // before replacing rather than only ever disposing the very last one in Dispose().
            _readCts.Dispose();
            _readCts = new CancellationTokenSource();

            // Start reading output streams asynchronously
            _ = Task.Run(() => ReadStreamAsync(_process.StandardOutput, false), _readCts.Token);
            _ = Task.Run(() => ReadStreamAsync(_process.StandardError, true), _readCts.Token);

            // Monitor process exit. Capture the process locally so this task keeps
            // observing the process this Start() call created even if _process is
            // later nulled out by Dispose().
            var monitoredProcess = _process;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Run(() => monitoredProcess.WaitForExit());
                }
                catch (Exception ex)
                {
                    // Racing Dispose()/Terminate() disposing the same Process while this is
                    // blocked in WaitForExit() can throw (e.g. ObjectDisposedException/
                    // InvalidOperationException) - unguarded, that would only surface via the
                    // global TaskScheduler.UnobservedTaskException handler as a generic
                    // "UNOBSERVED TASK" crash-log entry with no indication it came from here.
                    LogService.Error("Error monitoring terminal process exit", ex);
                    return;
                }

                if (_disposed) return;
                ProcessExited?.Invoke(this, EventArgs.Empty);
            });

            StateChanged?.Invoke(this, EventArgs.Empty);
            LogService.Info($"Terminal process started: {ShellType.GetExecutableName()} in {CurrentPath}");
            if (ShellType == ShellType.PowerShell)
            {
                LogService.Info($"PowerShell: encoding=UTF-8, args={psi.Arguments}");
            }
            else if (ShellType == ShellType.Cmd)
            {
                LogService.Info($"cmd.exe: encoding=OEM CP{CmdOemEncoding.CodePage}, args={psi.Arguments}");
            }
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to start terminal process ({ShellType})", ex);
            ErrorReceived?.Invoke(this, $"Error starting terminal: {ex.Message}\r\n");
            return false;
        }
    }

    /// <summary>Execute a command in the terminal.</summary>
    public void ExecuteCommand(string command)
    {
        if (_disposed || !IsRunning)
            return;

        try
        {
            if (_processInput == null)
                return;

            lock (_lockObj)
            {
                // Clean command: trim and remove control characters except tabs
                var cleanCmd = command.Trim();
                var cleaned = new string(cleanCmd.Where(c =>
                    c >= ' ' || c == '\t' || c == '\r' || c == '\n'
                ).ToArray());

                LogService.Info($"Sending: [{cleaned}]");

                _pendingEchoCommand = cleaned;

                // Write directly with proper encoding
                _processInput.WriteLine(cleaned);
                _processInput.Flush();
            }
        }
        catch (Exception ex)
        {
            LogService.Error("ExecuteCommand error", ex);
            ErrorReceived?.Invoke(this, $"Error: {ex.Message}\r\n");
        }
    }

    // Characters cmd.exe's line parser can act on even when they appear inside a double-quoted
    // argument (a well-documented cmd quirk - e.g. `cd "A & B"` still runs "B" as a separate
    // command). All of these are legal in Windows folder names, so a crafted folder entered via
    // the file panel could otherwise inject a command into the terminal with no keystroke from
    // the user - SetWorkingDirectory is called automatically whenever the active panel's path
    // changes. Rather than trying to escape them exactly right, skip the automatic "cd" for a
    // path containing any of them.
    private static readonly char[] CmdUnsafeChars = ['&', '|', '^', '%', '<', '>', '`', '!'];

    /// <summary>Change the working directory.</summary>
    public bool SetWorkingDirectory(string path)
    {
        if (!ShellValidator.IsPathAccessible(path))
            return false;

        CurrentPath = path;

        if (IsRunning)
        {
            if (TryBuildChangeDirectoryCommand(path, out var command))
                ExecuteCommand(command);
            else
                LogService.Warning($"Skipped terminal cwd sync: path contains characters unsafe for {ShellType.GetExecutableName()}: {path}");
        }

        return true;
    }

    private bool TryBuildChangeDirectoryCommand(string path, out string command)
    {
        if (ShellType == ShellType.PowerShell)
        {
            // A single-quoted PowerShell string is fully literal: no $(...) subexpression
            // expansion, no $variable interpolation, no backtick escapes - unlike the double
            // quotes used here previously, which do NOT stop PowerShell from expanding $(...) in
            // the string. -LiteralPath additionally disables wildcard interpretation of the path
            // itself. The only character that needs escaping is the quote delimiter.
            command = $"Set-Location -LiteralPath '{path.Replace("'", "''")}'";
            return true;
        }

        if (path.IndexOfAny(CmdUnsafeChars) >= 0)
        {
            command = "";
            return false;
        }

        command = $"cd /d \"{path}\"";
        return true;
    }

    /// <summary>Terminate the process.</summary>
    public void Terminate()
    {
        var process = _process;
        if (process == null)
            return;

        try
        {
            _readCts?.Cancel();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LogService.Error("Error terminating terminal process", ex);
        }
    }

    /// <summary>
    /// Scan (possibly across several read chunks) for a line ending in the last command we
    /// sent - both shells write the typed command back as part of the same line as the prompt
    /// (e.g. "C:\path&gt;cmd" for cmd.exe, "PS C:\path&gt; cmd" for PowerShell), so "line ends with
    /// what we sent" matches regardless of shell-specific prompt formatting - and drop that
    /// line. A shell can flush other lines (a blank line, a leftover idle prompt) before the
    /// real echo arrives, so a small number of non-matching lines are tolerated rather than
    /// giving up on the very first line. Returns null while still waiting for more data before
    /// a decision can be made; nothing should be emitted to the UI in that case.
    /// </summary>
    private string? ProcessPotentialEcho(string chunk)
    {
        if (_pendingEchoCommand == null && _echoScanBuffer.Length == 0 && _echoScanKept.Length == 0)
            return chunk; // fast path: nothing pending, don't touch normal output streaming

        _echoScanBuffer.Append(chunk);

        while (_pendingEchoCommand != null)
        {
            var buffered = _echoScanBuffer.ToString();
            var newlineIndex = buffered.IndexOf('\n');
            if (newlineIndex < 0)
                break; // no full line yet; wait for more data

            var rawLine = buffered[..(newlineIndex + 1)];
            var trimmedLine = buffered[..newlineIndex].TrimEnd('\r');
            _echoScanBuffer.Remove(0, newlineIndex + 1);

            if (trimmedLine.EndsWith(_pendingEchoCommand, StringComparison.Ordinal))
            {
                // Found the echo - drop the command text, but keep the line terminator (the
                // trailing \r\n/\n). The prompt preceding it on this same line was already
                // shown earlier with no newline of its own (shells leave the cursor sitting
                // right after the prompt, waiting for input); dropping the terminator too
                // would glue that old prompt directly onto whatever comes next.
                _echoScanKept.Append(rawLine[trimmedLine.Length..]);
                _pendingEchoCommand = null;
                _echoScanLinesSeen = 0;
                break;
            }

            _echoScanKept.Append(rawLine);
            if (++_echoScanLinesSeen >= 5)
            {
                _pendingEchoCommand = null; // give up looking; treat as real output instead
                _echoScanLinesSeen = 0;
            }
        }

        if (_echoScanBuffer.Length > 8192)
            _pendingEchoCommand = null; // safety valve against unbounded buffering

        if (_pendingEchoCommand != null)
            return null; // still scanning

        var result = _echoScanKept.Length > 0 ? _echoScanKept + _echoScanBuffer.ToString() : _echoScanBuffer.ToString();
        _echoScanKept.Clear();
        _echoScanBuffer.Clear();
        return result.Length > 0 ? result : null;
    }

    /// <summary>Read from a stream asynchronously.</summary>
    private async Task ReadStreamAsync(StreamReader stream, bool isError)
    {
        try
        {
            using (stream)
            {
                var buffer = new char[4096];
                int charsRead;
                var streamType = isError ? "stderr" : "stdout";
                LogService.Info($"Starting to read {streamType} from {ShellType.GetExecutableName()}");

                while (!_readCts.Token.IsCancellationRequested &&
                       (charsRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    var text = new string(buffer, 0, charsRead);

                    if (!isError)
                    {
                        // Recent Windows builds emit a UTF-8 BOM once at the very start of a
                        // redirected cmd.exe session's stdout regardless of OEM codepage; strip
                        // the resulting garbage prefix from the first chunk we see.
                        if (ShellType == ShellType.Cmd && !_stdoutBomChecked)
                        {
                            _stdoutBomChecked = true;
                            if (text.StartsWith(CmdBomArtifact, StringComparison.Ordinal))
                                text = text[CmdBomArtifact.Length..];
                        }

                        var processed = ProcessPotentialEcho(text);
                        if (processed == null)
                            continue; // still scanning for the echo line; nothing to show yet
                        text = processed;
                        if (text.Length == 0)
                            continue;
                    }

                    if (isError)
                        ErrorReceived?.Invoke(this, text);
                    else
                        OutputReceived?.Invoke(this, text);
                }

                LogService.Info($"Finished reading {streamType} from {ShellType.GetExecutableName()}");
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation during shutdown
        }
        catch (Exception ex)
        {
            LogService.Error("Error reading terminal stream", ex);
        }
    }

    /// <summary>Releases all resources, terminates the process, and disposes managed handles.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            Terminate();
            _readCts?.Dispose();
            _processInput?.Dispose();
            _process?.Dispose();
            _process = null;
        }
        catch (Exception ex)
        {
            LogService.Error("Error disposing terminal process", ex);
        }
    }
}
