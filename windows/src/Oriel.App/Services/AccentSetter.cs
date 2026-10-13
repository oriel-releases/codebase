using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Oriel.Core.Wallpaper;

namespace Oriel.UI.Services;

/// <summary>
/// Changes the Windows accent color to follow the wallpaper. There is no public API: the colors live in the
/// registry (the same values Settings > Personalization > Colors writes), and a broadcast makes Windows reload them.
/// </summary>
public sealed class AccentSetter(IAccentSampler sampler, ILogger<AccentSetter> log) : IAccentSetter
{
    const string PersonalizeDesktop = @"Control Panel\Desktop";
    const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    const string DwmKey = @"Software\Microsoft\Windows\DWM";

    public async Task ApplyAsync(string imagePath)
    {
        try
        {
            if (await sampler.SampleAsync(imagePath) is not { } hex) return;
            var color = (R: Convert.ToByte(hex[1..3], 16), G: Convert.ToByte(hex[3..5], 16), B: Convert.ToByte(hex[5..7], 16));
            // Telling Windows makes every open app redraw its colors: only worth it for a change you can see.
            if (Current() is { } now && Math.Abs(now.R - color.R) + Math.Abs(now.G - color.G) + Math.Abs(now.B - color.B) < 24)
            {
                log.LogInformation("Accent color kept: the photo's is about the same");
                return;
            }
            SetColor(color);
            Broadcast();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't change the accent color");
        }
    }

    /// <summary>The accent color Windows has now (as we last set it), or null.</summary>
    static (byte R, byte G, byte B)? Current()
    {
        using var dwm = Registry.CurrentUser.OpenSubKey(DwmKey);
        if (dwm?.GetValue("AccentColor") is not int abgr) return null;
        return ((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
    }

    void SetColor((byte R, byte G, byte B) c)
    {
        // Manual color: turn the automatic one off so Windows doesn't overwrite ours.
        using (var desktop = Registry.CurrentUser.CreateSubKey(PersonalizeDesktop))
            desktop.SetValue("AutoColorization", 0, RegistryValueKind.DWord);

        var palette = new byte[32];
        var shades = AccentPicker.Palette(c);
        for (var i = 0; i < shades.Length; i++)
        {
            palette[i * 4] = shades[i].R;
            palette[i * 4 + 1] = shades[i].G;
            palette[i * 4 + 2] = shades[i].B;
        }

        var abgr = unchecked((int)(0xFF000000u | (uint)(c.B << 16) | (uint)(c.G << 8) | c.R));
        var argb = unchecked((int)(0xC4000000u | (uint)(c.R << 16) | (uint)(c.G << 8) | c.B));

        using (var accent = Registry.CurrentUser.CreateSubKey(AccentKey))
        {
            accent.SetValue("AccentPalette", palette, RegistryValueKind.Binary);
            accent.SetValue("AccentColorMenu", abgr, RegistryValueKind.DWord);
            accent.SetValue("StartColorMenu", abgr, RegistryValueKind.DWord);
        }
        using (var dwm = Registry.CurrentUser.CreateSubKey(DwmKey))
        {
            dwm.SetValue("AccentColor", abgr, RegistryValueKind.DWord);
            dwm.SetValue("ColorizationColor", argb, RegistryValueKind.DWord);
            dwm.SetValue("ColorizationAfterglow", argb, RegistryValueKind.DWord);
        }
        log.LogInformation("Accent color set from the photo: #{R:X2}{G:X2}{B:X2}", c.R, c.G, c.B);
    }

    static void Broadcast()
    {
        var hwndBroadcast = new IntPtr(0xFFFF);
        SendMessageTimeout(hwndBroadcast, 0x001A /* WM_SETTINGCHANGE */, UIntPtr.Zero, "ImmersiveColorSet",
            0x2 /* SMTO_ABORTIFHUNG */, 2000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint flags, uint timeout, out UIntPtr result);
}
