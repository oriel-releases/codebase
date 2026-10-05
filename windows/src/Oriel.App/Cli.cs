using System.Runtime.InteropServices;
using Oriel.Core;
using Oriel.Core.Providers;
using Oriel.Core.Scheduling;
using Oriel.Core.Security;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Wallpaper;
using Oriel.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Oriel.UI;

/// <summary>
/// Headless mode: a short-lived process that does one thing and exits. Used by the Task Scheduler
/// fallback ("--apply-today") and handy for scripting. Oriel.exe is a GUI-subsystem app,
/// so in a terminal use:
///   Start-Process .\Oriel.exe -ArgumentList '--status' -Wait -NoNewWindow
/// </summary>
static class Cli
{
    static readonly string[] Commands = ["--apply-today", "--change-now", "--status", "--help"];

    public static bool IsCli(string[] args) => args.Any(a => Commands.Contains(a, StringComparer.OrdinalIgnoreCase));

    public static int Run(string[] args)
    {
        AttachConsole(-1);
        var cmd = args.First(a => Commands.Contains(a, StringComparer.OrdinalIgnoreCase)).ToLowerInvariant();
        return cmd switch
        {
            "--apply-today" => InBackground(sp => Apply(sp, force: false)),
            "--change-now" => InBackground(sp => Apply(sp, force: true)),
            "--status" => WithServices(Status),
            _ => Help(),
        };
    }

    static int InBackground(Func<IServiceProvider, int> work)
    {
        EfficiencyMode.Set(true);
        return WithServices(work);
    }

    static int WithServices(Func<IServiceProvider, int> work)
    {
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
        var result = sp.GetRequiredService<ScheduledRun>().RunAsync(force, AppHost.ExePath).GetAwaiter().GetResult();
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
        Out($"Built-in key: {sp.GetRequiredService<IApiKeyStore>().HasKey}");
        Out($"Daily time:   {s.DailyTime}   at sign-in: {s.ApplyAtLogin}   paused: {s.Paused}");
        Out($"Topics:       {string.Join(", ", s.EnabledQueries())}");
        Out($"Last applied: {st.LastAppliedDate?.ToString() ?? "never"}");
        var setter = sp.GetRequiredService<IWallpaperSetter>();
        foreach (var m in setter.GetMonitors())
            Out($"Monitor:      {m.Width}x{m.Height} at ({m.Left},{m.Top}){(m.IsPrimary ? " primary" : "")}");
        Out($"Wallpaper:    {setter.GetCurrent() ?? "(differs per monitor)"}");
        if (st.LastError is not null) Out($"Last error:   {st.LastError}  (next retry {st.NextRetryAt:t})");
        return 0;
    }

    static int Help()
    {
        Out("""
            Oriel - daily wallpaper
              --apply-today   change wallpaper if today's isn't set yet (used by Task Scheduler)
              --change-now    change wallpaper right now
              --status        show current settings and last run
              --background    start the app minimized to the tray
            """);
        return 0;
    }

    static void Out(string line)
    {
        try { Console.WriteLine(line); } catch (IOException) { }
    }

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);
}
