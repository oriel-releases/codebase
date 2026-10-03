using System.Runtime.InteropServices;

namespace Oriel.Core.Wallpaper;

/// <summary>
/// Windows 11 keeps a separate wallpaper per virtual desktop, and the public APIs
/// (SystemParametersInfo, IDesktopWallpaper) only change the current one. Explorer's
/// undocumented IVirtualDesktopManagerInternal can update them all at once.
/// Its IID and vtable change between Windows builds, so we only call a layout we've
/// verified. On any other build QueryService fails cleanly and we report false.
/// </summary>
static class VirtualDesktops
{
    static readonly Guid CLSID_ImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    static readonly Guid CLSID_VirtualDesktopManagerInternal = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");

    /// <summary>Sets the wallpaper on every virtual desktop. False when this Windows build isn't supported.</summary>
    public static bool TrySetWallpaperForAll(string imagePath) => Sta.Run(() =>
    {
        object? shell = null;
        IntPtr manager = IntPtr.Zero, hstring = IntPtr.Zero;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ImmersiveShell)!);
            if (shell is not IServiceProvider provider) return false;

            var service = CLSID_VirtualDesktopManagerInternal;
            var iid = typeof(IVirtualDesktopManagerInternal24H2).GUID;
            if (provider.QueryService(ref service, ref iid, out manager) != 0 || manager == IntPtr.Zero) return false;

            var vdm = (IVirtualDesktopManagerInternal24H2)Marshal.GetObjectForIUnknown(manager);
            Marshal.ThrowExceptionForHR(WindowsCreateString(imagePath, imagePath.Length, out hstring));
            vdm.UpdateWallpaperPathForAllDesktops(hstring);
            Marshal.ReleaseComObject(vdm);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (hstring != IntPtr.Zero) WindowsDeleteString(hstring);
            if (manager != IntPtr.Zero) Marshal.Release(manager);
            if (shell is not null) Marshal.ReleaseComObject(shell);
        }
    });

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    static extern int WindowsDeleteString(IntPtr hstring);

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid riid, out IntPtr obj);
    }

    // Windows 11 24H2+ layout (verified on build 26300). Only the slot order matters for the
    // methods we don't call, so their parameters are left as raw pointers.
    [ComImport, Guid("53F5CA0B-158F-4124-900C-057158060B27"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IVirtualDesktopManagerInternal24H2
    {
        int GetCount();
        void MoveViewToDesktop(IntPtr view, IntPtr desktop);
        bool CanViewMoveDesktops(IntPtr view);
        IntPtr GetCurrentDesktop();
        void GetDesktops(out IntPtr desktops);
        [PreserveSig] int GetAdjacentDesktop(IntPtr from, int direction, out IntPtr desktop);
        void SwitchDesktop(IntPtr desktop);
        void SwitchDesktopAndMoveForegroundView(IntPtr desktop);
        IntPtr CreateDesktop();
        void MoveDesktop(IntPtr desktop, int index);
        void RemoveDesktop(IntPtr desktop, IntPtr fallback);
        IntPtr FindDesktop(ref Guid id);
        void GetDesktopSwitchIncludeExcludeViews(IntPtr desktop, out IntPtr include, out IntPtr exclude);
        void SetDesktopName(IntPtr desktop, IntPtr name);
        void SetDesktopWallpaper(IntPtr desktop, IntPtr path);
        void UpdateWallpaperPathForAllDesktops(IntPtr path);
    }
}
