using Oriel.Core.Models;

namespace Oriel.Core.Wallpaper;

public interface IWallpaperSetter
{
    /// <summary>Active monitors, in physical pixels.</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>Same image on every monitor (SystemParametersInfo).</summary>
    void SetForAll(string imagePath, WallpaperFit fit);

    /// <summary>
    /// Same image on every virtual desktop, not just the current one. Best effort:
    /// false when this Windows build doesn't expose a way to do it.
    /// </summary>
    bool SetForAllDesktops(string imagePath);

    /// <summary>One image for one monitor (IDesktopWallpaper).</summary>
    void SetForMonitor(string monitorDevicePath, string imagePath, WallpaperFit fit);

    /// <summary>Current wallpaper file for a monitor, or the global one when null.</summary>
    string? GetCurrent(string? monitorDevicePath = null);
}
