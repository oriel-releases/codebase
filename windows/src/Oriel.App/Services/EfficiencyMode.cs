using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Oriel.UI.Services;

/// <summary>
/// Windows "efficiency mode" (EcoQoS): low priority, power-efficient cores. On while Oriel sits in
/// the tray or does a headless run, so it never competes with what the user is doing.
/// </summary>
static class EfficiencyMode
{
    const int ProcessPowerThrottling = 4;
    const uint PowerThrottlingCurrentVersion = 1;
    const uint PowerThrottlingExecutionSpeed = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            self.PriorityClass = enabled ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal;
            var state = new PowerThrottlingState
            {
                Version = PowerThrottlingCurrentVersion,
                ControlMask = PowerThrottlingExecutionSpeed,
                StateMask = enabled ? PowerThrottlingExecutionSpeed : 0,
            };
            SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>());
        }
        catch (Exception)
        {
            // Best effort: older Windows just runs at normal priority.
        }
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, uint size);
}
