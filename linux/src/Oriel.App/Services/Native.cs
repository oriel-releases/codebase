using System.Runtime.InteropServices;

namespace Oriel.UI.Services;

/// <summary>The few libc calls that keep a mostly idle tray process small and polite.</summary>
static partial class Native
{
    /// <summary>Gives free heap memory back to the OS. The .NET GC frees objects, glibc keeps the pages unless told.</summary>
    public static void TrimMemory()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        if (!OperatingSystem.IsLinux()) return;
        try { malloc_trim(0); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { /* musl: nothing to trim */ }
    }

    /// <summary>Lowers this process's CPU priority (for short headless runs that shouldn't compete with what you're doing).</summary>
    public static void BeNice()
    {
        if (!OperatingSystem.IsLinux()) return;
        try { setpriority(0 /* PRIO_PROCESS */, 0, 10); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int malloc_trim(nuint pad);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int setpriority(int which, uint who, int prio);
}
