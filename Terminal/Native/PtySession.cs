using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using CoderCommander.Services;

namespace CoderCommander.Terminal.Native;

/// <summary>
/// Owns one ConPTY-backed shell process end to end: spawn, resize, read, write, and the
/// deadlock-sensitive ordered teardown. This is the only place in the terminal subsystem that
/// touches raw ConPTY handles - everything above it (the VT parser, the screen model, the
/// canvas) only ever sees <see cref="OutputReceived"/>'s bytes and calls <see cref="Write"/>.
/// </summary>
internal sealed class PtySession : IAsyncDisposable
{
    private readonly FileStream _outputStream;
    private readonly FileStream _inputStream;
    private readonly SafePseudoConsoleHandle _hpc;
    private readonly SafeProcessHandle _processHandle;
    private readonly Process _process;
    private readonly JobObject? _job;
    private readonly Thread _readerThread;
    private readonly object _writeLock = new();
    private readonly object _resizeLock = new();
    private volatile bool _closing;
    private volatile bool _readingStarted;
    private int _disposed;
    private short _cols;
    private short _rows;

    /// <summary>Raised on the dedicated reader thread - NOT the UI thread - every time a chunk of
    /// raw bytes arrives from the shell. Consumers (the UTF-8 decoder + VT parser, in later
    /// phases) are expected to run synchronously on this same thread; nothing here marshals to
    /// the UI. Never mutate UI controls directly from a handler.</summary>
    public event Action<ReadOnlyMemory<byte>>? OutputReceived;

    /// <summary>Raised once, from a background wait, when the shell process exits on its own
    /// (not as a result of <see cref="DisposeAsync"/>). The event data is the process exit code.</summary>
    public event Action<int>? Exited;

    public int ProcessId { get; }

