using Microsoft.Win32.SafeHandles;

namespace CoderCommander.WinForms.Shell;

/// <summary>
/// Owns one absolute PIDL (<c>ITEMIDLIST</c>) returned by <c>SHParseDisplayName</c>, freeing it
/// via <c>ILFree</c> on dispose. A <see cref="SafeHandle"/> rather than a bare <see cref="IntPtr"/>
/// specifically so the free survives an exception thrown anywhere later in a shell-integration
/// pipeline (parse → bind → invoke) - the same reasoning <c>RemoteTls</c>/ConPTY handles in this
/// codebase already use <see cref="SafeHandle"/> for. <c>IntPtr.Zero</c> is a valid "no PIDL"
/// state (e.g. a path that failed to parse) and is treated as already-invalid, matching
/// <see cref="SafeHandleZeroOrMinusOneIsInvalid"/>'s own contract.
/// </summary>
internal sealed class SafePidlHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafePidlHandle() : base(ownsHandle: true) { }

    /// <summary>Wraps an already-obtained PIDL (e.g. from <c>SHParseDisplayName</c>).</summary>
    public SafePidlHandle(IntPtr pidl) : base(ownsHandle: true) => SetHandle(pidl);

    protected override bool ReleaseHandle()
    {
        ShellNative.ILFree(handle);
        return true;
    }
}
