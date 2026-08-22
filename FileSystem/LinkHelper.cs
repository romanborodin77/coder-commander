using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CoderCommander.FileSystem;

/// <summary>
/// Hard-link creation via P/Invoke - .NET's <see cref="File.CreateSymbolicLink(string, string)"/>/
/// <see cref="Directory.CreateSymbolicLink(string, string)"/> cover symbolic links natively since
/// .NET 6, but the BCL has no hard-link equivalent; <c>CreateHardLinkW</c> is the one Win32 call
/// this needs, same "P/Invoke is the only option" reasoning as <see cref="RecycleBinHelper"/>.
/// </summary>
public static class LinkHelper
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    /// <summary>
    /// Creates a hard link at <paramref name="linkPath"/> pointing at the same file content as
    /// <paramref name="existingFilePath"/>. Both paths must be real local (NativePaths) files on
    /// the same NTFS volume - hard links to a directory or across volumes are not something NTFS
    /// supports at all, not a permission the caller might be missing, so this deliberately does
    /// not attempt to translate that failure into anything friendlier than the OS's own error.
    /// </summary>
    /// <exception cref="Win32Exception">The OS call failed - caller shows <see cref="Exception.Message"/>.</exception>
    public static void CreateHardLink(string linkPath, string existingFilePath)
    {
        if (!CreateHardLinkW(linkPath, existingFilePath, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
