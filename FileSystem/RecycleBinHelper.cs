using System.Runtime.InteropServices;
using System.Text;

namespace CoderCommander.FileSystem;

/// <summary>
/// Windows Shell Recycle Bin operations via P/Invoke.
/// Used for "soft delete" — send to Recycle Bin instead of permanent removal.
/// </summary>
public static class RecycleBinHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    /// <summary>
    /// Sends one or more files/directories to the Recycle Bin.
    /// Returns true on success, false on failure.
    /// </summary>
    public static bool MoveToRecycleBin(string path)
    {
        return MoveToRecycleBin([path]);
    }

    /// <summary>
    /// Sends multiple files/directories to the Recycle Bin.
    /// </summary>
    /// <remarks>
    /// Called from a ThreadPool thread (see <c>DeleteOperation</c>), not one with a message pump.
    /// FOF_SILENT | FOF_NOERRORUI suppress the progress/error UI that would otherwise need one, which
    /// covers the common case; a handful of third-party shell extensions can still assume an STA
    /// apartment with a pump underneath SHFileOperationW. Known limitation - revisit with a dedicated
    /// STA worker thread if that's ever observed to cause real hangs.
    /// </remarks>
    public static bool MoveToRecycleBin(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return false;

        // The Recycle Bin doesn't exist for network locations at all. FOF_ALLOWUNDO below is a
        // request, not a guarantee: when the shell can't recycle an item (a UNC path is the most
        // common case; an oversized file or a disabled bin on a local drive are rarer ones this
        // check doesn't catch), it silently falls back to permanent deletion instead of failing -
        // and FOF_NOCONFIRMATION (needed because this runs off a thread pool thread with no
        // message pump, so we can't safely show FOF_WANTNUKEWARNING's confirmation dialog either)
        // suppresses the one warning that would otherwise say so. Without this check, a delete
        // the user expected to be recoverable via the Recycle Bin was actually permanent, with
        // this method still reporting success. Rejecting up front routes to DeleteOperation's
        // existing "Recycle Bin failed - confirm permanent delete?" fallback instead.
        if (paths.Any(p => p.StartsWith(@"\\", StringComparison.Ordinal)))
            return false;

        // Shell API requires double-null terminated string
        var sb = new StringBuilder();
        foreach (var p in paths)
            sb.Append(p).Append('\0');
        sb.Append('\0');

        var fromPtr = Marshal.StringToHGlobalUni(sb.ToString());
        try
        {
            var op = new SHFILEOPSTRUCTW
            {
                wFunc = FO_DELETE,
                pFrom = fromPtr,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
            };

            var result = SHFileOperationW(ref op);
            return result == 0 && !op.fAnyOperationsAborted;
        }
        finally
        {
            Marshal.FreeHGlobal(fromPtr);
        }
    }
}
