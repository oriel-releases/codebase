using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Oriel.UI.Services;

/// <summary>A global shortcut: modifier flags (Win32 MOD_*) plus a virtual-key code.</summary>
public readonly record struct Hotkey(uint Modifiers, uint Key)
{
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8;

    static readonly (uint Vk, string Name)[] Named =
    [
        (0x20, "Space"), (0x21, "PageUp"), (0x22, "PageDown"), (0x23, "End"), (0x24, "Home"),
        (0x25, "Left"), (0x26, "Up"), (0x27, "Right"), (0x28, "Down"), (0x2D, "Insert"), (0x2E, "Delete"),
    ];

    /// <summary>Name for a key we allow as a shortcut, or null if it isn't one.</summary>
    public static string? KeyName(uint vk)
    {
        if (vk is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return $"F{vk - 0x6F}";
        foreach (var (code, name) in Named) if (code == vk) return name;
        return null;
    }

    static uint? KeyCode(string name)
    {
        for (uint vk = 0; vk < 0x100; vk++)
            if (KeyName(vk) is { } n && n.Equals(name, StringComparison.OrdinalIgnoreCase)) return vk;
        return null;
    }

    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        uint mods = 0;
        uint? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": mods |= Ctrl; break;
                case "alt": mods |= Alt; break;
                case "shift": mods |= Shift; break;
                case "win": mods |= Win; break;
                default: key = KeyCode(part); if (key is null) return null; break;
            }
        }
        return key is { } k && mods != 0 ? new Hotkey(mods, k) : null;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & Ctrl) != 0) parts.Add("Ctrl");
        if ((Modifiers & Alt) != 0) parts.Add("Alt");
        if ((Modifiers & Shift) != 0) parts.Add("Shift");
        if ((Modifiers & Win) != 0) parts.Add("Win");
        parts.Add(KeyName(Key) ?? $"0x{Key:X}");
        return string.Join("+", parts);
    }
}

/// <summary>
/// Registers one system-wide hotkey. It lives on its own thread with a message loop, so it keeps
/// working while the window is closed (Oriel then runs from the tray).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    const int HotkeyId = 0x0E1;
    const uint WM_HOTKEY = 0x0312, WM_APP = 0x8000, WM_QUIT = 0x0012, PM_NOREMOVE = 0, MOD_NOREPEAT = 0x4000;

    readonly ConcurrentQueue<(Hotkey? Key, TaskCompletionSource<string?> Result)> _requests = new();
    readonly ManualResetEventSlim _ready = new();
    Thread? _thread;
    uint _threadId;

    /// <summary>Raised on the hotkey thread.</summary>
    public event Action? Pressed;

    /// <summary>Makes <paramref name="key"/> the shortcut (null removes it). Returns an error message, or null.</summary>
    public Task<string?> ApplyAsync(Hotkey? key)
    {
        EnsureThread();
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _requests.Enqueue((key, result));
        PostThreadMessage(_threadId, WM_APP, 0, 0);
        return result.Task;
    }

    void EnsureThread()
    {
        if (_thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Oriel hotkey" };
        _thread.Start();
        _ready.Wait();
    }

    void Loop()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, PM_NOREMOVE); // creates this thread's message queue
        _ready.Set();

        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY)
                Pressed?.Invoke();
            else if (msg.message == WM_APP)
                while (_requests.TryDequeue(out var request))
                    request.Result.SetResult(Register(request.Key));
        }
        UnregisterHotKey(0, HotkeyId);
    }

    static string? Register(Hotkey? key)
    {
        UnregisterHotKey(0, HotkeyId);
        if (key is not { } k) return null;
        return RegisterHotKey(0, HotkeyId, k.Modifiers | MOD_NOREPEAT, k.Key)
            ? null
            : $"{k} is already used by another app. Pick a different shortcut.";
    }

    public void Dispose()
    {
        if (_thread is not null) PostThreadMessage(_threadId, WM_QUIT, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Msg
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int ptX, ptY;
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")] static extern int GetMessage(out Msg msg, nint hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PeekMessage(out Msg msg, nint hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}
