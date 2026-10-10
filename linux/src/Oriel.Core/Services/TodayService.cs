using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

/// <summary>Today's photo from the website's Wallpaper of the day.</summary>
public sealed record TodayPhoto(string Id, string Path, string By, string Handle)
{
    public string ThumbnailUrl(int width) => $"https://images.unsplash.com/{Path}?w={width}&q=75&fm=jpg&auto=format&fit=crop";
    public Uri AuthorUri => new($"https://unsplash.com/@{Handle}?utm_source=oriel&utm_medium=referral");
}

/// <summary>
/// Wallpaper of the day, the same photo as oriel.akbarjon.uz/today. The website publishes the list
/// (<c>/today.json</c>); the pick is the same rule as on the page: days since 1 January 1970 in local time, as a position
/// in the list. The list is kept on disk and fetched again every few days, so it costs one small download now and then.
/// </summary>
public sealed class TodayService(HttpClient http, AppPaths paths, IClock clock, ILogger<TodayService> log)
{
    static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(3);
    IReadOnlyList<TodayPhoto>? _list;
    DateTime _loadedAt;

    /// <summary>Today's photo, or null when the list can't be had (offline on the very first try).</summary>
    public async Task<TodayPhoto?> GetAsync(CancellationToken ct = default)
    {
        var list = await LoadAsync(ct);
        return list.Count == 0 ? null : Pick(list, clock.Now.LocalDateTime);
    }

    /// <summary>The rule shared with the website: whole days since 1 January 1970 (local), going round the list.</summary>
    public static T Pick<T>(IReadOnlyList<T> list, DateTime local)
    {
        var day = (long)(local.Date - DateTime.UnixEpoch.Date).TotalDays;
        return list[(int)(((day % list.Count) + list.Count) % list.Count)];
    }

    async Task<IReadOnlyList<TodayPhoto>> LoadAsync(CancellationToken ct)
    {
        if (_list is { Count: > 0 } cached && DateTime.UtcNow - _loadedAt < RefreshAfter) return cached;
        var file = Path.Combine(paths.DataRoot, "today.json");
        var fresh = File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < RefreshAfter;
        if (!fresh)
        {
            try
            {
                var json = await http.GetStringAsync(OrielLinks.TodayList, ct);
                if (Parse(json).Count > 0) await File.WriteAllTextAsync(file, json, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                log.LogInformation("Couldn't get the Wallpaper of the day list: {Reason}", ex.Message);
            }
        }
        try
        {
            _list = File.Exists(file) ? Parse(await File.ReadAllTextAsync(file, ct)) : [];
        }
        catch (IOException)
        {
            _list = [];
        }
        _loadedAt = DateTime.UtcNow;
        return _list;
    }

    internal static IReadOnlyList<TodayPhoto> Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("photos").EnumerateArray()
                .Select(p => new TodayPhoto(Str(p, "id"), Str(p, "path"), Str(p, "by"), Str(p, "handle")))
                .Where(p => p.Id.Length > 0 && p.Path.Length > 0)
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
