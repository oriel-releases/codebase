using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Oriel.Core.Platform;

namespace Oriel.Core.Scheduling;

public interface IStartupRegistration
{
    void Set(bool enabled, string exePath);
}

/// <summary>"Start at login": an XDG autostart entry, so every desktop environment honors it. Starts minimized to the tray.</summary>
public sealed class AutostartRegistration : IStartupRegistration
{
    public static string EntryPath => Path.Combine(AppPaths.XdgConfigHome(), "autostart", "oriel.desktop");

    public void Set(bool enabled, string exePath)
    {
        if (!enabled)
        {
            try { File.Delete(EntryPath); } catch (IOException) { }
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
        File.WriteAllText(EntryPath, Unix($"""
            [Desktop Entry]
            Type=Application
            Name=Oriel
            Comment=A new wallpaper, every day
            Exec={DesktopEntry.Quote(exePath)} --background
            Icon=oriel
            Terminal=false
            NoDisplay=true
            X-GNOME-Autostart-enabled=true
            X-GNOME-Autostart-Delay=5

            """));
    }

    internal static string Unix(string text) => text.ReplaceLineEndings("\n");
}

/// <summary>
/// The application menu entry, and the <c>oriel://</c> link handler (the website's "Open Oriel" button launches
/// this exe with the link as its argument). Per user, no root; re-written on every start so it follows the copy
/// that is running now (an AppImage moves around, updates replace it).
/// </summary>
public static class DesktopEntry
{
    public static string EntryPath => Path.Combine(AppPaths.XdgDataHome(), "applications", "oriel.desktop");
    static string IconPath => Path.Combine(AppPaths.XdgDataHome(), "icons", "hicolor", "256x256", "apps", "oriel.png");

    public static void Register(string exePath, string? iconSource)
    {
        var entry = AutostartRegistration.Unix($"""
            [Desktop Entry]
            Type=Application
            Name=Oriel
            GenericName=Wallpaper changer
            Comment=A new wallpaper, every day
            Exec={Quote(exePath)} %u
            Icon=oriel
            Terminal=false
            Categories=Utility;GTK;
            Keywords=wallpaper;background;unsplash;
            StartupWMClass=Oriel
            MimeType=x-scheme-handler/oriel;

            """);
        var changed = WriteIfDifferent(EntryPath, entry);

        if (iconSource is not null && File.Exists(iconSource) && !File.Exists(IconPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IconPath)!);
            File.Copy(iconSource, IconPath, overwrite: true);
        }

        // Only talk to the desktop database when something changed: both tools can be slow.
        if (!changed) return;
        Proc.Run("xdg-mime", "default", "oriel.desktop", "x-scheme-handler/oriel");
        Proc.Run("update-desktop-database", Path.GetDirectoryName(EntryPath)!);
    }

    public static void Unregister()
    {
        foreach (var file in new[] { EntryPath, IconPath, AutostartRegistration.EntryPath })
            try { File.Delete(file); } catch (IOException) { }
        Proc.Run("update-desktop-database", Path.GetDirectoryName(EntryPath)!);
    }

    /// <summary>Quotes a path for an Exec= line (desktop entry spec: double quotes, with a few characters escaped).</summary>
    public static string Quote(string path)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in path)
        {
            // The spec escapes twice: once for the quoting rule, once for the string value ("\\" is one backslash).
            if (c == '\\') sb.Append(@"\\\\");
            else if (c is '"' or '`' or '$') sb.Append(@"\\").Append(c);
            else if (c == '%') sb.Append("%%");
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    static bool WriteIfDifferent(string file, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (File.Exists(file) && File.ReadAllText(file) == content) return false;
        File.WriteAllText(file, content);
        return true;
    }
}

public interface IScheduledTasks
{
    /// <summary>
    /// Daily run at <paramref name="time"/> (repeating every <paramref name="repeatEvery"/> through the day
    /// for a slideshow), plus one soon after login when <paramref name="atSignIn"/>.
    /// </summary>
    bool Register(string exePath, TimeOnly time, bool atSignIn, TimeSpan? repeatEvery = null);

    /// <summary>One-shot run after a failed change (offline, rate limited). Replaces any earlier retry.</summary>
    bool ScheduleRetry(string exePath, DateTimeOffset at);

    /// <summary>Removes every Oriel timer.</summary>
    void Unregister();
}

/// <summary>
/// systemd user timers: the fallback for when the tray app isn't running. <c>Oriel --apply-today</c> runs at the
/// daily time (and shortly after login if asked); it takes a few seconds and exits. <c>Persistent=true</c> runs a
/// missed one (PC off or asleep) as soon as possible afterwards. Does nothing on systems without systemd; the
/// in-app scheduler still works while Oriel is open.
/// </summary>
public sealed class SystemdTimerRegistration(ILogger<SystemdTimerRegistration> log) : IScheduledTasks
{
    const string Unit = "oriel-apply";
    const string RetryUnit = "oriel-retry";

