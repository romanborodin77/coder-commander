namespace CoderCommander.WinForms.Shell;

/// <summary>Shared PIDL-parsing helper - used by <see cref="ExplorerHelper"/> and
/// <see cref="ShellContextMenuHost"/> alike, so there is exactly one place that calls
/// <c>SHParseDisplayName</c> and wraps the result in a <see cref="SafePidlHandle"/>.</summary>
internal static class ShellPaths
{
    /// <summary>Parses a real Windows path into an absolute PIDL, or <see langword="null"/> if the
    /// shell can't resolve it (e.g. a share that just went offline).</summary>
    public static SafePidlHandle? ParseDisplayName(string path)
    {
        var hr = ShellNative.SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _);
        return hr == 0 && pidl != IntPtr.Zero ? new SafePidlHandle(pidl) : null;
    }
}
