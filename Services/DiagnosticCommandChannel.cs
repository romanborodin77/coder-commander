using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CoderCommander.Commands;
using CoderCommander.ViewModels;

namespace CoderCommander.Services;

/// <summary>
/// A named-pipe channel that invokes any registered <see cref="CommandEngine"/> command directly,
/// bypassing keyboard/mouse simulation entirely. Gated behind <c>CODERCOMMANDER_UI_DEBUG=1</c>,
/// the same switch <see cref="UiDumpService"/> already uses - never active in a normal end-user
/// launch.
///
/// <para><b>The problem this replaces.</b> Driving the app through simulated input for automated
/// testing has three independent failure modes, each hit and documented separately this audit
/// pass: modifier-combo keys (Ctrl+G, Ctrl+Shift+T) posted via <c>PostMessage</c> are empirically
/// unreliable; a menu's own dropdown items are not reliably visible to UIA while the popup is
/// open; and a lookup by menu-item name breaks the moment the UI language changes. All three
/// disappear at once by not going through input or the menu at all - a command has exactly one
/// stable identifier (<see cref="CommandIds"/>) regardless of hotkey, menu wording, or language.</para>
///
/// <para><b>Why a named pipe instead of the file-drop-and-poll shape <see cref="UiDumpService"/>
/// uses.</b> That shape fits a fire-and-forget snapshot (dump now, read whenever). This needs a
/// synchronous answer - did the command run, what happened if not - which is what a pipe's
/// request/response round trip gives directly, without a caller polling for a result file to
/// appear or racing a previous run's leftover one.</para>
///
/// <para><b>Pipe name is per-process</b> (<c>CoderCommander.Diagnostics.{pid}</c>), not a single
/// well-known name: more than one debug-mode instance can legitimately be running at once (a
/// manual <c>start_app()</c> session alongside a test run), and each must be addressable on its
/// own rather than racing the others for one shared pipe.</para>
///
/// <para><b>One connection per request.</b> No persistent session, no state carried between calls -
/// simpler to implement correctly and to reason about than a long-lived duplex conversation, at
/// the cost of one connection setup per call. For a diagnostic channel used a few times per test
/// rather than per frame, that cost is irrelevant.</para>
///
/// <para><b>Never invoke a command whose handler opens a modal dialog - confirmed by experiment,
/// not just reasoned about.</b> <c>MakeDir</c>, <c>Rename</c>, <c>ChangeDir</c>, <c>SelectGroup</c>,
/// <c>DeselectGroup</c>, <c>MultiRename</c>, <c>Copy</c>, <c>Move</c>, <c>Delete</c>, <c>Wipe</c>,
/// <c>PackFiles</c>, <c>UnpackFiles</c> and similar all call <c>Form.ShowDialog()</c> synchronously
/// from inside their handler. <see cref="InvokeOnUiThread"/> runs that handler via
/// <c>Control.Invoke</c>, which blocks this class's listener thread until the handler returns - and
/// the handler does not return until the dialog closes. A test driving that dialog would normally do
/// so over UIA from the very thread that is, in this scenario, itself blocked waiting for the pipe
/// response - nothing left free to click the dialog shut. The result is a hang, not an error;
/// verified directly against <c>MakeDir</c> (a plain <c>Task.Wait</c> with a 6s outer timeout around
/// a 3s inner one never completed). Commands that mutate state directly with no dialog -
/// <c>Refresh</c>, <c>ToggleHidden</c>, <c>ToggleFlatView</c>, <c>SelectAll</c>/<c>DeselectAll</c>/
/// <c>InvertSelection</c>, <c>SwapPanels</c>, <c>GoToParent</c>, <c>ToggleTerminal</c>,
/// <c>CloseTerminalTab</c> - are safe and are exactly what this channel is for; a dialog-opening
/// command must keep using real keyboard input plus
/// <c>PressUntilModalAppears</c>/<c>WaitForModal</c>, the pattern already in use before this channel
/// existed. <c>CreateTerminalTab</c> is a third case, worth calling out separately: its handler is
/// <c>async void</c> and awaits a shell-discovery scan before showing <c>SelectShellDialog</c>, so
/// it does NOT hang this channel - the handler returns (and this channel responds) before the
/// dialog exists, not after. That makes <c>Handled=true</c> from this command mean "the async
/// operation was started", not "the dialog is up", which is a confusing enough contract that it's
/// better left off invoke_command entirely and kept on real keyboard input like the dialog-opening
/// group.</para>
/// </summary>
public static class DiagnosticCommandChannel
{
    public const string EnvironmentVariable = "CODERCOMMANDER_UI_DEBUG";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Pipe name this process's channel listens on, if started. Written to the crash-safe
    /// temp log so an external tool that only knows the process id can still discover it without
    /// guessing the naming scheme.</summary>
    public static string PipeName { get; } = $"CoderCommander.Diagnostics.{Environment.ProcessId}";

    private static CancellationTokenSource? _cts;

