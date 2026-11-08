using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClipwellWin.Services;

/// <summary>How much of the machine a helper process may take.</summary>
public enum AiLoad
{
    /// <summary>Below-normal priority at full clock speed: foreground work of other programs always comes first.</summary>
    Normal,
    /// <summary>Idle priority plus EcoQoS: Windows prefers efficiency cores and low clock speeds for the process.</summary>
    Eco,
}

/// <summary>Applies <see cref="AiLoad"/> to a running process; priority and EcoQoS can change while it computes.</summary>
public static class ProcessLoad
{
    private const int ProcessPowerThrottling = 4;
    private const uint PowerThrottlingCurrentVersion = 1;
    private const uint PowerThrottlingExecutionSpeed = 0x1;

    public static void Apply(Process process, AiLoad load)
    {
        try
        {
            if (process.HasExited) return;
            process.PriorityClass = load == AiLoad.Eco ? ProcessPriorityClass.Idle : ProcessPriorityClass.BelowNormal;
            // Explicitly off for Normal, otherwise Windows may throttle the windowless process on its own.
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PowerThrottlingCurrentVersion,
                ControlMask = PowerThrottlingExecutionSpeed,
                StateMask = load == AiLoad.Eco ? PowerThrottlingExecutionSpeed : 0,
            };
            if (!SetProcessInformation(process.Handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>()))
                App.LogCrash(new Win32Exception(Marshal.GetLastWin32Error(), "EcoQoS konnte für die lokale KI nicht gesetzt werden."));
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, uint size);
}
