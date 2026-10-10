using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.Foundation;

namespace Oriel.UI;

/// <summary>Native Windows toasts via the Windows App SDK (works unpackaged).</summary>
static class Toasts
{
    public const string ActionKey = "action";
    public const string IdKey = "id";
    public const string OpenAction = "open";
    public const string KeepAction = "keep";
    public const string SkipAction = "skip";
    public const string FavoriteAction = "favorite";

    static bool _registered;

    public static void Register(TypedEventHandler<AppNotificationManager, AppNotificationActivatedEventArgs>? onInvoked)
    {
        if (_registered) return;
        try
        {
            if (onInvoked is not null) AppNotificationManager.Default.NotificationInvoked += onInvoked;
            AppNotificationManager.Default.Register("Oriel",
                new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png")));
            _registered = true;
        }
        catch (Exception)
        {
            // Toasts are optional; never block the app over them.
        }
    }
}
