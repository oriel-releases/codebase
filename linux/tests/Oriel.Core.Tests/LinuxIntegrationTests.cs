using Oriel.Core.Platform;
using Oriel.Core.Scheduling;
using Oriel.Core.Wallpaper;

namespace Oriel.Core.Tests;

public sealed class LinuxIntegrationTests
{
    [Theory]
    [InlineData("GNOME", null, Desktop.Gnome)]
    [InlineData("ubuntu:GNOME", null, Desktop.Gnome)]
    [InlineData("Budgie:GNOME", null, Desktop.Gnome)]
    [InlineData("X-Cinnamon", null, Desktop.Cinnamon)]
    [InlineData("KDE", null, Desktop.Kde)]
    [InlineData("XFCE", null, Desktop.Xfce)]
    [InlineData("MATE", null, Desktop.Mate)]
    [InlineData("LXQt", null, Desktop.Lxqt)]
    [InlineData("sway", null, Desktop.Sway)]
    [InlineData("Hyprland", null, Desktop.Hyprland)]
    [InlineData(null, "plasma", Desktop.Kde)]
    [InlineData("i3", null, Desktop.Unknown)]
    [InlineData(null, null, Desktop.Unknown)]
    public void Detects_the_desktop(string? current, string? session, Desktop expected)
    {
        string? Env(string name) => name switch
        {
            "XDG_CURRENT_DESKTOP" => current,
            "DESKTOP_SESSION" => session,
            _ => null,
        };
        Assert.Equal(expected, DesktopEnvironment.Detect(Env));
    }

    [Fact]
    public void Detects_sway_and_hyprland_from_their_sockets_when_the_desktop_name_is_missing()
    {
        Assert.Equal(Desktop.Sway, DesktopEnvironment.Detect(n => n == "SWAYSOCK" ? "/run/user/1000/sway.sock" : null));
        Assert.Equal(Desktop.Hyprland, DesktopEnvironment.Detect(n => n == "HYPRLAND_INSTANCE_SIGNATURE" ? "abc" : null));
    }

    [Fact]
    public void Parses_xrandr_monitors()
    {
        var monitors = LinuxWallpaperSetter.ParseXrandr("""
            Monitors: 2
             0: +*DP-1 2560/597x1440/336+0+0  DP-1
             1: +HDMI-1 1920/527x1080/296+2560+180  HDMI-1
            """);

        Assert.Equal(2, monitors.Count);
        Assert.Equal(new Models.MonitorInfo("DP-1", 0, 0, 2560, 1440), monitors[0]);
        Assert.True(monitors[0].IsPrimary);
        Assert.Equal(new Models.MonitorInfo("HDMI-1", 2560, 180, 1920, 1080), monitors[1]);
        Assert.False(monitors[1].IsPrimary);
    }

    [Fact]
    public void Daily_timer_runs_once_at_the_chosen_time_and_catches_up()
    {
        var text = SystemdTimerRegistration.TimerText(new TimeOnly(9, 30), atSignIn: false, repeatEvery: null);

        Assert.Contains("OnCalendar=*-*-* 09:30:00", text);
        Assert.Equal(1, text.Split("OnCalendar=").Length - 1);
        Assert.Contains("Persistent=true", text);
        Assert.DoesNotContain("OnStartupSec", text);
    }

    [Fact]
    public void Slideshow_timer_repeats_through_the_day_from_the_daily_time()
    {
        var times = SystemdTimerRegistration.RunTimes(new TimeOnly(9, 0), TimeSpan.FromHours(6));
        Assert.Equal([new TimeOnly(3, 0), new TimeOnly(9, 0), new TimeOnly(15, 0), new TimeOnly(21, 0)], times);

        Assert.Equal(24, SystemdTimerRegistration.RunTimes(new TimeOnly(0, 15), TimeSpan.FromHours(1)).Count);
    }

    [Fact]
    public void Timer_runs_shortly_after_login_when_asked()
    {
        var text = SystemdTimerRegistration.TimerText(new TimeOnly(9, 0), atSignIn: true, repeatEvery: null);
        Assert.Contains("OnStartupSec=1min", text);
    }

    [Fact]
    public void Service_runs_the_headless_apply()
    {
        var text = SystemdTimerRegistration.ServiceText("/home/me/Apps/Oriel x.AppImage");
        Assert.Contains("ExecStart=\"/home/me/Apps/Oriel x.AppImage\" --apply-today", text);
    }

    [Theory]
    [InlineData("/usr/bin/oriel", "\"/usr/bin/oriel\"")]
    [InlineData("/home/a b/Oriel", "\"/home/a b/Oriel\"")]
    [InlineData("/odd/$name", "\"/odd/\\\\$name\"")]
    [InlineData("/odd/100%", "\"/odd/100%%\"")]
    public void Quotes_exec_paths_for_desktop_entries(string path, string expected) =>
        Assert.Equal(expected, DesktopEntry.Quote(path));

    [Fact]
    public void Uses_xdg_directories_for_data_and_pictures()
    {
        var paths = new AppPaths();
        Assert.EndsWith(Path.Combine("oriel"), paths.DataRoot);
        Assert.EndsWith("Oriel", paths.PicturesRoot);
        Assert.Equal(Path.Combine(paths.DataRoot, "settings.json"), paths.SettingsFile);
    }
}

public sealed class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+W", Hotkey.Ctrl | Hotkey.Alt, "W", "w")]
    [InlineData("ctrl+shift+f5", Hotkey.Ctrl | Hotkey.Shift, "F5", "F5")]
    [InlineData("Super+Space", Hotkey.Super, "Space", "space")]
    [InlineData("Alt+PageDown", Hotkey.Alt, "PageDown", "Next")]
    public void Parses_and_round_trips(string text, uint mods, string key, string keysym)
    {
        var hotkey = Hotkey.Parse(text);
        Assert.Equal(new Hotkey(mods, key), hotkey);
        Assert.Equal(keysym, hotkey!.Value.KeysymName);
        Assert.Equal(hotkey, Hotkey.Parse(hotkey.Value.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("W")]            // needs a modifier
    [InlineData("Shift+W")]      // Shift alone doesn't count
    [InlineData("Ctrl+Alt")]     // no key
    [InlineData("Ctrl+Nonsense")]
    public void Rejects_unusable_shortcuts(string text) => Assert.Null(Hotkey.Parse(text));
}
