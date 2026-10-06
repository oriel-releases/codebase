using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Oriel.Core.Models;
using Oriel.Core.Platform;
using Oriel.Core.Services;

namespace Oriel.Core.Wallpaper;

/// <summary>
/// Sets the wallpaper by asking the desktop's own tool: gsettings (GNOME, Cinnamon, MATE, Budgie, Deepin),
/// plasma-apply-wallpaperimage (KDE Plasma), xfconf-query (Xfce), swaymsg (Sway), swww / hyprpaper (Hyprland),
/// pcmanfm (LXQt, LXDE), and feh, nitrogen, xwallpaper or hsetroot on a bare X11 window manager.
/// Nothing stays running: each change is one short command.
/// </summary>
public sealed partial class LinuxWallpaperSetter(AppPaths paths, ILogger<LinuxWallpaperSetter> log) : IWallpaperSetter
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        try
        {
            return DesktopEnvironment.Current switch
            {
                Desktop.Sway => SwayMonitors() is { Count: > 0 } s ? s : XrandrMonitors(),
                Desktop.Hyprland => HyprlandMonitors() is { Count: > 0 } h ? h : XrandrMonitors(),
                _ => XrandrMonitors(),
            };
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            log.LogDebug(ex, "Couldn't read the monitor layout");
            return [];
        }
    }

    public void SetForAll(string imagePath, WallpaperFit fit)
    {
        if (!File.Exists(imagePath)) throw new WallpaperException("That image file is gone from disk.");
        var desktop = DesktopEnvironment.Current;
        log.LogDebug("Setting wallpaper on {Desktop}", desktop);

        var ok = desktop switch
        {
            Desktop.Gnome => SetGSettings("org.gnome.desktop.background", imagePath, fit, both: true),
            Desktop.Cinnamon => SetGSettings("org.cinnamon.desktop.background", imagePath, fit, both: false),
            Desktop.Deepin => SetGSettings("com.deepin.wrap.gnome.desktop.background", imagePath, fit, both: false),
            Desktop.Mate => SetMate(imagePath, fit),
            Desktop.Kde => SetKde(imagePath, fit),
            Desktop.Xfce => SetXfce(imagePath, fit),
            Desktop.Sway => SetSway(null, imagePath, fit),
            Desktop.Hyprland => SetHyprland(imagePath),
            Desktop.Lxqt => SetPcmanfm("pcmanfm-qt", imagePath, fit),
            Desktop.Lxde => SetPcmanfm("pcmanfm", imagePath, fit),
            _ => false,
        };
        // Unknown desktop, or its tool is missing: try the generic X11 tools before giving up.
        if (!ok) ok = SetWithXTool(imagePath, fit);
        // A desktop we couldn't name that still speaks GNOME settings (Unity, Budgie spins, ...).
        if (!ok && desktop == Desktop.Unknown)
            ok = SetGSettings("org.gnome.desktop.background", imagePath, fit, both: true);

        if (!ok)
            throw new WallpaperException(desktop == Desktop.Unknown
                ? "Oriel doesn't know how to set the wallpaper on this desktop. On a bare window manager, install feh."
                : $"Couldn't set the wallpaper on {desktop}. Is its settings tool installed?");

        Remember(imagePath);
    }

    public void SetForMonitor(string monitorDevicePath, string imagePath, WallpaperFit fit)
    {
        if (DesktopEnvironment.Current == Desktop.Sway && File.Exists(imagePath) && SetSway(monitorDevicePath, imagePath, fit))
        {
            Remember(imagePath);
            return;
        }
        SetForAll(imagePath, fit);
    }

    public string? GetCurrent(string? monitorDevicePath = null)
    {
        try
        {
            if (!File.Exists(paths.CurrentWallpaperFile)) return null;
            var path = File.ReadAllText(paths.CurrentWallpaperFile).Trim();
            return path.Length > 0 && File.Exists(path) ? path : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    void Remember(string imagePath)
    {
        try { File.WriteAllText(paths.CurrentWallpaperFile, imagePath); }
        catch (IOException ex) { log.LogDebug(ex, "Couldn't remember the current wallpaper"); }
    }

    // ---------- GNOME family ----------

    static string GSettingsFit(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fit => "scaled",
        WallpaperFit.Stretch => "stretched",
        WallpaperFit.Tile => "wallpaper",
        WallpaperFit.Center => "centered",
        WallpaperFit.Span => "spanned",
        _ => "zoom",
    };

    /// <param name="both">GNOME has a second picture for the dark style; set it too or dark mode keeps the old one.</param>
    bool SetGSettings(string schema, string imagePath, WallpaperFit fit, bool both)
    {
        var uri = new Uri(imagePath).AbsoluteUri;
        if (!Gsettings("set", schema, "picture-uri", uri)) return false;
        if (both) Gsettings("set", schema, "picture-uri-dark", uri);
        Gsettings("set", schema, "picture-options", GSettingsFit(fit));
        return true;
    }

    bool SetMate(string imagePath, WallpaperFit fit)
    {
        const string schema = "org.mate.background";
        if (!Gsettings("set", schema, "picture-filename", imagePath)) return false;
        Gsettings("set", schema, "picture-options", GSettingsFit(fit));
        return true;
    }

    bool Gsettings(params string[] args)
    {
        var r = Proc.Run("gsettings", args);
        if (r is { Ok: true }) return true;
        if (r is not null) log.LogDebug("gsettings {Args} failed: {Error}", string.Join(' ', args.Take(3)), r.Error.Trim());
        return false;
    }

    // ---------- KDE Plasma ----------

    static string? KdeFillMode(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fit => "preserveAspectFit",
        WallpaperFit.Stretch => "stretch",
        WallpaperFit.Tile => "tile",
        WallpaperFit.Center => "pad",
        _ => null, // Fill and Span: Plasma's default crops to fill
    };

    bool SetKde(string imagePath, WallpaperFit fit)
    {
        if (Proc.Has("plasma-apply-wallpaperimage"))
        {
            if (KdeFillMode(fit) is { } mode &&
                Proc.Run("plasma-apply-wallpaperimage", "--fill-mode", mode, imagePath) is { Ok: true })
                return true;
            // Older Plasma has no --fill-mode: the picture still changes, just without the fit.
            return Proc.Run("plasma-apply-wallpaperimage", imagePath) is { Ok: true };
        }

        // Plasma before 5.25: run a small script inside the shell.
        var uri = new Uri(imagePath).AbsoluteUri.Replace("'", "%27");
        var script = "var all = desktops(); for (var i = 0; i < all.length; i++) { var d = all[i]; " +
                     "d.wallpaperPlugin = 'org.kde.image'; d.currentConfigGroup = ['Wallpaper', 'org.kde.image', 'General']; " +
                     $"d.writeConfig('Image', '{uri}'); }}";
        foreach (var qdbus in new[] { "qdbus6", "qdbus", "qdbus-qt5" })
            if (Proc.Run(qdbus, "org.kde.plasmashell", "/PlasmaShell", "org.kde.PlasmaShell.evaluateScript", script) is { Ok: true })
                return true;
        return false;
    }

    // ---------- Xfce ----------

    static int XfceStyle(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fit => 4,     // scaled
        WallpaperFit.Stretch => 3,
        WallpaperFit.Tile => 2,
        WallpaperFit.Center => 1,
        WallpaperFit.Span => 6,
        _ => 5,                    // zoomed
    };

    bool SetXfce(string imagePath, WallpaperFit fit)
    {
        var list = Proc.Run("xfconf-query", "-c", "xfce4-desktop", "-l");
        if (list is not { Ok: true }) return false;

        // Xfce keeps one image per monitor and workspace: set all of them.
        var keys = list.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.EndsWith("/last-image", StringComparison.Ordinal)).ToList();
        if (keys.Count == 0) keys.Add("/backdrop/screen0/monitor0/workspace0/last-image");

        var any = false;
        foreach (var key in keys)
        {
            var set = Proc.Run("xfconf-query", "-c", "xfce4-desktop", "-p", key, "-n", "-t", "string", "-s", imagePath);
            any |= set is { Ok: true };
            var style = key[..^"last-image".Length] + "image-style";
            Proc.Run("xfconf-query", "-c", "xfce4-desktop", "-p", style, "-n", "-t", "int", "-s",
                XfceStyle(fit).ToString(CultureInfo.InvariantCulture));
        }
        return any;
    }

    // ---------- Wayland compositors ----------

    static string SwayFit(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fit => "fit",
        WallpaperFit.Stretch => "stretch",
        WallpaperFit.Tile => "tile",
        WallpaperFit.Center => "center",
        _ => "fill",
    };

    static bool SetSway(string? output, string imagePath, WallpaperFit fit) =>
        Proc.Run("swaymsg", "output", output ?? "*", "bg", imagePath, SwayFit(fit)) is { Ok: true };

    static bool SetHyprland(string imagePath)
    {
        if (Proc.Has("swww"))
            return Proc.Run("swww", "img", imagePath) is { Ok: true };
        if (!Proc.Has("hyprctl")) return false;
        Proc.Run("hyprctl", "hyprpaper", "preload", imagePath);
        return Proc.Run("hyprctl", "hyprpaper", "wallpaper", "," + imagePath) is { Ok: true };
    }

    // ---------- LXQt / LXDE ----------

    static bool SetPcmanfm(string tool, string imagePath, WallpaperFit fit)
    {
        var mode = fit switch
        {
            WallpaperFit.Fit => "fit",
            WallpaperFit.Stretch => "stretch",
            WallpaperFit.Tile => "tile",
            WallpaperFit.Center => "center",
            _ => "crop",
        };
        // pcmanfm-qt calls the filling mode "zoom"; pcmanfm calls it "crop".
        if (tool == "pcmanfm-qt" && mode == "crop") mode = "zoom";
        return Proc.Run(tool, $"--set-wallpaper={imagePath}", $"--wallpaper-mode={mode}") is { Ok: true } ||
               Proc.Run(tool, "--set-wallpaper", imagePath, "--wallpaper-mode", mode) is { Ok: true };
    }

    // ---------- Bare X11 window managers ----------

    static bool SetWithXTool(string imagePath, WallpaperFit fit)
    {
        if (Proc.Has("feh"))
        {
            var flag = fit switch
            {
                WallpaperFit.Fit => "--bg-max",
                WallpaperFit.Stretch => "--bg-scale",
                WallpaperFit.Tile => "--bg-tile",
                WallpaperFit.Center => "--bg-center",
                _ => "--bg-fill",
            };
            if (Proc.Run("feh", flag, imagePath) is { Ok: true }) return true;
        }
        if (Proc.Has("nitrogen"))
        {
            var flag = fit switch
            {
                WallpaperFit.Fit => "--set-scaled",
                WallpaperFit.Stretch => "--set-scaled",
                WallpaperFit.Tile => "--set-tiled",
                WallpaperFit.Center => "--set-centered",
                _ => "--set-zoom-fill",
            };
            if (Proc.Run("nitrogen", flag, "--save", imagePath) is { Ok: true }) return true;
        }
        if (Proc.Has("xwallpaper"))
        {
            var flag = fit switch
            {
                WallpaperFit.Fit => "--maximize",
                WallpaperFit.Stretch => "--stretch",
                WallpaperFit.Tile => "--tile",
                WallpaperFit.Center => "--center",
                _ => "--zoom",
            };
            if (Proc.Run("xwallpaper", flag, imagePath) is { Ok: true }) return true;
        }
        if (Proc.Has("hsetroot"))
        {
            var flag = fit switch
            {
                WallpaperFit.Fit => "-full",
                WallpaperFit.Stretch => "-fill",
                WallpaperFit.Tile => "-tile",
                WallpaperFit.Center => "-center",
                _ => "-cover",
            };
            if (Proc.Run("hsetroot", flag, imagePath) is { Ok: true }) return true;
        }
        return false;
    }

    // ---------- Monitors ----------

    // "0: +*DP-1 2560/597x1440/336+0+0  DP-1"
    [GeneratedRegex(@"^\s*\d+:\s*\+\*?\S+\s+(?<w>\d+)/\d+x(?<h>\d+)/\d+\+(?<x>-?\d+)\+(?<y>-?\d+)\s+(?<name>\S+)\s*$")]
    internal static partial Regex XrandrLine();

    internal static IReadOnlyList<MonitorInfo> ParseXrandr(string output)
    {
        var list = new List<MonitorInfo>();
        foreach (var line in output.Split('\n'))
        {
            var m = XrandrLine().Match(line);
            if (!m.Success) continue;
            list.Add(new MonitorInfo(m.Groups["name"].Value, Int(m, "x"), Int(m, "y"), Int(m, "w"), Int(m, "h")));
        }
        return list;
    }

    static IReadOnlyList<MonitorInfo> XrandrMonitors() =>
        Proc.Run("xrandr", "--listmonitors") is { Ok: true } r ? ParseXrandr(r.Output) : [];

    static int Int(Match m, string group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    static IReadOnlyList<MonitorInfo> SwayMonitors()
    {
        var r = Proc.Run("swaymsg", "-t", "get_outputs", "-r");
        if (r is not { Ok: true }) return [];
        using var doc = JsonDocument.Parse(r.Output);
        var list = new List<MonitorInfo>();
        foreach (var o in doc.RootElement.EnumerateArray())
        {
            if (o.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.False) continue;
            var rect = o.GetProperty("rect");
            // The mode is in physical pixels; the rect is scaled.
            var mode = o.TryGetProperty("current_mode", out var cm) && cm.ValueKind == JsonValueKind.Object ? cm : rect;
            list.Add(new MonitorInfo(o.GetProperty("name").GetString()!, rect.GetProperty("x").GetInt32(),
                rect.GetProperty("y").GetInt32(), mode.GetProperty("width").GetInt32(), mode.GetProperty("height").GetInt32()));
        }
        return list;
    }

    static IReadOnlyList<MonitorInfo> HyprlandMonitors()
    {
        var r = Proc.Run("hyprctl", "monitors", "-j");
        if (r is not { Ok: true }) return [];
        using var doc = JsonDocument.Parse(r.Output);
        return doc.RootElement.EnumerateArray()
            .Select(o => new MonitorInfo(o.GetProperty("name").GetString()!, o.GetProperty("x").GetInt32(),
                o.GetProperty("y").GetInt32(), o.GetProperty("width").GetInt32(), o.GetProperty("height").GetInt32()))
            .ToList();
    }
}
