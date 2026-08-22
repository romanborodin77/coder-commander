using System.Runtime.InteropServices;

namespace CoderCommander.WinForms;

/// <summary>
/// Shows the Windows "Open With" picker via <c>SHOpenWithDialog</c> - the same dialog Explorer's
/// own "Open with…" context menu entry opens. Deliberately not the older
/// <c>rundll32 shell32.dll,OpenAs_RunDLL</c> trick: that shells out through a whole extra process
/// just to reach the identical dialog this API opens directly, in-process, with no argument
/// quoting to get wrong.
///
/// <para><b>Why no confirmation gate.</b> Every other place this app launches something outside
/// itself - <see cref="Services.ExternalToolLauncher"/>, <c>MainForm.OnItemActivated</c> running an
/// activated <c>.exe</c> - asks first, because it's <em>this app</em> deciding what to run.
/// "Open With" is the opposite shape: the user explicitly asked for the OS's own picker, and the
/// actual launch happens through <c>OAIF_EXEC</c> inside Explorer's own trusted dialog, not
/// through anything this app constructs - the same trust boundary a double-click in Explorer
/// itself already crosses with no extra prompt.</para>
/// </summary>
public static class OpenWithHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(IntPtr hwndParent, ref OpenAsInfo oaInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pcszFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pcszClass;
        public OpenAsInfoFlags oaifInFlags;
    }

    [Flags]
    private enum OpenAsInfoFlags
    {
        /// <summary>Show the "always use this app" checkbox, same as Explorer's own dialog.</summary>
        AllowRegistration = 0x00000001,
        /// <summary>Actually launch the chosen application once the user picks one, instead of
        /// only recording the association.</summary>
        Exec = 0x00000004,
    }

    /// <summary>HRESULT SHOpenWithDialog returns when the user cancels the picker - not a failure,
    /// nothing to report.</summary>
    private const int ErrorCancelledHResult = unchecked((int)0x800704C7);

    /// <summary>Opens the picker for <paramref name="filePath"/>, owned by <paramref name="ownerHwnd"/>.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The dialog itself failed to open -
    /// never thrown for a plain user cancel.</exception>
    public static void Show(IntPtr ownerHwnd, string filePath)
    {
        var info = new OpenAsInfo
        {
            pcszFile = filePath,
            pcszClass = null,
            oaifInFlags = OpenAsInfoFlags.AllowRegistration | OpenAsInfoFlags.Exec
        };

        var hr = SHOpenWithDialog(ownerHwnd, ref info);
        if (hr != 0 && hr != ErrorCancelledHResult)
            Marshal.ThrowExceptionForHR(hr);
    }
}
