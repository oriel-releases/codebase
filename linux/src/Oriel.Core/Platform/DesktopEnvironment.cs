namespace Oriel.Core.Platform;

public enum Desktop { Unknown, Gnome, Cinnamon, Mate, Kde, Xfce, Lxqt, Lxde, Sway, Hyprland, Deepin }

/// <summary>Which desktop and display server this session runs, to pick the right way to set a wallpaper.</summary>
public static class DesktopEnvironment
{
    /// <summary>Variables a systemd-started run is missing; they're copied over from the user manager (see <see cref="ImportSessionEnvironment"/>).</summary>
    static readonly string[] SessionVariables =
    [
        "DISPLAY", "WAYLAND_DISPLAY", "XAUTHORITY", "XDG_CURRENT_DESKTOP", "XDG_SESSION_TYPE", "DESKTOP_SESSION",
        "DBUS_SESSION_BUS_ADDRESS", "XDG_RUNTIME_DIR", "SWAYSOCK", "HYPRLAND_INSTANCE_SIGNATURE", "KDE_FULL_SESSION",
    ];

    public static Desktop Current { get; } = Detect(Environment.GetEnvironmentVariable);

    public static bool IsWayland =>
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    public static Desktop Detect(Func<string, string?> env)
    {
        var tokens = ((env("XDG_CURRENT_DESKTOP") ?? "") + ":" + (env("DESKTOP_SESSION") ?? ""))
            .ToLowerInvariant().Split([':', ';', ' ', '-'], StringSplitOptions.RemoveEmptyEntries);

        bool Has(string t) => tokens.Contains(t);

        // The most specific names first: "ubuntu:GNOME" and "Budgie:GNOME" are GNOME-family, "X-Cinnamon" isn't plain GNOME.
        if (Has("cinnamon")) return Desktop.Cinnamon;
        if (Has("mate")) return Desktop.Mate;
        if (Has("kde") || Has("plasma")) return Desktop.Kde;
        if (Has("xfce")) return Desktop.Xfce;
        if (Has("lxqt")) return Desktop.Lxqt;
        if (Has("lxde")) return Desktop.Lxde;
        if (Has("deepin") || Has("dde")) return Desktop.Deepin;
        if (Has("gnome") || Has("unity") || Has("budgie") || Has("pantheon") || Has("ubuntu")) return Desktop.Gnome;
        if (Has("sway") || !string.IsNullOrEmpty(env("SWAYSOCK"))) return Desktop.Sway;
        if (Has("hyprland") || !string.IsNullOrEmpty(env("HYPRLAND_INSTANCE_SIGNATURE"))) return Desktop.Hyprland;
        return Desktop.Unknown;
    }

    /// <summary>
    /// A run started by a systemd timer has no desktop in its environment. The user manager usually knows it
    /// (the desktop session imports it at login), so copy over what's missing.
    /// </summary>
    public static void ImportSessionEnvironment()
    {
        if (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") is { Length: > 0 } &&
            (Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 } ||
             Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 }))
            return;

        var shown = Proc.Run("systemctl", "--user", "show-environment");
        if (shown is not { Ok: true }) return;
        foreach (var line in shown.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var name = line[..eq];
            if (!SessionVariables.Contains(name) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))) continue;
            Environment.SetEnvironmentVariable(name, Unquote(line[(eq + 1)..]));
        }
    }

    /// <summary>"systemctl show-environment" prints values shell-quoted ($'...') when they need it.</summary>
    static string Unquote(string value) =>
        value.Length >= 3 && value.StartsWith("$'") && value.EndsWith('\'') ? value[2..^1] : value;
}