    private PtySession(
        FileStream outputStream, FileStream inputStream, SafePseudoConsoleHandle hpc,
        SafeProcessHandle processHandle, Process process, JobObject? job, short cols, short rows)
    {
        _outputStream = outputStream;
        _inputStream = inputStream;
        _hpc = hpc;
        _processHandle = processHandle;
        _process = process;
        _job = job;
        _cols = cols;
        _rows = rows;
        ProcessId = process.Id;

        // NOT started here - see BeginReading(). Starting the reader thread inside the
        // constructor would race the caller's own OutputReceived subscription: for a fast
        // one-shot command (cmd /c echo ...), the shell can print and exit before Start()
        // even returns, and the very first output chunk - possibly the only one - would be
        // dispatched to a still-empty event with no subscribers and silently lost.
        _readerThread = new Thread(ReadLoop) { IsBackground = true, Name = $"pty-read-{process.Id}" };

        // Subscribe BEFORE EnableRaisingEvents to avoid missing a fast exit
        _process.Exited += OnProcessExited;
        _process.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Starts the reader thread. Callers MUST subscribe to <see cref="OutputReceived"/> (and
    /// <see cref="Exited"/>, if needed) BEFORE calling this - output that arrives before any
    /// subscriber is attached is lost, and for a fast-exiting command there may be no output
    /// left to receive after this call returns. Idempotent; safe to call at most meaningfully
    /// once.
    /// </summary>
    public void BeginReading()
    {
        if (_closing || _readingStarted) return;
        _readingStarted = true;
        _readerThread.Start();
    }

    /// <summary>
    /// Spawns <paramref name="executablePath"/> attached to a new pseudo console. Every failure
    /// path unwinds everything allocated so far before rethrowing - nothing is left dangling on
    /// a partial spawn.
    /// </summary>
    public static PtySession Start(
        string executablePath,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? extraEnvironment,
        short cols,
        short rows,
        IReadOnlyCollection<string>? excludeEnvironmentKeys = null)
    {
        cols = Math.Clamp(cols, (short)2, (short)1000);
        rows = Math.Clamp(rows, (short)1, (short)1000);

        var rollbacks = new Stack<Action>();
        try
        {
            // inputWrite/outputRead below: ownership moves into the FileStreams built further
            // down (see the comment there) or into the rollback stack on a failure path - the
            // analyzer can't see either destination from the out-var declaration site.
#pragma warning disable CA2000
            if (!ConPtyInterop.CreatePipe(out var inputRead, out var inputWrite, 0, 0))
                throw new PtyNativeException("CreatePipe (input)");
            rollbacks.Push(() => inputWrite.Dispose());
            // inputRead is handed to CreatePseudoConsole below and disposed immediately after -
            // it must NOT be added to the rollback stack under its own disposal, see below.

            if (!ConPtyInterop.CreatePipe(out var outputRead, out var outputWrite, 0, 0))
            {
                inputRead.Dispose();
                throw new PtyNativeException("CreatePipe (output)");
            }
            rollbacks.Push(() => outputRead.Dispose());
#pragma warning restore CA2000
            // outputWrite: same story as inputRead - consumed by CreatePseudoConsole, closed
            // immediately after, never separately rolled back.

            var hr = ConPtyInterop.CreatePseudoConsole(
                new ConPtyInterop.COORD { X = cols, Y = rows }, inputRead, outputWrite, 0, out var hpcRaw);

            // CreatePseudoConsole duplicates both handles internally. Closing our copies right
            // away is not optional cleanup - if outputWrite in particular stays open, the read
            // side never observes EOF once the client disconnects, and ClosePseudoConsole (which
            // blocks until the client disconnects AND the output is drained) deadlocks forever.
            // This is the single most common ConPTY deadlock, hence closing both unconditionally,
            // even on the failure path.
            inputRead.Dispose();
            outputWrite.Dispose();

            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);

            var hpc = new SafePseudoConsoleHandle(hpcRaw);
            rollbacks.Push(() => hpc.Dispose());

            var job = new JobObject();
            if (job.IsUsable)
                rollbacks.Push(() => job.Dispose());
            else
                job.Dispose();

            var pi = SpawnProcess(executablePath, arguments, workingDirectory, extraEnvironment, excludeEnvironmentKeys, hpc, job.IsUsable ? job : null);
            ConPtyInterop.CloseHandle(pi.hThread);

            // ownsHandle: true - this SafeProcessHandle is the ONE place pi.hProcess ever gets
            // closed, on both the success path (DisposeAsync -> _processHandle.Dispose()) and the
            // failure path (the rollback below). Do not also raw-CloseHandle(pi.hProcess)
            // anywhere else - SafeHandle release isn't reentrant-safe against a bare Win32
            // CloseHandle racing it on the same handle value.
            var processHandle = new SafeProcessHandle(pi.hProcess, ownsHandle: true);
            rollbacks.Push(() => processHandle.Dispose());

            var process = Process.GetProcessById(pi.dwProcessId);

            var outputStream = new FileStream(outputRead, FileAccess.Read, bufferSize: 1, isAsync: false);
            var inputStream = new FileStream(inputWrite, FileAccess.Write, bufferSize: 1, isAsync: false);

            // Ownership of outputRead/inputWrite has moved into the FileStreams above -
            // Dispose()ing the FileStream disposes the underlying handle. The rollback stack
            // still holds separate dispose closures over the same two SafeFileHandles from
            // lines above; that's intentionally left as-is rather than surgically removed, since
            // SafeHandle.Dispose() is documented idempotent (a no-op once already closed) - a
            // rollback firing after the FileStream already owns/closed the handle is harmless.

            LogService.Info($"PtySession: started pid={pi.dwProcessId} \"{executablePath}\" ({cols}x{rows})");
            return new PtySession(outputStream, inputStream, hpc, processHandle, process, job.IsUsable ? job : null, cols, rows);
        }
        catch (Exception ex)
        {
            LogService.Error($"PtySession: spawn failed for \"{executablePath}\"", ex);
            while (rollbacks.Count > 0)
            {
                try { rollbacks.Pop()(); }
                catch { /* best-effort unwind */ }
            }
            throw;
        }
    }

    private static ConPtyInterop.PROCESS_INFORMATION SpawnProcess(
        string executablePath, IReadOnlyList<string> arguments, string? workingDirectory,
        IReadOnlyDictionary<string, string>? extraEnvironment, IReadOnlyCollection<string>? excludeEnvironmentKeys,
        SafePseudoConsoleHandle hpc, JobObject? job)
    {
        var commandLine = BuildCommandLine(executablePath, arguments).ToCharArray();
        Array.Resize(ref commandLine, commandLine.Length + 1); // CreateProcessW wants room to NUL-terminate/normalize in place

        var envPin = default(GCHandle);
        var jobArrayPin = default(GCHandle);
        var attrList = nint.Zero;
        var hpcRefTaken = false;
        var jobRefTaken = false;
        try
        {
            var envChars = BuildEnvironmentBlock(extraEnvironment, excludeEnvironmentKeys);
            envPin = GCHandle.Alloc(envChars, GCHandleType.Pinned);

            var attrCount = job != null ? 2 : 1;
            nint size = 0;
            ConPtyInterop.InitializeProcThreadAttributeList(0, attrCount, 0, ref size);
            // The call above is EXPECTED to return false with ERROR_INSUFFICIENT_BUFFER (122) -
            // that's how this API reports "here's the buffer size you need", not a failure.
            attrList = Marshal.AllocHGlobal(size);
            if (!ConPtyInterop.InitializeProcThreadAttributeList(attrList, attrCount, 0, ref size))
                throw new PtyNativeException("InitializeProcThreadAttributeList");

            hpc.DangerousAddRef(ref hpcRefTaken);
            if (!ConPtyInterop.UpdateProcThreadAttribute(
                    attrList, 0, ConPtyInterop.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    hpc.DangerousGetHandle(), IntPtr.Size, 0, 0))
                throw new PtyNativeException("UpdateProcThreadAttribute (PSEUDOCONSOLE)");

            if (job != null)
            {
                job.Handle.DangerousAddRef(ref jobRefTaken);
                var jobHandles = new nint[] { job.Handle.DangerousGetHandle() };
                jobArrayPin = GCHandle.Alloc(jobHandles, GCHandleType.Pinned);
                // lpValue for JOB_LIST is a POINTER TO an array of handles (unlike PSEUDOCONSOLE,
                // where the handle value itself IS lpValue) - both must stay valid until
                // CreateProcess returns, which the pins above + this method's own scope guarantee.
                if (!ConPtyInterop.UpdateProcThreadAttribute(
                        attrList, 0, ConPtyInterop.PROC_THREAD_ATTRIBUTE_JOB_LIST,
                        jobArrayPin.AddrOfPinnedObject(), IntPtr.Size * jobHandles.Length, 0, 0))
                {
                    // Not fatal - proceed without the belt-and-braces job-list attribute; the
                    // fallback AssignProcessToJobObject after CreateProcess still applies.
                    LogService.Warning($"PtySession: UpdateProcThreadAttribute (JOB_LIST) failed (Win32 error {Marshal.GetLastWin32Error()})");
                }
            }

            var si = new ConPtyInterop.STARTUPINFOEXW
            {
                StartupInfo = new ConPtyInterop.STARTUPINFOW
                {
                    // The EX struct's size, not STARTUPINFOW's - a very easy mistake that
                    // produces either ERROR_INVALID_PARAMETER or, worse, a process that starts
                    // but silently ignores the pseudo console attribute.
                    cb = Marshal.SizeOf<ConPtyInterop.STARTUPINFOEXW>()
                },
                lpAttributeList = attrList
            };
            // Deliberately NOT setting STARTF_USESTDHANDLES / hStd* - the pseudo console
            // attribute installs the correct std handles on its own; overriding them here would
            // fight it.

            var creationFlags = ConPtyInterop.EXTENDED_STARTUPINFO_PRESENT | ConPtyInterop.CREATE_UNICODE_ENVIRONMENT;

            // bInheritHandles: false. This is a security requirement, not a style choice - the
            // pseudo console attribute supplies correct std handles regardless of this flag, so
            // there is no functional need for true. Passing true would leak every inheritable
            // handle already open in THIS process (archive streams, in-flight copy-operation file
            // handles, locked files) into an interactive shell the user can type into.
            if (!ConPtyInterop.CreateProcess(
                    null, commandLine, 0, 0, bInheritHandles: false, creationFlags,
                    envPin.AddrOfPinnedObject(), workingDirectory, ref si, out var pi))
                throw new PtyNativeException("CreateProcess");

            if (job != null)
                job.TryAssign(pi.hProcess); // fallback/belt-and-braces if JOB_LIST above didn't take

            return pi;
        }
        finally
        {
            if (hpcRefTaken) hpc.DangerousRelease();
            if (jobRefTaken) job!.Handle.DangerousRelease();
            if (attrList != nint.Zero)
            {
                ConPtyInterop.DeleteProcThreadAttributeList(attrList);
                Marshal.FreeHGlobal(attrList);
            }
            if (jobArrayPin.IsAllocated) jobArrayPin.Free();
            if (envPin.IsAllocated) envPin.Free();
        }
    }

    /// <summary>
    /// Standard Win32 command-line quoting (the same algorithm CommandLineToArgvW parses) - now a
    /// shared helper (<see cref="Utils.Win32ArgumentQuoting"/>) since Services/ExternalToolLauncher
    /// needs the identical, correct escaping for launching a user-configured external viewer/editor
    /// and used to hand-roll a different (and buggy) one.
    /// </summary>
    internal static string BuildCommandLine(string executablePath, IReadOnlyList<string> arguments) =>
        Utils.Win32ArgumentQuoting.BuildCommandLine(executablePath, arguments);

    /// <summary>
    /// Builds a CREATE_UNICODE_ENVIRONMENT-compatible block: "KEY=VALUE\0" pairs, sorted
    /// case-insensitively by key (Windows documents this as required, not just tidy), terminated
    /// by an extra trailing NUL. Starts from this process's own environment and layers
    /// <paramref name="extra"/> on top, so callers only need to specify what they're adding or
    /// overriding (TERM, COLORTERM, shell-integration variables, ...).
    /// </summary>
    /// <param name="exclude">Keys removed entirely rather than blanked - e.g.
    /// CODERCOMMANDER_UI_DEBUG must never reach an interactive shell the user can inspect with
    /// "set"/"$env:", not merely be emptied.</param>
    private static char[] BuildEnvironmentBlock(IReadOnlyDictionary<string, string>? extra, IReadOnlyCollection<string>? exclude = null)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            var key = (string)e.Key;
            if (key.Length > 0)
                merged[key] = (string?)e.Value ?? "";
        }
        if (exclude != null)
        {
            foreach (var key in exclude)
                merged.Remove(key);
        }
        if (extra != null)
        {
            foreach (var (k, v) in extra)
                merged[k] = v;
        }

