using Oriel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Oriel.UI;

/// <summary>Composition root shared by the window and the headless runs.</summary>
public static class AppHost
{
    public static ServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var paths = new AppPaths();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information).AddOrielFile(paths));
        services.AddOrielCore(paths);
        services.AddSingleton<Oriel.Core.Wallpaper.ILockScreenSetter, Services.LockScreenSetter>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public static string ExePath => Environment.ProcessPath!;
}