    /// <summary>
    /// Starts the listener loop on a background thread. <paramref name="uiThread"/> is any live
    /// control on the UI thread - <see cref="Control.Invoke(Delegate)"/> on it is how each request
    /// reaches <paramref name="commands"/> safely, since <see cref="CommandEngine.Execute"/> and
    /// everything it touches (the ViewModels, the panels) has the same UI-thread affinity as the
    /// rest of WinForms.
    /// </summary>
    public static void Start(MainViewModel vm, Control uiThread)
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) != "1") return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var thread = new Thread(() => RunLoop(vm, uiThread, ct))
        {
            IsBackground = true,
            Name = "DiagnosticCommandChannel",
        };
        thread.Start();

        LogService.Info($"DiagnosticCommandChannel: listening on pipe '{PipeName}'");
    }

    public static void Stop() => _cts?.Cancel();

    private static void RunLoop(MainViewModel vm, Control uiThread, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Message, PipeOptions.Asynchronous);

                server.WaitForConnectionAsync(ct).GetAwaiter().GetResult();
                HandleOneRequest(server, vm, uiThread);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                // A client that connected and vanished (e.g. its own process killed mid-request) -
                // ordinary for a diagnostic channel driven by test infrastructure, not worth more
                // than a debug-level note. The loop starts a fresh pipe instance and keeps serving.
                LogService.Debug($"DiagnosticCommandChannel: connection error ({ex.GetType().Name})");
            }
            catch (Exception ex)
            {
                LogService.Warning($"DiagnosticCommandChannel: unexpected error ({ex.GetType().Name}): {ex.Message}");
            }
        }
    }

    private static void HandleOneRequest(NamedPipeServerStream server, MainViewModel vm, Control uiThread)
    {
        var requestJson = ReadMessage(server);
        var response = Dispatch(requestJson, vm, uiThread);
        WriteMessage(server, response);
    }

    private static string Dispatch(string requestJson, MainViewModel vm, Control uiThread)
    {
        DiagnosticRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<DiagnosticRequest>(requestJson, JsonOpts);
        }
        catch (JsonException ex)
        {
            return Serialize(new DiagnosticResponse(false, false, $"malformed request: {ex.Message}", null));
        }

        if (request is null || string.IsNullOrEmpty(request.Action))
            return Serialize(new DiagnosticResponse(false, false, "missing 'action'", null));

        try
        {
            return request.Action switch
            {
                "invoke" => Serialize(InvokeOnUiThread(request, vm, uiThread)),
                "list" => Serialize(new DiagnosticResponse(true, true, null, [.. vm.Commands.RegisteredCommands.OrderBy(c => c)])),
                _ => Serialize(new DiagnosticResponse(false, false, $"unknown action '{request.Action}'", null)),
            };
        }
        catch (Exception ex)
        {
            // A command handler throwing is already caught and logged inside CommandEngine.Execute
            // itself, which reports it as "handled" (the handler ran, it just failed) rather than
            // rethrowing - so reaching this catch means something outside that contract broke (the
            // marshal to the UI thread, deserialization edge cases). Reported rather than left to
            // hang the caller waiting on a response that will never come.
            return Serialize(new DiagnosticResponse(false, false, $"{ex.GetType().Name}: {ex.Message}", null));
        }
    }

    private static DiagnosticResponse InvokeOnUiThread(DiagnosticRequest request, MainViewModel vm, Control uiThread)
    {
        if (string.IsNullOrEmpty(request.CommandId))
            return new DiagnosticResponse(false, false, "missing 'commandId'", null);

        if (uiThread.IsDisposed)
            return new DiagnosticResponse(false, false, "main window is gone", null);

        // Invoke, not BeginInvoke: the caller is waiting for a definite answer (did a handler run),
        // and this method already runs on its own background thread - there is no UI-thread
        // re-entrancy risk to a synchronous call here, unlike calling Invoke from the UI thread.
        var handled = (bool)uiThread.Invoke(() => vm.Commands.Execute(request.CommandId!, request.Param));
        return new DiagnosticResponse(true, handled, handled ? null : $"no handler registered for '{request.CommandId}'", null);
    }

    private static string Serialize(DiagnosticResponse response) => JsonSerializer.Serialize(response, JsonOpts);

    /// <summary>Reads exactly one message. Message-mode pipes deliver one write as one read-able
    /// unit (possibly split across several <c>ReadAsync</c> calls if larger than the pipe's
    /// buffer), signalled by <c>IsMessageComplete</c> - a fixed-size buffer plus that flag is all
    /// that is needed, no length prefix to get wrong.</summary>
    private static string ReadMessage(NamedPipeServerStream server)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        do
        {
            var read = server.Read(chunk, 0, chunk.Length);
            if (read == 0) break;
            buffer.Write(chunk, 0, read);
        } while (!server.IsMessageComplete);

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static void WriteMessage(NamedPipeServerStream server, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        server.Write(bytes, 0, bytes.Length);
        server.Flush();
        server.WaitForPipeDrain();
    }

    private sealed record DiagnosticRequest(string? Action, string? CommandId, string? Param);

    private sealed record DiagnosticResponse(bool Ok, bool Handled, string? Error, string[]? Commands);
}
