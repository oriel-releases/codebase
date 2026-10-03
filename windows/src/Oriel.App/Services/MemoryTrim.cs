using System.Runtime;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Oriel.UI.Services;

/// <summary>Gives memory back to Windows after the window closes, so the tray process stays small.</summary>
static class MemoryTrim
{
    public static void Now()
    {
        // Let the closed window's XAML objects finish tearing down before collecting.
        App.Dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
        });
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
}
