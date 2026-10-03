using Oriel.Core;
using Oriel.Core.Models;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

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

    public static async Task ShowAppliedAsync(HistoryEntry entry, AppPaths paths)
    {
        var thumb = await MakeThumbnailAsync(entry, paths);

        var builder = new AppNotificationBuilder()
            .AddArgument(ActionKey, OpenAction)
            .AddText("New wallpaper")
            .AddText(entry.Description is { Length: > 0 } d ? Capitalize(d) : $"Today's {entry.Topic} photo")
            .SetAttributionText($"Photo by {entry.PhotographerName} on Unsplash")
            .AddButton(new AppNotificationButton("Keep").AddArgument(ActionKey, KeepAction))
            .AddButton(new AppNotificationButton("Skip").AddArgument(ActionKey, SkipAction))
            .AddButton(new AppNotificationButton("Favorite").AddArgument(ActionKey, FavoriteAction).AddArgument(IdKey, entry.Id.ToString()));
        if (thumb is not null) builder.SetHeroImage(new Uri(thumb));

        AppNotificationManager.Default.Show(builder.BuildNotification());
    }

    public static void ShowError(string message)
    {
        var builder = new AppNotificationBuilder()
            .AddArgument(ActionKey, OpenAction)
            .AddText("Couldn't change your wallpaper")
            .AddText(message + " Your current wallpaper was kept.");
        AppNotificationManager.Default.Show(builder.BuildNotification());
    }

    /// <summary>Called after a headless change, so scheduled runs notify if the user turned that on.</summary>
    public static void TryShowApplied(ApplyResult result, ISettingsStore settings, AppPaths paths)
    {
        if (!settings.Load().ShowNotifications) return;
        try
        {
            Register(null);
            foreach (var e in result.Entries.Take(1))
                ShowAppliedAsync(e, paths).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // ignore: notification is a nice-to-have
        }
    }

    /// <summary>Toasts reject big local images, so make a small JPEG copy.</summary>
    static async Task<string?> MakeThumbnailAsync(HistoryEntry entry, AppPaths paths)
    {
        try
        {
            var dir = Path.Combine(paths.DataRoot, "thumbs");
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, $"{entry.Provider}_{entry.PhotoId}.jpg");
            if (File.Exists(target)) return target;

            var src = await StorageFile.GetFileFromPathAsync(entry.FilePath);
            using var input = await src.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(input);
            var scale = Math.Min(1.0, 728.0 / decoder.PixelWidth);

            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateForTranscodingAsync(output, decoder);
            encoder.BitmapTransform.ScaledWidth = (uint)(decoder.PixelWidth * scale);
            encoder.BitmapTransform.ScaledHeight = (uint)(decoder.PixelHeight * scale);
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
            await encoder.FlushAsync();

            output.Seek(0);
            await using (var fs = File.Create(target))
                await output.AsStreamForRead().CopyToAsync(fs);
            return target;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string Capitalize(string s) => char.ToUpper(s[0]) + s[1..];
}
