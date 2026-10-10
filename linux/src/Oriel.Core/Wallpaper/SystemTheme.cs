using Oriel.Core.Platform;

namespace Oriel.Core.Wallpaper;

/// <summary>Whether the system is in dark mode, so the photo can suit it.</summary>
public interface ISystemTheme
{
    bool IsDark { get; }
}

/// <summary>
/// The desktop's dark style, through gsettings: the color-scheme setting (GNOME 42 and later, also read by many other
/// desktops), else a theme name with "dark" in it. Unknown means light.
/// </summary>
public sealed class LinuxSystemTheme : ISystemTheme
{
    public bool IsDark
    {
        get
        {
            if (!Proc.Has("gsettings")) return false;
            if (Proc.Run("gsettings", "get", "org.gnome.desktop.interface", "color-scheme") is { Ok: true } scheme &&
                scheme.Output.Contains("dark", StringComparison.OrdinalIgnoreCase)) return true;
            return Proc.Run("gsettings", "get", "org.gnome.desktop.interface", "gtk-theme") is { Ok: true } theme &&
                   theme.Output.Contains("dark", StringComparison.OrdinalIgnoreCase);
        }
    }
}
