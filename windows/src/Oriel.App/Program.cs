using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Windows.AppLifecycle;
using Oriel.Core.Scheduling;
using Velopack;

namespace Oriel.UI;

public static class Program
{
    public const string InstanceKey = "Oriel.Main";

    [STAThread]
    static int Main(string[] args)
    {
        // Must run first: handles Setup/Update.exe hooks (and exits for those). A headless
        // scheduled run must not restart itself into a pending update, so only the app applies them.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(!Cli.IsCli(args))
            .OnBeforeUninstallFastCallback(_ => RemoveSystemIntegration())
            .Run();

        if (Cli.IsCli(args)) return Cli.Run(args);

        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Single instance: a second launch (Start menu, toast click, Run key) wakes the first one.
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!main.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            // Redirecting must not block the STA thread's message pump.
            Task.Run(() => main.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    /// <summary>Uninstall: drop the Run key and scheduled tasks. Settings and photos are kept.</summary>
    static void RemoveSystemIntegration()
    {
        try { new StartupRegistration().Set(false, ""); } catch (Exception) { }
        try { new TaskSchedulerRegistration(NullLogger<TaskSchedulerRegistration>.Instance).Unregister(); } catch (Exception) { }
    }
}
