using Oriel.Core;
using Oriel.Core.Auth;
using Oriel.Core.Scheduling;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Oriel.UI.Services;
using Oriel.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace Oriel.UI;

/// <summary>
/// Lives in the tray. The window is destroyed when closed (not just hidden), so the XAML tree and
/// images are freed and the process idles in efficiency mode until it's opened again.
/// </summary>
public partial class App : Application
{
    static ServiceProvider? _services;
    MainWindow? _window;
    bool _quitting;
    ILogger? _log;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            _log?.LogError(e.Exception, "Unhandled UI exception");
            e.Handled = true; // a tray app should survive a bad click
        };
    }

    public static DispatcherQueue Dispatcher { get; private set; } = null!;
    public static T Get<T>() where T : notnull => _services!.GetRequiredService<T>();

    bool IsWindowVisible => _window is { Visible: true };

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Dispatcher = DispatcherQueue.GetForCurrentThread();
        // We live in the tray: closing the window must not end the process.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

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
        });
        _log = Get<ILoggerFactory>().CreateLogger("App");

        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        AppInstance.GetCurrent().Activated += (_, e) => Dispatcher.TryEnqueue(() => OnActivated(e));

        Toasts.Register((_, e) => Dispatcher.TryEnqueue(() => HandleToast(e.Arguments)));

        Get<TrayIconService>().Create(open: ShowWindow, changeNow: () => ChangeFromTrayAsync(), quit: Quit);

        var hotkeys = Get<HotkeyService>();
        hotkeys.Pressed += () =>
        {
            _log?.LogInformation("Shortcut pressed");
            Dispatcher.TryEnqueue(() => _ = ChangeFromTrayAsync(alwaysShowPopup: true));
        };
        _ = hotkeys.ApplyAsync(Hotkey.Parse(Get<ISettingsStore>().Load().Hotkey))
            .ContinueWith(t => _log?.LogInformation("Shortcut registered: {Result}", t.Result ?? "ok"));

        Get<WallpaperService>().Applied += (_, r) =>
        {
            Dispatcher.TryEnqueue(() => OnApplied(r));
            _ = Get<UpdateService>().CheckAsync();
        };

        var scheduler = Get<DailyScheduler>();
        scheduler.Attempted += (_, r) => Dispatcher.TryEnqueue(() => Get<HomeViewModel>().ShowResult(r));
        scheduler.Start();

        // Keep the Run key and the fallback task in step with the settings, and pointed at this exe.
        var schedule = Get<ScheduleSync>();
        Get<ISettingsStore>().SettingsChanged += (_, _) => Task.Run(() => schedule.Sync(AppHost.ExePath));
        Task.Run(() => schedule.Sync(AppHost.ExePath, force: true));

        Get<UpdateService>().Start();

        // oriel:// links (the website's "Open Oriel" button) launch this exe; keep that pointed at this copy.
        Task.Run(() => ProtocolRegistration.Register(AppHost.ExePath));
        var auth = Get<AuthService>();
        // Back from the browser after signing in: bring Oriel to the front, active (not dimmed).
        auth.Changed += (_, _) => { if (auth.IsSignedIn) Dispatcher.TryEnqueue(ShowWindow); };
        auth.Start();

        var background = Environment.GetCommandLineArgs().Contains("--background", StringComparer.OrdinalIgnoreCase);
        if (activation.Kind == ExtendedActivationKind.AppNotification)
        {
            var toastArgs = ((AppNotificationActivatedEventArgs)activation.Data).Arguments;
            background = HandleToast(toastArgs) == false;
        }
        // A sign-in link that started this process always shows the window, so the result is visible.
        if (HandleLink(Environment.GetCommandLineArgs().Skip(1))) background = false;
        else if (!auth.IsSignedIn) background = false; // nothing works until signed in: ask right away
        if (background) EnterBackground();
        else ShowWindow();

        _log.LogInformation("Oriel started (background={Background})", background);
    }

    /// <summary>Handles an oriel://auth link among the arguments. Returns true if there was one.</summary>
    bool HandleLink(IEnumerable<string> args)
    {
        var list = args.ToList();
        if (!DeepLink.IsLink(list)) return false;
        _log?.LogInformation("Sign-in link received");
        var signIn = Get<SignInViewModel>();
        if (DeepLink.TryGetGoogleToken(list, out var token)) _ = signIn.CompleteAsync(token);
        else signIn.Invalid();
        return true;
    }

    void OnActivated(AppActivationArguments e)
    {
        // The website's "Open Oriel" button while Oriel is already running.
        if (e.Kind == ExtendedActivationKind.Launch &&
            e.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch &&
            HandleLink(launch.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
        {
            ShowWindow();
            return;
        }

        // A second launch was redirected here (Start menu, shortcut, toast while not registered).
        if (e.Kind == ExtendedActivationKind.AppNotification &&
            e.Data is AppNotificationActivatedEventArgs toast)
        {
            if (HandleToast(toast.Arguments)) ShowWindow();
            return;
        }
        ShowWindow();
    }

    /// <returns>True if the window should be shown.</returns>
    bool HandleToast(IDictionary<string, string> args)
    {
        args.TryGetValue(Toasts.ActionKey, out var action);
        switch (action)
        {
            case Toasts.SkipAction:
                _ = Get<HomeViewModel>().ChangeAsync();
                return false;
            case Toasts.FavoriteAction:
                if (args.TryGetValue(Toasts.IdKey, out var id) && long.TryParse(id, out var entryId))
                {
                    Get<IHistoryStore>().SetFavorite(entryId, true);
                    _ = Get<HomeViewModel>().RefreshAsync();
                }
                return false;
            case Toasts.KeepAction:
                return false;
            default:
                ShowWindow();
                return true;
        }
    }

    void OnApplied(ApplyResult result)
    {
        if (IsWindowVisible)
        {
            _ = Get<HomeViewModel>().RefreshAsync();
            return;
        }
    }

    /// <summary>
    /// Tray menu or shortcut: change now. The popup says it's working; the open window already
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
            onPhase = (_, phase) => Dispatcher.TryEnqueue(() => popup.SetPhase(phase));
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
            Toasts.ShowError(result.Error ?? "Unknown error.");
    }

    public void ShowWindow()
    {
        if (_window is null)
        {
            EfficiencyMode.Set(false);
            _window = new MainWindow();
            _window.Closed += (_, _) =>
            {
                _window = null;
                if (!_quitting) EnterBackground();
            };
        }
        if (_window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p)
            p.Restore();
        _window.Activate();
        // Windows only lets the foreground app take focus; a tap of Alt makes this process eligible.
        keybd_event(0x12, 0, 0, 0);
        keybd_event(0x12, 0, 2, 0);
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(_window));
    }

    /// <summary>Tray-only mode: free what the window used and run in efficiency mode.</summary>
    static void EnterBackground()
    {
        Get<HomeViewModel>().ReleaseImages();
        Get<HistoryViewModel>().Release();
        Get<CollectionsViewModel>().Release();
        MemoryTrim.Now();
        EfficiencyMode.Set(true);
    }

    void Quit()
    {
        _quitting = true;
        Get<DailyScheduler>().Dispose();
        Get<TrayIconService>().Dispose();
        Get<HotkeyService>().Dispose();
        Get<UpdateService>().ApplyOnExit();
        _window?.Close();
        _services?.Dispose();
        Exit();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(nint hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern void keybd_event(byte vk, byte scan, uint flags, nuint extraInfo);
}
