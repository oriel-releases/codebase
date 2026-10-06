using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Oriel.UI.Services;
using Velopack;

namespace Oriel.UI;

public static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Must run first: handles the updater's install hooks (and exits for those). A headless
        // timer run must not restart itself into a pending update, so only the app applies them.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(!Cli.IsCli(args))
            .Run();

        if (Cli.IsCli(args)) return Cli.Run(args);

        // One Oriel per user: a second launch hands its request to the first and exits.
        var request = RequestFor(args);
        if (SingleInstance.TrySend(request)) return 0;
        var instance = SingleInstance.TryListen();
        if (instance is null && SingleInstance.TrySend(request)) return 0;

        App.Instance = instance;
        App.StartupArgs = args;
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            instance?.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        // A photo viewer needs no GPU. Software drawing avoids loading a GL driver (tens of MB, plus a GPU wakeup);
        // set ORIEL_GPU=1 to use the GPU anyway.
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("ORIEL_GPU") != "1")
            builder = builder.With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] });
        return builder;
    }

    /// <summary>What a second launch asks the running Oriel to do.</summary>
    static string RequestFor(string[] args)
    {
        if (args.FirstOrDefault(a => a.StartsWith("oriel://", StringComparison.OrdinalIgnoreCase)) is { } link)
            return SingleInstance.LinkPrefix + link;
        if (args.Contains("--change-now", StringComparer.OrdinalIgnoreCase)) return SingleInstance.Change;
        return args.Contains("--background", StringComparer.OrdinalIgnoreCase) ? SingleInstance.Refresh : SingleInstance.Show;
    }
}
