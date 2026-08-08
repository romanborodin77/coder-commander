using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CoderCommander.Terminal.Native;

/// <summary>
/// Raw Win32 P/Invoke surface for ConPTY (pseudo console) process creation and I/O.
/// <para>
/// Nothing here does any lifecycle management - see <see cref="PtySession"/> for the ordered
/// spawn/teardown sequence and the deadlock/handle-safety rules that sequence exists to satisfy.
/// This file is deliberately just the raw signatures plus the couple of struct-layout landmines
/// that are easy to get backwards.
/// </para>
/// </summary>
internal static partial class ConPtyInterop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFOW
    {
        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFOEXW
    {
        public STARTUPINFOW StartupInfo;
        public nint lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    internal const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    internal const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    internal const uint CREATE_SUSPENDED = 0x00000004;
    internal const nint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    internal const nint PROC_THREAD_ATTRIBUTE_JOB_LIST = 0x0002000D;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        nint lpPipeAttributes,
        uint nSize);

    // HRESULT, not BOOL - callers must Marshal.ThrowExceptionForHR the result.
    [LibraryImport("kernel32.dll")]
    internal static partial int CreatePseudoConsole(
        COORD size,
        SafeFileHandle hInput,
        SafeFileHandle hOutput,
        uint dwFlags,
        out nint phPC);

    // Also an HRESULT despite some headers documenting it as void - check it.
    [LibraryImport("kernel32.dll")]
    internal static partial int ResizePseudoConsole(nint hPC, COORD size);

    [LibraryImport("kernel32.dll")]
    internal static partial void ClosePseudoConsole(nint hPC);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeProcThreadAttributeList(
        nint lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref nint lpSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateProcThreadAttribute(
        nint lpAttributeList,
        uint dwFlags,
        nint attribute,
        nint lpValue,
        nint cbSize,
        nint lpPreviousValue,
        nint lpReturnSize);

    [LibraryImport("kernel32.dll")]
    internal static partial void DeleteProcThreadAttributeList(nint lpAttributeList);

    // Deliberately [DllImport], not [LibraryImport]: CreateProcessW can WRITE into
    // lpCommandLine (Windows normalizes/trims it in place). [LibraryImport]'s Utf16 string
    // marshalling passes a pinned pointer directly into the managed string's own buffer with no
    // copy - letting the OS write through that pointer would corrupt a managed (possibly
    // interned) string. A writable char[] scratch buffer sidesteps this entirely.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateProcessW")]
    internal static extern bool CreateProcess(
        string? lpApplicationName,
        [In, Out] char[] lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEXW lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    // I/O itself goes through FileStream wrapping the pipe SafeFileHandles (the well-established
    // pattern for ConPTY in .NET - anonymous pipes aren't overlapped-capable, so there's no async
    // win here over a dedicated blocking reader thread, and FileStream saves reimplementing
    // ReadFile/WriteFile error handling). CancelIoEx is kept as the one raw primitive still
    // needed: a backstop to unblock a reader thread parked in a blocking FileStream.Read() during
    // forced teardown, since closing the handle out from under a pending read is not a supported
    // pattern.
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CancelIoEx(SafeFileHandle hFile, nint lpOverlapped);
}
