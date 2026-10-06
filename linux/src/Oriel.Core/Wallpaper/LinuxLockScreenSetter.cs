using Microsoft.Extensions.Logging;
using Oriel.Core.Platform;

namespace Oriel.Core.Wallpaper;

/// <summary>
/// Lock screen picture. KDE Plasma has its own setting for it. GNOME and Cinnamon show the desktop wallpaper
/// on the lock screen themselves (older GNOME has a separate key, set here), so those need no extra step.
/// Other desktops don't let an app change it, and a display manager's greeter needs root: those say no.
/// </summary>
public sealed class LinuxLockScreenSetter(ILogger<LinuxLockScreenSetter> log) : ILockScreenSetter
{
    public Task<bool> TrySetAsync(string imagePath) => Task.FromResult(TrySet(imagePath));

    bool TrySet(string imagePath)
    {
        var uri = new Uri(imagePath).AbsoluteUri;
        switch (DesktopEnvironment.Current)
        {
            case Desktop.Kde:
                foreach (var tool in new[] { "kwriteconfig6", "kwriteconfig5" })
                {
                    if (!Proc.Has(tool)) continue;
                    var ok = true;
                    foreach (var key in new[] { "Image", "PreviewImage" })
                        ok &= Proc.Run(tool, "--file", "kscreenlockerrc", "--group", "Greeter", "--group", "Wallpaper",
                            "--group", "org.kde.image", "--group", "General", "--key", key, uri) is { Ok: true };
                    if (!ok) log.LogDebug("{Tool} couldn't write the lock screen picture", tool);
                    return ok;
                }
                return false;

            case Desktop.Gnome:
                // GNOME up to 3.38 has its own lock screen picture; newer ones reuse the desktop wallpaper.
                if (Proc.Run("gsettings", "list-keys", "org.gnome.desktop.screensaver") is { Ok: true } keys &&
                    keys.Output.Contains("picture-uri", StringComparison.Ordinal))
                    Proc.Run("gsettings", "set", "org.gnome.desktop.screensaver", "picture-uri", uri);
                return true;

            case Desktop.Cinnamon:
                return true;

            default:
                return false;
        }
    }
}
