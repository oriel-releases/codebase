using Oriel.Core.Auth;
using Oriel.Core.Models;
using Oriel.Core.Storage;
using Oriel.Core.Wallpaper;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

/// <summary>Shares the wallpaper that was just applied with the account's other devices.</summary>
public interface ISyncPublisher
{
    Task PublishAsync(HistoryEntry entry, CancellationToken ct = default);
}

/// <summary>
/// Writes <c>users/{uid}/sync/current</c> so the Oriel browser extension can show the same photo. Only the Unsplash
/// photo (ID and links) is shared, never the image file: each device builds its own crop. Best effort: being offline
/// or signed out never affects changing the wallpaper.
/// </summary>
public sealed class SyncPublisher(AuthService auth, FirebaseClient firebase, IHistoryStore history, ILogger<SyncPublisher> log,
    IAccentSampler? accent = null, Settings.ISettingsStore? settings = null) : ISyncPublisher
{
    const string Source = "windows";
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public Task PublishAsync(HistoryEntry entry, CancellationToken ct = default) => RunAsync(entry, onlyIfMissing: false, ct);

    /// <summary>
    /// Publishes the newest history entry if the account has no shared wallpaper yet, so a freshly connected extension
    /// has something to show. An existing one is left alone: it may come from another device and be newer.
    /// </summary>
    public Task PublishLatestIfMissingAsync(CancellationToken ct = default) =>
        history.List().FirstOrDefault() is { } latest ? RunAsync(latest, onlyIfMissing: true, ct) : Task.CompletedTask;

    async Task RunAsync(HistoryEntry entry, bool onlyIfMissing, CancellationToken ct)
    {
        if (!auth.IsSignedIn)
        {
            log.LogInformation("Not sharing the wallpaper: not signed in");
            return;
        }
        if (ToDocument(entry) is not { } document)
        {
            log.LogInformation("Not sharing the wallpaper: {Photo} isn't an Unsplash photo", entry.PhotoId);
            return;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var tokens = await auth.GetTokensAsync(timeout.Token);
            if (tokens is null) return;
            if (onlyIfMissing && await firebase.HasCurrentWallpaperAsync(tokens, timeout.Token)) return;
            await firebase.SetCurrentWallpaperAsync(tokens, document with { Color = await SampleColorAsync(entry) }, timeout.Token);
            // The doorbell: devices listening hear about it now instead of at their next poll.
            if (!onlyIfMissing)
            {
                try
                {
                    await firebase.RingAsync(tokens, entry.PhotoId, Source, auth.Current?.SessionId ?? "", timeout.Token);
                }
                catch (Exception ex)
                {
                    log.LogInformation("Couldn't ring the other devices (they still poll): {Reason}", ex.Message);
                }
            }
            log.LogInformation("Shared the wallpaper {Photo} with your other devices", entry.PhotoId);
            // Sharing with friends: your follow page shows the new wallpaper too.
            if (!onlyIfMissing && settings?.Load().MyFollowCode is { } code && ShareLink.Photo(entry) is { } photo)
                await firebase.SetFollowCodePhotoAsync(tokens, code, photo, timeout.Token);
        }
        catch (Exception ex)
        {
            // Offline, rejected by the rules, or anything unexpected: the wallpaper has already changed, so only note it.
            log.LogWarning("Couldn't share the wallpaper {Photo}: {Reason}", entry.PhotoId, ex.Message);
        }
    }

    async Task<string?> SampleColorAsync(HistoryEntry entry)
    {
        if (accent is null) return null;
        try
        {
            return await accent.SampleAsync(entry.FilePath);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Couldn't read the accent color of {Photo}", entry.PhotoId);
            return null;
        }
    }

    /// <summary>Only Unsplash photos can be shared (the extension hotlinks them); anything else is skipped.</summary>
    static CurrentWallpaper? ToDocument(HistoryEntry e) =>
        Uri.TryCreate(e.ThumbnailUrl, UriKind.Absolute, out var uri) && uri.Host == "images.unsplash.com"
            ? new CurrentWallpaper(e.PhotoId, e.ThumbnailUrl, e.PhotoPageUrl, e.PhotographerName, e.PhotographerUrl, e.Description, e.Topic, Source)
            : null;
}
