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
            SetColor((Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16)));
            Broadcast();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't change the accent color");
        }
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
