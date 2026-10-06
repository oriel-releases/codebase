using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oriel.Core.Platform;
using Oriel.Core.Providers;
using Oriel.Core.Scheduling;
using Oriel.Core.Security;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Wallpaper;
using Oriel.UI.Services;

namespace Oriel.UI;

/// <summary>
/// Headless mode: a short-lived process that does one thing and exits. Used by the systemd timer
/// ("--apply-today") and handy for scripting and for a desktop keyboard shortcut ("--change-now").
/// </summary>
static class Cli
{
    static readonly string[] Commands = ["--apply-today", "--change-now", "--status", "--help", "-h", "--version"];

    public static bool IsCli(string[] args) => args.Any(a => Commands.Contains(a, StringComparer.OrdinalIgnoreCase));

    public static int Run(string[] args)
    {
        var cmd = args.First(a => Commands.Contains(a, StringComparer.OrdinalIgnoreCase)).ToLowerInvariant();
        return cmd switch
        {
            "--apply-today" => InBackground(sp => Apply(sp, force: false)),
            "--change-now" => ChangeNow(),
            "--status" => WithServices(Status),
            "--version" => Version(),
            _ => Help(),
        };
    }

    /// <summary>With Oriel running, let it do the change (it shows its popup and updates the window); otherwise do it here.</summary>
    static int ChangeNow()
    {
        if (SingleInstance.TrySend(SingleInstance.Change)) return 0;
        return InBackground(sp => Apply(sp, force: true));
    }

    static int InBackground(Func<IServiceProvider, int> work)
    {
        Native.BeNice();
        return WithServices(work);
    }

    static int WithServices(Func<IServiceProvider, int> work)
    {
        // Started by a timer there is no desktop in the environment; borrow the session's.
        DesktopEnvironment.ImportSessionEnvironment();
        using var services = AppHost.Build();
        try
        {
            return work(services);
        }
        catch (Exception ex)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Cli").LogError(ex, "Headless run crashed");
            Out($"Error: {ex.Message}");
            return 1;
        }
    }

    static int Apply(IServiceProvider sp, bool force)
    {
        var result = sp.GetRequiredService<ScheduledRun>().RunAsync(force, AppInfo.ExePath).GetAwaiter().GetResult();
        if (result.Status == ApplyStatus.Applied) SingleInstance.TrySend(SingleInstance.Refresh); // the open window catches up
        switch (result.Status)
        {
            case ApplyStatus.Applied:
                foreach (var e in result.Entries)
                    Out($"Applied '{e.Topic}' photo by {e.PhotographerName}: {e.FilePath}");
                return 0;
            case ApplyStatus.AlreadyAppliedToday:
                Out("Today's wallpaper is already set.");
                return 0;
            case ApplyStatus.Paused:
                Out("Oriel is paused.");
                return 0;
            case ApplyStatus.Busy:
            case ApplyStatus.RateLimited:
                Out(result.Error!);
                return 0;
            case ApplyStatus.SignInRequired:
                Out("Sign in to Oriel first: open the app and continue with Google.");
                return 3;
            default:
                Out($"Failed: {result.Error} (current wallpaper kept)");
                return result.ErrorKind is ProviderErrorKind.MissingKey or ProviderErrorKind.Unauthorized ? 3 : 1;
        }
    }

    static int Status(IServiceProvider sp)
    {
        var store = sp.GetRequiredService<ISettingsStore>();
        var s = store.Load();
        var st = store.LoadState();
        Out($"Desktop:      {DesktopEnvironment.Current}{(DesktopEnvironment.IsWayland ? " (Wayland)" : "")}");
        Out($"Built-in key: {sp.GetRequiredService<IApiKeyStore>().HasKey}");
        Out($"Daily time:   {s.DailyTime}   at login: {s.ApplyAtLogin}   paused: {s.Paused}");
        Out($"Topics:       {string.Join(", ", s.EnabledQueries())}");
        Out($"Last applied: {st.LastAppliedDate?.ToString() ?? "never"}");
        var setter = sp.GetRequiredService<IWallpaperSetter>();
        foreach (var m in setter.GetMonitors())
            Out($"Monitor:      {m.DevicePath} {m.Width}x{m.Height} at ({m.Left},{m.Top}){(m.IsPrimary ? " primary" : "")}");
        Out($"Wallpaper:    {setter.GetCurrent() ?? "(not set by Oriel yet)"}");
        if (st.LastError is not null) Out($"Last error:   {st.LastError}  (next retry {st.NextRetryAt:t})");
        return 0;
    }

    static int Version()
    {
        Out($"Oriel {AppInfo.Version}");
        return 0;
    }

    static int Help()
    {
        Out("""
            Oriel - a new wallpaper, every day
              --apply-today   change wallpaper if today's isn't set yet (used by the systemd timer)
              --change-now    change wallpaper right now (hand it to a keyboard shortcut on Wayland)
              --status        show current settings and last run
              --background    start the app minimized to the tray
              --version       print the version
            """);
        return 0;
    }

    static void Out(string line)
    {
        try { Console.WriteLine(line); } catch (IOException) { }
    }
}