    static string UnitDir => Path.Combine(AppPaths.XdgConfigHome(), "systemd", "user");

    public bool Register(string exePath, TimeOnly time, bool atSignIn, TimeSpan? repeatEvery = null)
    {
        if (!Proc.Has("systemctl")) return false;
        try
        {
            Directory.CreateDirectory(UnitDir);
            var changed = WriteIfDifferent($"{Unit}.service", ServiceText(exePath))
                        | WriteIfDifferent($"{Unit}.timer", TimerText(time, atSignIn, repeatEvery));
            if (changed) Proc.Run("systemctl", "--user", "daemon-reload");

            var result = Proc.Run("systemctl", "--user", "enable", "--now", $"{Unit}.timer");
            // A changed schedule must replace the one systemd already loaded.
            if (changed) Proc.Run("systemctl", "--user", "restart", $"{Unit}.timer");
            if (result is { Ok: false }) log.LogInformation("systemd timer not enabled: {Error}", result.Error.Trim());
            return result is { Ok: true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Couldn't write the systemd timer");
            return false;
        }
    }

    public bool ScheduleRetry(string exePath, DateTimeOffset at)
    {
        if (!Proc.Has("systemd-run")) return false;
        StopRetry();
        var result = Proc.Run("systemd-run", ["--user", $"--unit={RetryUnit}", "--collect", "--quiet",
            "--timer-property=AccuracySec=1s", "--property=Nice=10",
            $"--on-calendar={at.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}",
            exePath, "--apply-today"]);
        if (result is { Ok: false }) log.LogInformation("Couldn't schedule a retry: {Error}", result.Error.Trim());
        return result is { Ok: true };
    }

    public void Unregister()
    {
        if (!Proc.Has("systemctl")) return;
        StopRetry();
        Proc.Run("systemctl", "--user", "disable", "--now", $"{Unit}.timer");
        foreach (var file in new[] { $"{Unit}.timer", $"{Unit}.service" })
            try { File.Delete(Path.Combine(UnitDir, file)); } catch (IOException) { }
        Proc.Run("systemctl", "--user", "daemon-reload");
    }

    static void StopRetry() =>
        Proc.Run("systemctl", "--user", "stop", $"{RetryUnit}.timer", $"{RetryUnit}.service");

    internal static string ServiceText(string exePath) => AutostartRegistration.Unix($"""
        [Unit]
        Description=Oriel: change the wallpaper

        [Service]
        Type=oneshot
        ExecStart={DesktopEntry.Quote(exePath)} --apply-today
        Nice=10
        TimeoutStartSec=10min

        """);

    /// <summary>The clock times a day's runs fall on: the daily time, then every <paramref name="repeatEvery"/> for 24 hours.</summary>
    internal static IReadOnlyList<TimeOnly> RunTimes(TimeOnly time, TimeSpan? repeatEvery)
    {
        var step = repeatEvery is { } e && e >= TimeSpan.FromMinutes(1) ? e : TimeSpan.FromDays(1);
        var times = new List<TimeOnly>();
        for (var offset = TimeSpan.Zero; offset < TimeSpan.FromDays(1); offset += step)
            times.Add(time.Add(offset));
        return times.Distinct().Order().ToList();
    }

    internal static string TimerText(TimeOnly time, bool atSignIn, TimeSpan? repeatEvery)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Unit]");
        sb.AppendLine("Description=Oriel: scheduled wallpaper change");
        sb.AppendLine();
        sb.AppendLine("[Timer]");
        foreach (var t in RunTimes(time, repeatEvery))
            sb.AppendLine(CultureInfo.InvariantCulture, $"OnCalendar=*-*-* {t:HH:mm}:00");
        // The user manager starts at login; the pause keeps Oriel out of the way while the desktop is busy starting.
        if (atSignIn) sb.AppendLine("OnStartupSec=1min");
        sb.AppendLine("Persistent=true");
        sb.AppendLine("AccuracySec=30s");
        sb.AppendLine();
        sb.AppendLine("[Install]");
        sb.AppendLine("WantedBy=timers.target");
        return AutostartRegistration.Unix(sb.ToString());
    }

    static bool WriteIfDifferent(string name, string content)
    {
        var file = Path.Combine(UnitDir, name);
        if (File.Exists(file) && File.ReadAllText(file) == content) return false;
        File.WriteAllText(file, content);
        return true;
    }
}