        var sb = new StringBuilder();
        foreach (var (k, v) in merged)
            sb.Append(k).Append('=').Append(v).Append('\0');
        sb.Append('\0');
        return sb.ToString().ToCharArray();
    }

    /// <summary>Writes raw bytes to the shell's stdin. Safe to call from any thread; serialized
    /// against concurrent writers and against teardown.</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return;
        lock (_writeLock)
        {
            if (_closing) return;
            try
            {
                _inputStream.Write(data);
                _inputStream.Flush();
            }
            catch (IOException) { /* pipe gone - session is exiting/exited, nothing to do */ }
            catch (ObjectDisposedException) { /* stream disposed - session is exiting/exited, nothing to do */ }
        }
    }

    /// <summary>Resizes the pseudo console. Clamped to [2,1000]x[1,1000] - never 0 in either
    /// dimension, which some clients (and ConPTY itself) mishandle - and a no-op if the size
    /// didn't actually change, to avoid needlessly churning the client's screen buffer on every
    /// minor layout pass.</summary>
    public void Resize(short cols, short rows)
    {
        cols = Math.Clamp(cols, (short)2, (short)1000);
        rows = Math.Clamp(rows, (short)1, (short)1000);

        lock (_resizeLock)
        {
            if (_closing || (cols == _cols && rows == _rows))
                return;

            var refTaken = false;
            try
            {
                _hpc.DangerousAddRef(ref refTaken);
                var hr = ConPtyInterop.ResizePseudoConsole(_hpc.DangerousGetHandle(), new ConPtyInterop.COORD { X = cols, Y = rows });
                if (hr != 0)
                {
                    LogService.Warning($"PtySession: ResizePseudoConsole failed, hr=0x{hr:X8}");
                    return;
                }
                _cols = cols;
                _rows = rows;
            }
            catch (ObjectDisposedException) { /* handle disposed - session is exiting/exited, nothing to do */ }
            finally
            {
                if (refTaken) _hpc.DangerousRelease();
            }
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[8192];
        try
        {
            int n;
            while (!_closing && (n = _outputStream.Read(buffer, 0, buffer.Length)) > 0)
                OutputReceived?.Invoke(buffer.AsMemory(0, n));
        }
        catch (Exception ex) when (_closing)
        {
            // Expected: CancelIoEx/handle teardown during DisposeAsync unblocks a pending Read
            // with an exception rather than a clean 0-byte EOF. Not an error once we're closing.
            _ = ex;
        }
        catch (Exception ex)
        {
            LogService.Error("PtySession: reader thread error", ex);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_closing) return; // exit triggered by our own teardown, not a spontaneous exit
        int code;
        try { code = _process.ExitCode; }
        catch { code = -1; }
        LogService.Info($"PtySession: process {ProcessId} exited with code {code}");
        Exited?.Invoke(code);
    }

    /// <summary>
    /// Ordered, deadlock-aware teardown - never run any part of this on the UI thread.
    /// <list type="number">
    /// <item>Signal closing, so racing Resize/Write calls and the reader's exception filter
    /// treat what follows as expected, not an error.</item>
    /// <item>Close stdin - a well-behaved shell sees EOF and exits voluntarily, keeping any
    /// trailing output well-formed instead of being killed mid-write.</item>
    /// <item>Wait up to 2s for the process to exit on its own.</item>
    /// <item>Still alive -&gt; kill via the job object (or a direct Kill as a fallback if the job
    /// was never usable), which takes the whole descendant tree with it.</item>
    /// <item>Close the pseudo console handle. This call BLOCKS until the client has fully
    /// disconnected, which can hang on Windows builds 1809-1903 even with a live reader - so it
    /// runs on a background thread under a 5s watchdog; if it doesn't return in time, the handle
    /// is deliberately abandoned rather than blocking app shutdown (process teardown reclaims it).</item>
    /// <item>Cancel the pending read (if any) so the reader thread can exit, then join it.</item>
    /// <item>Dispose the remaining handles.</item>
    /// </list>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _closing = true;
        _process.Exited -= OnProcessExited;

        lock (_writeLock)
        {
            try { _inputStream.Dispose(); } catch { /* best effort */ }
        }

        try
        {
            using var exitWait = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            if (!_process.HasExited)
                await _process.WaitForExitAsync(exitWait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* still alive after 2s - fall through to kill */ }
        catch (Exception ex) { LogService.Warning($"PtySession: wait-for-exit error: {ex.Message}"); }

        if (!SafeHasExited())
        {
            try
            {
                if (_job is { IsUsable: true })
                    _job.Dispose(); // KILL_ON_JOB_CLOSE takes the whole tree down
                else
                    _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) { LogService.Warning($"PtySession: kill error: {ex.Message}"); }
        }
        else
        {
            _job?.Dispose();
        }

        var closeTask = Task.Run(() => _hpc.Dispose());
        if (await Task.WhenAny(closeTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false) != closeTask)
            LogService.Warning("PtySession: ClosePseudoConsole did not return within 5s - abandoning the handle (known ConPTY hang on Windows 1809-1903)");

        try { ConPtyInterop.CancelIoEx(SafeFileHandleOf(_outputStream), nint.Zero); }
        catch { /* best effort - if there's nothing pending this is a harmless no-op failure */ }

        // Only join if BeginReading() was ever called - Thread.Join on a never-started thread
        // throws ThreadStateException, and a session whose caller never subscribed/started
        // reading is a legitimate (if unusual) lifecycle to support.
        if (_readingStarted && !_readerThread.Join(TimeSpan.FromSeconds(2)))
            LogService.Warning($"PtySession: reader thread for pid {ProcessId} did not exit within 2s");

        try { _outputStream.Dispose(); } catch { /* best effort */ }
        try { _processHandle.Dispose(); } catch { /* best effort */ }
        _process.Dispose();

        LogService.Info($"PtySession: torn down pid={ProcessId}");
    }

    private bool SafeHasExited()
    {
        try { return _process.HasExited; }
        catch { return true; }
    }

    private static SafeFileHandle SafeFileHandleOf(FileStream fs) => fs.SafeFileHandle;
}

/// <summary>Thin <see cref="Exception"/> wrapper that captures the current Win32 error alongside
/// the failing API name, so <c>PtySession.Start</c>'s failures are diagnosable without a debugger.</summary>
internal sealed class PtyNativeException : Exception
{
    public int NativeErrorCode { get; }

    public PtyNativeException(string apiName)
        : base($"{apiName} failed (Win32 error {Marshal.GetLastWin32Error()})")
    {
        NativeErrorCode = Marshal.GetLastWin32Error();
    }
}
