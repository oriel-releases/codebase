using System.ComponentModel;
using System.Runtime.InteropServices;
using Oriel.Core.Models;
using Microsoft.Win32;

namespace Oriel.Core.Wallpaper;

public sealed class WindowsWallpaperSetter : IWallpaperSetter
{
    public IReadOnlyList<MonitorInfo> GetMonitors() => Sta.Run(() =>
    {
        var dw = Native.CreateDesktopWallpaper();
        try
        {
            var result = new List<MonitorInfo>();
            var count = dw.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                var path = dw.GetMonitorDevicePathAt(i);
                try
                {
                    var r = dw.GetMonitorRECT(path);
                    if (r.Right - r.Left > 0 && r.Bottom - r.Top > 0)
                        result.Add(new MonitorInfo(path, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
                }
                catch (COMException)
                {
                    // Detached monitors are still listed; GetMonitorRECT fails for them.
                }
            }
            return (IReadOnlyList<MonitorInfo>)result;
        }
        finally
        {
            Marshal.ReleaseComObject(dw);
        }
    });

    public void SetForAll(string imagePath, WallpaperFit fit)
    {
        EnsureExists(imagePath);
        var (style, tile) = fit switch
        {
            WallpaperFit.Fill => ("10", "0"),
            WallpaperFit.Fit => ("6", "0"),
            WallpaperFit.Stretch => ("2", "0"),
            WallpaperFit.Tile => ("0", "1"),
            WallpaperFit.Center => ("0", "0"),
            WallpaperFit.Span => ("22", "0"),
            _ => ("10", "0"),
        };
        using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true))
        {
            key?.SetValue("WallpaperStyle", style);
            key?.SetValue("TileWallpaper", tile);
        }

        if (!Native.SystemParametersInfo(Native.SPI_SETDESKWALLPAPER, 0, imagePath,
                Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SystemParametersInfo failed to set the wallpaper.");
    }

    public bool SetForAllDesktops(string imagePath)
    {
        EnsureExists(imagePath);
        return VirtualDesktops.TrySetWallpaperForAll(imagePath);
    }

    public void SetForMonitor(string monitorDevicePath, string imagePath, WallpaperFit fit)
    {
        EnsureExists(imagePath);
        Sta.Run(() =>
        {
            var dw = Native.CreateDesktopWallpaper();
            try
            {
                // Position is global in Windows; Span doesn't make sense per monitor, use Fill instead.
                dw.SetPosition(ToPosition(fit == WallpaperFit.Span ? WallpaperFit.Fill : fit));
                dw.SetWallpaper(monitorDevicePath, imagePath);
            }
            finally
            {
                Marshal.ReleaseComObject(dw);
            }
            return 0;
        });
    }

    public string? GetCurrent(string? monitorDevicePath = null) => Sta.Run(() =>
    {
        var dw = Native.CreateDesktopWallpaper();
        try
        {
            var path = dw.GetWallpaper(monitorDevicePath);
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch (COMException)
        {
            // S_FALSE-ish case: monitors have different wallpapers and no monitor was given.
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(dw);
        }
    });

    static Native.DesktopWallpaperPosition ToPosition(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Center => Native.DesktopWallpaperPosition.Center,
        WallpaperFit.Tile => Native.DesktopWallpaperPosition.Tile,
        WallpaperFit.Stretch => Native.DesktopWallpaperPosition.Stretch,
        WallpaperFit.Fit => Native.DesktopWallpaperPosition.Fit,
        WallpaperFit.Span => Native.DesktopWallpaperPosition.Span,
        _ => Native.DesktopWallpaperPosition.Fill,
    };

    static void EnsureExists(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Wallpaper image is missing.", imagePath);
    }
}

/// <summary>Runs COM work on a dedicated STA thread so callers can be on any thread.</summary>
static class Sta
{
    public static T Run<T>(Func<T> work)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return work();

        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (error is not null) throw error;
        return result;
    }
}

static class Native
{
    public const uint SPI_SETDESKWALLPAPER = 0x0014;
    public const uint SPIF_UPDATEINIFILE = 0x01;
    public const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

    static readonly Guid CLSID_DesktopWallpaper = new("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD");

    public static IDesktopWallpaper CreateDesktopWallpaper() =>
        (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_DesktopWallpaper)!)!;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    public enum DesktopWallpaperPosition { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        Rect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(DesktopWallpaperPosition position);
        DesktopWallpaperPosition GetPosition();
        void SetSlideshow(IntPtr items);
        IntPtr GetSlideshow();
        void SetSlideshowOptions(uint options, uint slideshowTick);
        void GetSlideshowOptions(out uint options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, uint direction);
        uint GetStatus();
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }
}
