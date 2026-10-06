using System.Runtime.InteropServices;
using Oriel.Core.Platform;

namespace Oriel.UI.Services;

/// <summary>
/// The global "next wallpaper" shortcut. On X11 it grabs the key on the root window from one background thread
/// that sleeps in poll() until the key is pressed (no timers, no wakeups). Wayland doesn't let apps listen for
/// global keys; there the shortcut is made in the desktop's own keyboard settings, running <c>oriel --change-now</c>.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    readonly object _gate = new();
    readonly Queue<(Hotkey? Key, TaskCompletionSource<string?> Done)> _commands = new();
    Thread? _thread;
    int _wakeWrite = -1;
    bool _disposed;

    public event Action? Pressed;

    public static bool IsAvailable => OperatingSystem.IsLinux() && !DesktopEnvironment.IsWayland &&
                                      !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    /// <summary>Registers the shortcut (null clears it). Returns a note for the user when it couldn't be registered.</summary>
    public Task<string?> ApplyAsync(Hotkey? key)
    {
        if (!OperatingSystem.IsLinux()) return Task.FromResult<string?>(null);
        if (DesktopEnvironment.IsWayland)
            return Task.FromResult<string?>(key is null ? null :
                "Wayland doesn't let apps listen for keys. Add a shortcut in your system keyboard settings that runs: oriel --change-now");
        if (!IsAvailable)
            return Task.FromResult<string?>(key is null ? null : "No X11 display found for the shortcut.");

        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_disposed) return Task.FromResult<string?>(null);
            // Nothing to grab and no thread yet: don't start one just to do nothing.
            if (key is null && _thread is null) return Task.FromResult<string?>(null);
            _commands.Enqueue((key, done));
            if (_thread is null && !StartThread())
            {
                _commands.Clear();
                return Task.FromResult<string?>("Couldn't open the X11 display for the shortcut.");
            }
            Wake(); // the thread sleeps until poked, also for the very first command
        }
        return done.Task;
    }

    bool StartThread()
    {
        var ready = new ManualResetEventSlim();
        var ok = false;
        _thread = new Thread(() => Run(ready, v => ok = v)) { IsBackground = true, Name = "Oriel hotkey" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        if (!ok) _thread = null;
        return ok;
    }

    void Wake()
    {
        if (_wakeWrite < 0) return;
        byte b = 1;
        X.write(_wakeWrite, ref b, 1);
    }

    void Run(ManualResetEventSlim ready, Action<bool> started)
    {
        var display = X.XOpenDisplay(0);
        var pipeFds = new int[2];
        if (display == 0 || X.pipe(pipeFds) != 0)
        {
            if (display != 0) X.XCloseDisplay(display);
            started(false);
            ready.Set();
            return;
        }
        _wakeWrite = pipeFds[1];
        started(true);
        ready.Set();

        // Another program holding the key makes XGrabKey fail with an X error, which would kill the process by default.
        var grabFailed = false;
        X.XErrorHandler handler = (_, _) => { grabFailed = true; return 0; };
        X.XSetErrorHandler(handler);

        var root = X.XDefaultRootWindow(display);
        var xfd = X.XConnectionNumber(display);
        var ev = Marshal.AllocHGlobal(192); // XEvent is a 24-long union
        var poll = new X.PollFd[2];
        (int Code, uint Mods)[] grabbed = [];

        try
        {
            while (true)
            {
                while (X.XPending(display) > 0)
                {
                    X.XNextEvent(display, ev);
                    if (Marshal.ReadInt32(ev) == X.KeyPress) Pressed?.Invoke();
                }

                poll[0] = new X.PollFd { Fd = xfd, Events = X.POLLIN };
                poll[1] = new X.PollFd { Fd = pipeFds[0], Events = X.POLLIN };
                if (X.poll(poll, 2, -1) < 0 && Marshal.GetLastWin32Error() != X.EINTR) break;

                if ((poll[1].Revents & X.POLLIN) == 0) continue;
                byte drain;
                X.read(pipeFds[0], out drain, 1);

                while (true)
                {
                    (Hotkey? Key, TaskCompletionSource<string?> Done) command;
                    lock (_gate)
                    {
                        if (_disposed) return;
                        if (!_commands.TryDequeue(out command)) break;
                    }
                    foreach (var (code, mods) in grabbed) X.XUngrabKey(display, code, mods, root);
                    grabbed = [];
                    grabFailed = false;
                    command.Done.TrySetResult(command.Key is { } key ? Grab(display, root, key, ref grabbed, ref grabFailed) : null);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ev);
            X.XCloseDisplay(display);
            X.close(pipeFds[0]);
            X.close(pipeFds[1]);
            _wakeWrite = -1;
            GC.KeepAlive(handler);
        }
    }

    static string? Grab(nint display, nint root, Hotkey key, ref (int Code, uint Mods)[] grabbed, ref bool failed)
    {
        var keysym = X.XStringToKeysym(key.KeysymName);
        var code = keysym == 0 ? 0 : X.XKeysymToKeycode(display, keysym);
        if (code == 0) return $"The key {key.Key} isn't on this keyboard.";

        uint mask = 0;
        if ((key.Modifiers & Hotkey.Shift) != 0) mask |= X.ShiftMask;
        if ((key.Modifiers & Hotkey.Ctrl) != 0) mask |= X.ControlMask;
        if ((key.Modifiers & Hotkey.Alt) != 0) mask |= X.Mod1Mask;
        if ((key.Modifiers & Hotkey.Super) != 0) mask |= X.Mod4Mask;

        // Caps Lock and Num Lock change the modifier state, so grab every combination of them too.
        var list = new List<(int, uint)>();
        foreach (var extra in new uint[] { 0, X.LockMask, X.Mod2Mask, X.LockMask | X.Mod2Mask })
        {
            X.XGrabKey(display, code, mask | extra, root, 0, X.GrabModeAsync, X.GrabModeAsync);
            list.Add((code, mask | extra));
        }
        X.XSync(display, 0);
        grabbed = [.. list];
        if (failed)
        {
            foreach (var (c, m) in grabbed) X.XUngrabKey(display, c, m, root);
            grabbed = [];
            return $"{key} is already used by another program. Pick a different shortcut.";
        }
        return null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Wake();
        }
    }

    static class X
    {
        public const int KeyPress = 2, GrabModeAsync = 1, POLLIN = 1, EINTR = 4;
        public const uint ShiftMask = 1, LockMask = 2, ControlMask = 4, Mod1Mask = 8, Mod2Mask = 16, Mod4Mask = 64;

        public delegate int XErrorHandler(nint display, nint errorEvent);

        [StructLayout(LayoutKind.Sequential)]
        public struct PollFd { public int Fd; public short Events; public short Revents; }

        [DllImport("libX11.so.6")] public static extern nint XOpenDisplay(nint name);
        [DllImport("libX11.so.6")] public static extern int XCloseDisplay(nint display);
        [DllImport("libX11.so.6")] public static extern nint XDefaultRootWindow(nint display);
        [DllImport("libX11.so.6")] public static extern int XConnectionNumber(nint display);
        [DllImport("libX11.so.6")] public static extern int XPending(nint display);
        [DllImport("libX11.so.6")] public static extern int XNextEvent(nint display, nint ev);
        [DllImport("libX11.so.6")] public static extern int XSync(nint display, int discard);
        [DllImport("libX11.so.6")] public static extern nint XSetErrorHandler(XErrorHandler handler);
        [DllImport("libX11.so.6")] public static extern nint XStringToKeysym([MarshalAs(UnmanagedType.LPStr)] string name);
        [DllImport("libX11.so.6")] public static extern byte XKeysymToKeycode(nint display, nint keysym);
        [DllImport("libX11.so.6")] public static extern int XGrabKey(nint display, int keycode, uint modifiers, nint grabWindow, int ownerEvents, int pointerMode, int keyboardMode);
        [DllImport("libX11.so.6")] public static extern int XUngrabKey(nint display, int keycode, uint modifiers, nint grabWindow);

        [DllImport("libc", SetLastError = true)] public static extern int pipe(int[] fds);
        [DllImport("libc", SetLastError = true)] public static extern int poll([In, Out] PollFd[] fds, ulong count, int timeout);
        [DllImport("libc", SetLastError = true)] public static extern nint read(int fd, out byte b, nuint count);
        [DllImport("libc", SetLastError = true)] public static extern nint write(int fd, ref byte b, nuint count);
        [DllImport("libc", SetLastError = true)] public static extern int close(int fd);
    }
}
