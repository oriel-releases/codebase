using Oriel.Core.Auth;
using Oriel.Core.Providers;
using Oriel.Core.Scheduling;
using Oriel.Core.Security;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Oriel.Core.Wallpaper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Oriel.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrielCore(this IServiceCollection services, AppPaths? paths = null)
    {
        paths ??= new AppPaths();
        paths.EnsureCreated();

        services.AddSingleton(paths);
        services.AddSingleton(_ => new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestHeaders = { { "User-Agent", "Oriel/1.0 (+desktop wallpaper app)" } },
        });
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IAccountStore, DpapiAccountStore>();
        services.AddSingleton<FirebaseClient>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<IAuthState>(sp => sp.GetRequiredService<AuthService>());
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IApiKeyStore, BuiltInApiKeyStore>();
        services.AddSingleton<IHistoryStore, SqliteHistoryStore>();
        services.AddSingleton<IWallpaperSetter, WindowsWallpaperSetter>();
        services.AddSingleton<ITopicPicker>(_ => new RandomTopicPicker());
        services.AddSingleton<UnsplashProvider>();
        services.AddSingleton<IImageProvider>(sp => sp.GetRequiredService<UnsplashProvider>());
        services.AddSingleton<SyncPublisher>();
        services.AddSingleton<ISyncPublisher>(sp => sp.GetRequiredService<SyncPublisher>());
        services.AddSingleton<WallpaperService>();
        services.AddSingleton<RetentionService>();
        services.AddSingleton<WallpaperFolderMover>();
        services.AddSingleton<IStartupRegistration, StartupRegistration>();
        services.AddSingleton<IScheduledTasks, TaskSchedulerRegistration>();
        services.AddSingleton<DailyScheduler>();
        services.AddSingleton<ScheduleSync>();
        services.AddSingleton<ScheduledRun>();
        return services;
    }

    public static ILoggingBuilder AddOrielFile(this ILoggingBuilder builder, AppPaths paths) =>
        builder.AddProvider(new Logging.FileLoggerProvider(paths.LogsDir));
}
