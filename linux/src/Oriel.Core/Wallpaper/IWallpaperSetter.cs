using Oriel.Core.Models;

namespace Oriel.Core.Wallpaper;

public interface IWallpaperSetter
{
    /// <summary>Active monitors, in physical pixels. Empty when the desktop can't be asked.</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>Same image on every monitor (and every workspace the desktop keeps a separate wallpaper for).</summary>
    void SetForAll(string imagePath, WallpaperFit fit);

    /// <summary>One image for one monitor, where the desktop supports it (Sway). Elsewhere it sets them all.</summary>
    void SetForMonitor(string monitorDevicePath, string imagePath, WallpaperFit fit);

    /// <summary>The wallpaper file Oriel set last (desktops mostly can't be asked), or null if it's gone.</summary>
    string? GetCurrent(string? monitorDevicePath = null);
}
