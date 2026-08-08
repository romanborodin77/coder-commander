using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using CoderCommander.Services;

namespace CoderCommander.Terminal.Native;

/// <summary>
/// One Win32 Job Object per terminal session, configured with
/// <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>: when the job handle closes (including on an
/// unhandled crash of this process - Windows tears down job handles as part of normal process
/// cleanup), every process still assigned to the job is force-terminated. This is what
/// guarantees a shell - and anything it spawned - can never outlive the app, even if
/// <see cref="PtySession"/>'s own orderly teardown never runs.
/// <para>
/// Deliberately NOT set: <c>JOB_OBJECT_LIMIT_BREAKAWAY_OK</c> (children must not be able to
/// escape the job) and <c>ActiveProcessLimit</c> (a legitimate build can spawn dozens of
/// short-lived processes; capping that would be a false-positive footgun, not a security
/// control).
/// </para>
/// </summary>
internal sealed partial class JobObject : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeJobHandle CreateJobObject(nint lpJobAttributes, string? lpName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(
        SafeJobHandle hJob, int JobObjectInformationClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInformation, uint cbJobObjectInformationLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeJobHandle hJob, nint hProcess);

    public SafeJobHandle Handle { get; }

    /// <summary>True if the job object itself and its kill-on-close limit were both set up
    /// successfully. When false, the caller proceeds without job-based crash protection rather
    /// than failing the whole spawn - a session without the safety net is still strictly better
    /// than no session (e.g. running nested inside another restrictive job on some CI/sandbox
    /// hosts can make job creation or assignment fail).</summary>
    public bool IsUsable { get; }

    public JobObject()
    {
        Handle = CreateJobObject(0, null);
        if (Handle.IsInvalid)
        {
            LogService.Warning($"JobObject: CreateJobObject failed (Win32 error {Marshal.GetLastWin32Error()})");
            IsUsable = false;
            return;
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        var size = (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        if (!SetInformationJobObject(Handle, JobObjectExtendedLimitInformation, ref info, size))
        {
            LogService.Warning($"JobObject: SetInformationJobObject failed (Win32 error {Marshal.GetLastWin32Error()})");
            IsUsable = false;
            return;
        }

        IsUsable = true;
    }

    /// <summary>Assigns an already-running process to this job. Only meaningful as a fallback -
    /// the primary path is <c>PROC_THREAD_ATTRIBUTE_JOB_LIST</c> at process-creation time, which
    /// closes the brief window between CreateProcess and assignment during which a fast-exiting
    /// child could escape the job.</summary>
    public bool TryAssign(nint hProcess)
    {
        if (!IsUsable) return false;
        if (AssignProcessToJobObject(Handle, hProcess)) return true;
        LogService.Warning($"JobObject: AssignProcessToJobObject failed (Win32 error {Marshal.GetLastWin32Error()})");
        return false;
    }

    public void Dispose() => Handle.Dispose();
}

/// <summary>Zero-or-invalid SafeHandle for a Win32 job object handle.</summary>
internal sealed partial class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeJobHandle() : base(ownsHandle: true) { }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);

    protected override bool ReleaseHandle() => CloseHandle(handle);
}
