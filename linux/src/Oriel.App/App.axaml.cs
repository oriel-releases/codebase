using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oriel.Core.Auth;
using Oriel.Core.Platform;
using Oriel.Core.Scheduling;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.UI.Services;
using Oriel.UI.ViewModels;
using Oriel.UI.Views;

namespace Oriel.UI;

/// <summary>
/// Lives in the tray. The window is destroyed when closed (not just hidden), so its controls and images are
/// freed, and memory is handed back to the system until it's opened again.
/// </summary>
public sealed class App : Application
{
    static ServiceProvider? _services;
    MainWindow? _window;
    bool _quitting;
    ILogger? _log;

    public static SingleInstance? Instance { get; set; }
    public static string[] StartupArgs { get; set; } = [];

    public static T Get<T>() where T : notnull => _services!.GetRequiredService<T>();

    bool IsWindowVisible => _window is { IsVisible: true };

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // We live in the tray: closing the window must not end the process.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Start(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    void Start(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _services = AppHost.Build(services =>
        {
            services.AddSingleton<TrayIconService>();
            services.AddSingleton<UpdateService>();
            services.AddSingleton<HotkeyService>();
            services.AddSingleton<SignInViewModel>();
            services.AddSingleton<HomeViewModel>();
            services.AddSingleton<CollectionsViewModel>();
            services.AddSingleton<HistoryViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<MainViewModel>();
        });
        _log = Get<ILoggerFactory>().CreateLogger("App");

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            _log?.LogError(e.Exception, "Unhandled UI exception");
            e.Handled = true; // a tray app should survive a bad click
        };

        if (Instance is not null)
        {
            Instance.MessageReceived += m => Dispatcher.UIThread.Post(() => OnMessage(m));
            Instance.StartListening();
        }

        Get<TrayIconService>().Create(open: ShowWindow, changeNow: () => ChangeFromTrayAsync(), quit: () => Quit(lifetime));

        var hotkeys = Get<HotkeyService>();
        hotkeys.Pressed += () =>
        {
            _log?.LogInformation("Shortcut pressed");
            Dispatcher.UIThread.Post(() => _ = ChangeFromTrayAsync(alwaysShowPopup: true));
        };
        _ = hotkeys.ApplyAsync(Hotkey.Parse(Get<ISettingsStore>().Load().Hotkey))
            .ContinueWith(t => _log?.LogInformation("Shortcut registered: {Result}", t.IsCompletedSuccessfully ? t.Result ?? "ok" : "failed"));

        Get<WallpaperService>().Applied += (_, r) =>
        {
            Dispatcher.UIThread.Post(() => OnApplied(r));
            _ = Get<UpdateService>().CheckAsync();
        };

        var scheduler = Get<DailyScheduler>();
        scheduler.Attempted += (_, r) => Dispatcher.UIThread.Post(() => Get<HomeViewModel>().ShowResult(r));
        scheduler.Start();

        // Keep the autostart entry and the fallback timer in step with the settings, and pointed at this copy.
        var schedule = Get<ScheduleSync>();
        Get<ISettingsStore>().SettingsChanged += (_, _) => Task.Run(() => schedule.Sync(AppInfo.ExePath));
        Task.Run(() => schedule.Sync(AppInfo.ExePath, force: true));

        Get<UpdateService>().Start();

        // The menu entry and the oriel:// link handler (the website's "Open Oriel" button): keep them pointed at this copy.
        Task.Run(() =>
        {
            try { DesktopEntry.Register(AppInfo.ExePath, AppInfo.IconFile); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log?.LogWarning(ex, "Couldn't register the menu entry"); }
        });

        var auth = Get<AuthService>();
        // Back from the browser after signing in: bring Oriel to the front.
        auth.Changed += (_, _) => { if (auth.IsSignedIn) Dispatcher.UIThread.Post(ShowWindow); };
        auth.Start();
        // Make sure a browser extension that gets linked later has a wallpaper to show right away.
        var sync = Get<SyncPublisher>();
        auth.Changed += (_, _) => { if (auth.IsSignedIn) _ = sync.PublishLatestIfMissingAsync(); };
        _ = sync.PublishLatestIfMissingAsync();

        var background = StartupArgs.Contains("--background", StringComparer.OrdinalIgnoreCase);
        // A sign-in link that started this process always shows the window, so the result is visible.
        if (HandleLink(StartupArgs)) background = false;
        else if (!auth.IsSignedIn) background = false; // nothing works until signed in: ask right away
        if (background) EnterBackground();
        else ShowWindow();

        _log.LogInformation("Oriel started (background={Background}, desktop={Desktop})", background, DesktopEnvironment.Current);
    }

    void OnMessage(string message)
    {
        if (message.StartsWith(SingleInstance.LinkPrefix, StringComparison.Ordinal))
        {
            HandleLink([message[SingleInstance.LinkPrefix.Length..]]);
            ShowWindow();
        }
        else if (message == SingleInstance.Change) _ = ChangeFromTrayAsync(alwaysShowPopup: true);
        else if (message == SingleInstance.Refresh) { if (IsWindowVisible) _ = Get<HomeViewModel>().RefreshAsync(); }
        else ShowWindow();
    }

    /// <summary>Handles an oriel://auth link among the arguments. Returns true if there was one.</summary>
    bool HandleLink(IEnumerable<string> args)
    {
        var list = args.ToList();
        if (!DeepLink.IsLink(list)) return false;
        if (DeepLink.IsOpen(list)) return true; // just show the window
        _log?.LogInformation("Sign-in link received");
        var signIn = Get<SignInViewModel>();
        if (DeepLink.TryGetGoogleToken(list, out var token)) _ = signIn.CompleteAsync(token);
        else signIn.Invalid();
        return true;
    }

    void OnApplied(ApplyResult result)
    {
        if (IsWindowVisible) _ = Get<HomeViewModel>().RefreshAsync();
    }

    /// <summary>
    /// Tray menu, shortcut or command: change now. The popup says it's working; the open window already
    /// shows its own spinner, so it only appears there when the shortcut was used.
    /// </summary>
    async Task ChangeFromTrayAsync(bool alwaysShowPopup = false)
    {
        if (!Get<AuthService>().IsSignedIn)
        {
            ShowWindow();
            return;
        }
        var home = Get<HomeViewModel>();
        ChangePopup? popup = null;
        if (!home.IsBusy && (alwaysShowPopup || !IsWindowVisible))
        {
            try
            {
                popup = new ChangePopup();
                popup.ShowPopup();
            }
            catch (Exception ex)
            {
                // The popup is a nicety: never let it stop the change itself.
                _log?.LogWarning(ex, "Couldn't show the change popup");
                popup = null;
            }
        }
        var wallpapers = Get<WallpaperService>();
        EventHandler<ApplyPhase>? onPhase = null;
        if (popup is not null)
        {
            onPhase = (_, phase) => Dispatcher.UIThread.Post(() => popup.SetPhase(phase));
            wallpapers.PhaseChanged += onPhase;
        }
        ApplyResult result;
        try
        {
            result = await home.ChangeAsync();
        }
        finally
        {
            if (onPhase is not null) wallpapers.PhaseChanged -= onPhase;
            popup?.Dismiss();
        }
        if (result.Status is ApplyStatus.Failed or ApplyStatus.RateLimited && !IsWindowVisible)
            Notifier.ShowError(result.Error ?? "Unknown error.");
    }

    public void ShowWindow()
    {
        if (_window is null)
        {
            var vm = Get<MainViewModel>();
            _window = new MainWindow { DataContext = vm };
            var window = _window;
            window.Closed += (_, _) =>
            {
                vm.Dispose();
                if (_window == window) _window = null;
                if (!_quitting) EnterBackground();
            };
            window.Activated += (_, _) => _ = Get<HomeViewModel>().RefreshAsync();
        }
        if (_window.WindowState == Avalonia.Controls.WindowState.Minimized)
            _window.WindowState = Avalonia.Controls.WindowState.Normal;
        if (!_window.IsVisible) _window.Show();
        _window.Activate();
    }

    /// <summary>Tray-only mode: free what the window used and give the memory back.</summary>
    static void EnterBackground()
    {
        Get<HomeViewModel>().ReleaseImages();
        Get<HistoryViewModel>().Release();
        Get<CollectionsViewModel>().Release();
        // After the window's controls are gone: let the UI finish closing first.
        Dispatcher.UIThread.Post(Native.TrimMemory, DispatcherPriority.Background);
    }

    void Quit(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _quitting = true;
        Get<DailyScheduler>().Dispose();
        Get<TrayIconService>().Dispose();
        Get<HotkeyService>().Dispose();
        Get<UpdateService>().ApplyOnExit();
        _window?.Close();
        _services?.Dispose();
        lifetime.Shutdown();
    }
}
