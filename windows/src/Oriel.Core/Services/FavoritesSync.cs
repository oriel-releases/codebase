using Oriel.Core.Auth;
using Oriel.Core.Models;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

/// <summary>One favorite as the account keeps it: the photo, and whether it's (still) a favorite. Never deleted, so an
/// un-favorite syncs too.</summary>
public sealed record SyncedFavorite(
    string PhotoId, string ImageUrl, string PageUrl, string Photographer, string PhotographerUrl, string? Description,
    bool Favorite, DateTimeOffset UpdatedAt);

/// <summary>
/// The same favorites on every device: starring or un-starring here writes <c>users/{uid}/favorites/{photoId}</c> and
/// rings the other devices; a ring, or opening the app, brings the changes made elsewhere (only those since the last
/// sync, so it stays cheap). A favorite of a photo this computer never showed is added to History, where it can be set
/// (it's downloaded then). The first sync on a device uploads the favorites it already had.
/// </summary>
public sealed class FavoritesSync(AuthService auth, FirebaseClient firebase, IHistoryStore history, ISettingsStore store,
    ILogger<FavoritesSync> log)
{
    readonly SemaphoreSlim _gate = new(1, 1);
    volatile bool _quiet;

    public void Start()
    {
        history.FavoriteChanged += OnFavoriteChanged;
        auth.Changed += (_, _) => { if (auth.IsSignedIn) _ = PullAsync(); };
        _ = PullAsync();
    }

    void OnFavoriteChanged(HistoryEntry entry)
    {
        if (_quiet) return; // came from another device: nothing to send back
        _ = Task.Run(() => PushAsync(entry));
    }

    async Task PushAsync(HistoryEntry entry, bool ring = true)
    {
        if (ToSynced(entry, DateTimeOffset.UtcNow) is not { } favorite) return;
        try
        {
            if (await auth.GetTokensAsync() is not { } tokens) return;
            var device = auth.Current?.SessionId ?? "";
            await firebase.SetFavoriteAsync(tokens, favorite, device);
            if (ring) await firebase.RingFavoritesAsync(tokens, device);
        }
        catch (FirebaseException ex)
        {
            log.LogInformation("Couldn't share the favorite {Photo}: {Reason}", entry.PhotoId, ex.Message);
        }
    }

    /// <summary>Brings here the favorites changed on other devices since the last sync.</summary>
    public async Task PullAsync(CancellationToken ct = default)
    {
        if (!auth.IsSignedIn || !await _gate.WaitAsync(0, ct)) return;
        try
        {
            if (await auth.GetTokensAsync(ct) is not { } tokens) return;
            var state = store.LoadState();
            var since = state.FavoritesSyncedAt;
            var changes = await firebase.GetFavoritesSinceAsync(tokens, since, ct);
            var local = history.List(includeDeleted: true);
            _quiet = true;
            try
            {
                foreach (var change in changes.OrderBy(c => c.UpdatedAt)) Apply(change, local);
            }
            finally
            {
                _quiet = false;
            }
            if (since is null)
            {
                // First sync here: the account gets this device's favorites too.
                var known = changes.Select(c => c.PhotoId).ToHashSet();
                foreach (var mine in history.List().Where(e => e.IsFavorite && !known.Contains(e.PhotoId)).DistinctBy(e => e.PhotoId))
                    await PushAsync(mine, ring: false);
                await firebase.RingFavoritesAsync(tokens, auth.Current?.SessionId ?? "", ct);
            }
            state = store.LoadState();
            state.FavoritesSyncedAt = changes.Count > 0 ? changes.Max(c => c.UpdatedAt) : since ?? DateTimeOffset.UnixEpoch;
            store.SaveState(state);
            if (changes.Count > 0) log.LogInformation("Took {Count} favorite change(s) from your other devices", changes.Count);
        }
        catch (FirebaseException ex)
        {
            log.LogInformation("Couldn't read the shared favorites: {Reason}", ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    void Apply(SyncedFavorite change, IReadOnlyList<HistoryEntry> local)
    {
        var mine = local.Where(e => e.PhotoId == change.PhotoId).ToList();
        if (mine.Count > 0)
        {
            foreach (var e in mine.Where(e => e.IsFavorite != change.Favorite)) history.SetFavorite(e.Id, change.Favorite);
            return;
        }
        if (!change.Favorite) return;
        // A photo never shown here: into History (filed at its own time, so it doesn't become the current wallpaper).
        var newest = local.FirstOrDefault(e => !e.IsDeleted)?.AppliedAt;
        var at = newest is { } n && change.UpdatedAt >= n ? n.AddSeconds(-1) : change.UpdatedAt;
        var id = history.Add(new HistoryEntry
        {
            Provider = "unsplash", PhotoId = change.PhotoId, FilePath = "",
            ThumbnailUrl = change.ImageUrl + (change.ImageUrl.Contains('?') ? "&" : "?") + "w=640&q=80&fm=jpg&fit=max",
            PhotoPageUrl = change.PageUrl, PhotographerName = change.Photographer, PhotographerUrl = change.PhotographerUrl,
            Description = change.Description, Topic = "favorite", AppliedAt = at, IsFavorite = true,
        });
        history.SetFavorite(id, true);
    }

    /// <summary>Only Unsplash photos can be shared between devices (every device fetches its own copy).</summary>
    static SyncedFavorite? ToSynced(HistoryEntry e, DateTimeOffset at) =>
        Uri.TryCreate(e.ThumbnailUrl, UriKind.Absolute, out var image) && image.Host == "images.unsplash.com"
            ? new SyncedFavorite(e.PhotoId, $"https://images.unsplash.com{image.AbsolutePath}", e.PhotoPageUrl, e.PhotographerName,
                e.PhotographerUrl, e.Description, e.IsFavorite, at)
            : null;
}
