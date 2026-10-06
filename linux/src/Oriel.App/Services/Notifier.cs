using Oriel.Core.Platform;

namespace Oriel.UI.Services;

/// <summary>Desktop notifications through <c>notify-send</c> (libnotify). Optional: a missing tool is not an error.</summary>
static class Notifier
{
    public static void ShowError(string message) =>
        Show("Couldn't change your wallpaper", message + " Your current wallpaper was kept.");

    public static void Show(string title, string body) =>
        Proc.Start("notify-send", "--app-name=Oriel", "--icon=oriel", "--urgency=low", "--", title, body);
}
