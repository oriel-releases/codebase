using Microsoft.Win32;

namespace Oriel.Core.Wallpaper;

/// <summary>Whether the system is in dark mode, so the photo can suit it.</summary>
public interface ISystemTheme
{
    bool IsDark { get; }
}

/// <summary>Windows' "Choose your app mode" (Settings > Personalization > Colors).</summary>
public sealed class WindowsSystemTheme : ISystemTheme
{
    public bool IsDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
