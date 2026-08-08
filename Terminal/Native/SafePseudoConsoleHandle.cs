using Microsoft.Win32.SafeHandles;

namespace CoderCommander.Terminal.Native;

/// <summary>
/// Wraps an HPCON returned by <see cref="ConPtyInterop.CreatePseudoConsole"/>.
/// <para>
/// <b>Finalizer hazard:</b> <c>ClosePseudoConsole</c> blocks until the pty's client process has
/// disconnected from the console, which in turn requires the output pipe to have been fully
/// drained. If the GC ever runs this handle's finalizer while nothing is draining the output
/// pipe (e.g. the owning <see cref="PtySession"/> was dropped without an orderly
/// <c>DisposeAsync</c>), the finalizer THREAD blocks - stalling finalization for the entire
/// process, not just this handle. <see cref="PtySession"/> is the only intended owner and always
/// performs the ordered teardown (stdin EOF -> wait -> job kill -> close HPCON under a watchdog
/// -> drain to EOF) explicitly, with the reader thread still alive to see it through. This
/// SafeHandle exists purely as a last-resort leak guard, not as the primary close path.
/// </para>
/// </summary>
internal sealed class SafePseudoConsoleHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafePseudoConsoleHandle() : base(ownsHandle: true) { }

    public SafePseudoConsoleHandle(nint handle) : base(ownsHandle: true) => SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        ConPtyInterop.ClosePseudoConsole(handle);
        return true;
    }
}
