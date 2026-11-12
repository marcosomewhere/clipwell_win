using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Merkautomat.Services;

/// <summary>
/// Windows job object with "kill on close": processes assigned to it end together with Merkautomat, also after a
/// crash or a forced termination, so a helper process can never keep running or lock its files.
/// </summary>
internal static class ChildProcessJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;
    private static readonly Lazy<IntPtr> Job = new(Create);

    public static void Assign(Process process)
    {
        var job = Job.Value;
        if (job == IntPtr.Zero) return;
        if (!AssignProcessToJobObject(job, process.Handle))
            App.LogCrash(new Win32Exception(Marshal.GetLastWin32Error(), "Hilfsprozess konnte nicht an Merkautomat gebunden werden."));
    }

    /// <summary>Ends helper processes started from <paramref name="executable"/> that are not <paramref name="keep"/>.</summary>
    public static int KillOrphans(string executable, Process? keep = null)
    {
        var killed = 0;
        foreach (var candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (candidate)
            {
                try
                {
                    if (keep != null && candidate.Id == keep.Id) continue;
                    if (!string.Equals(candidate.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) continue;
                    candidate.Kill(entireProcessTree: true);
                    candidate.WaitForExit(3000);
                    killed++;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        return killed;
    }

    private static IntPtr Create()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = { LimitFlags = JobObjectLimitKillOnJobClose } };
        var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var pointer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, pointer, (uint)length)) return IntPtr.Zero;
        }
        finally { Marshal.FreeHGlobal(pointer); }
        // The handle stays open for the lifetime of the process; Windows closes it on exit and ends the job.
        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
}
