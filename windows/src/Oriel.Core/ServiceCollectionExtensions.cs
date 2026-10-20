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
        // Connections race IPv6 against IPv4 like a browser does, so a broken IPv6 network never stalls Oriel (see FastConnect).
        services.AddSingleton(_ => new HttpClient(FastConnect.Handler())
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
        services.AddSingleton<PollinationsClient>();
        services.AddSingleton<SyncPublisher>();
        services.AddSingleton<ShareService>();
        services.AddSingleton<ISyncPublisher>(sp => sp.GetRequiredService<SyncPublisher>());
        services.AddSingleton<ISystemTheme, WindowsSystemTheme>();
        services.AddSingleton<WallpaperService>();
        services.AddSingleton<FollowService>();
        services.AddSingleton<PulseListener>();
        services.AddSingleton<SettingsSync>();
        services.AddSingleton<FavoritesSync>();
        services.AddSingleton<ISlotClaimer, SlotClaimer>();
        services.AddSingleton<TodayService>();
        services.AddSingleton<StatsReporter>();
        services.AddSingleton<IStats>(sp => sp.GetRequiredService<StatsReporter>());
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
