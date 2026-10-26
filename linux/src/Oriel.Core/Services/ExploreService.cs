using System.Text.Json;
using Oriel.Core.Models;
using Oriel.Core.Providers;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

/// <summary>One photo of the Explore feed, with the interest or Unsplash topic it was found under.</summary>
public sealed record ExplorePhoto(PhotoCandidate Photo, string? Topic);

/// <summary>This hour's Explore photos (UTC hour they were picked for).</summary>
public sealed record ExploreFeed(DateTimeOffset Hour, IReadOnlyList<ExplorePhoto> Photos)
{
    public static readonly ExploreFeed Empty = new(DateTimeOffset.MinValue, []);
}

/// <summary>
/// The Explore page's photos: about 150 wallpapers picked once an hour for every Oriel user by the public
/// oriel-releases/explore repo (metadata only, in Unsplash's own shape), so scrolling costs no Unsplash requests at all.
/// The feed is kept on disk and fetched again once a newer hour is due.
/// </summary>
public sealed class ExploreService(HttpClient http, AppPaths paths, IClock clock, ILogger<ExploreService> log)
{
    /// <summary>The hour's feed is published a few minutes past the hour (sometimes later): until then, ask again this often.</summary>
    static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);
    readonly SemaphoreSlim _gate = new(1, 1);
    ExploreFeed? _feed;
    DateTimeOffset _triedAt = DateTimeOffset.MinValue;

    /// <summary>
    /// This hour's photos; the last ones kept when the new ones can't be had; empty when there never were any.
    /// <paramref name="force"/> (a refresh) asks again even if the last try failed moments ago.
    /// </summary>
    public async Task<ExploreFeed> GetAsync(CancellationToken ct = default, bool force = false)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var file = Path.Combine(paths.DataRoot, "explore.json");
            if (_feed is null && File.Exists(file))
            {
                try { _feed = Parse(await File.ReadAllTextAsync(file, ct)); }
                catch (IOException) { }
            }
            var now = clock.Now;
            if (IsCurrent(_feed, now) || (!force && now - _triedAt < RetryAfter)) return _feed ?? ExploreFeed.Empty;
            _triedAt = now;
            try
            {
                var json = await http.GetStringAsync(OrielLinks.ExploreFeed, ct);
                var fresh = Parse(json);
                if (fresh.Photos.Count > 0 && fresh.Hour >= (_feed?.Hour ?? DateTimeOffset.MinValue))
                {
                    _feed = fresh;
                    await File.WriteAllTextAsync(file, json, ct);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                log.LogInformation("Couldn't get the Explore feed: {Reason}", ex.Message);
            }
            return _feed ?? ExploreFeed.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The feed is for this hour (UTC) or later: nothing newer to fetch yet.</summary>
    internal static bool IsCurrent(ExploreFeed? feed, DateTimeOffset now)
    {
        if (feed is not { Photos.Count: > 0 }) return false;
        var utc = now.ToUniversalTime();
        return feed.Hour >= new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    /// <summary>The computers' half of the feed (<c>landscape</c>); photos it can't read are left out.</summary>
    internal static ExploreFeed Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var hour = root.TryGetProperty("hour", out var h) && h.TryGetDateTimeOffset(out var at) ? at : DateTimeOffset.MinValue;
            if (!root.TryGetProperty("landscape", out var list) || list.ValueKind != JsonValueKind.Array) return ExploreFeed.Empty;
            var photos = new List<ExplorePhoto>();
            foreach (var p in list.EnumerateArray())
            {
                try
                {
                    var photo = UnsplashProvider.Parse(p);
                    if (!photo.RawUrl.StartsWith("https://images.unsplash.com/", StringComparison.Ordinal)) continue;
                    var topic = p.TryGetProperty("topic", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                    photos.Add(new ExplorePhoto(photo, topic));
                }
                catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException) { }
            }
            return new ExploreFeed(hour, photos);
        }
        catch (JsonException)
        {
            return ExploreFeed.Empty;
        }
    }
}
