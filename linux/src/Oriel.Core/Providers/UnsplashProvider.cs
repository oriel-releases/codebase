using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Oriel.Core.Models;
using Oriel.Core.Security;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Providers;

public sealed class UnsplashProvider(HttpClient http, IApiKeyStore keys, ILogger<UnsplashProvider> log) : IImageProvider
{
    public const string ApiBase = "https://api.unsplash.com/";
    const string Utm = "utm_source=oriel&utm_medium=referral";

    // Unsplash demo apps get 50 req/hour. Remember what the API tells us and stop early.
    int? _remaining;
    DateTimeOffset _rateLimitedUntil = DateTimeOffset.MinValue;

    public string Name => "unsplash";
    public bool IsConfigured => keys.HasKey;
    public int? RemainingRequests => _remaining;

    public async Task<IReadOnlyList<PhotoCandidate>> GetRandomAsync(ImageQuery query, CancellationToken ct)
    {
        // Relevance first: pick from the best matches (a random one of the first few pages, so
        // days differ) instead of Unsplash's loosely-related random endpoint.
        var page = Random.Shared.Next(1, 4);
        var found = await SearchAsync(query.Query, page, ct);
        if (found.Photos.Count == 0 && page > 1) found = await SearchAsync(query.Query, 1, ct);
        if (found.Photos.Count == 0)
            throw new ProviderException(ProviderErrorKind.NoResults, "No photos found for that topic.");

        var shuffled = found.Photos.ToArray();
        Random.Shared.Shuffle(shuffled);
        return shuffled;
    }

    public async Task<SearchPage> SearchAsync(string query, int page, CancellationToken ct)
    {
        var url = $"{ApiBase}search/photos?orientation=landscape&content_filter=high&order_by=relevant" +
                  $"&per_page=30&page={Math.Max(1, page)}&query={Uri.EscapeDataString(query)}";

        using var resp = await SendApiAsync(url, ct);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        try
        {
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            var photos = root.GetProperty("results").EnumerateArray().Select(Parse).ToList();
            var pages = root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt32() : 1;
            return new SearchPage(photos, pages);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ProviderException(ProviderErrorKind.BadResponse, "Unsplash returned data Oriel couldn't read.", ex);
        }
    }

    public async Task<byte[]> DownloadAsync(PhotoCandidate photo, PixelSize size, CancellationToken ct)
    {
        var url = BuildDownloadUrl(photo.RawUrl, size);
        try
        {
            // Image CDN (images.unsplash.com) doesn't count against the API rate limit and needs no key.
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsByteArrayAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(ProviderErrorKind.Network, "Couldn't download the photo.", ex);
        }
    }

    public async Task TrackUsageAsync(PhotoCandidate photo, CancellationToken ct)
    {
        if (photo.DownloadTrackingUrl is null) return;
        try
        {
            using var _ = await SendApiAsync(photo.DownloadTrackingUrl, ct);
        }
        catch (ProviderException ex)
        {
            log.LogWarning("Download tracking failed for {PhotoId}: {Message}", photo.Id, ex.Message);
        }
    }

    internal static string BuildDownloadUrl(string rawUrl, PixelSize size)
    {
        var sep = rawUrl.Contains('?') ? '&' : '?';
        return $"{rawUrl}{sep}w={size.Width}&h={size.Height}&fit=crop&crop=entropy&q=90&fm=jpg";
    }

    async Task<HttpResponseMessage> SendApiAsync(string url, CancellationToken ct)
    {
        var key = keys.Get();
        if (string.IsNullOrWhiteSpace(key))
            throw new ProviderException(ProviderErrorKind.MissingKey, "This build of Oriel has no Unsplash access key.");

        if (DateTimeOffset.UtcNow < _rateLimitedUntil)
            throw new ProviderException(ProviderErrorKind.RateLimited, "Unsplash hourly limit reached. Will try again later.");

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", key);
        req.Headers.Add("Accept-Version", "v1");

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(ProviderErrorKind.Network, "Can't reach Unsplash. Are you offline?", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ProviderException(ProviderErrorKind.Network, "Unsplash took too long to answer.", ex);
        }

        ReadRateLimit(resp);

        if (resp.IsSuccessStatusCode) return resp;

        var status = resp.StatusCode;
        resp.Dispose();
        throw status switch
        {
            HttpStatusCode.Unauthorized => new ProviderException(ProviderErrorKind.Unauthorized, "Unsplash rejected Oriel's access key."),
            HttpStatusCode.Forbidden when _remaining is 0 => RateLimited(),
            (HttpStatusCode)429 => RateLimited(),
            HttpStatusCode.Forbidden => new ProviderException(ProviderErrorKind.Unauthorized, "Unsplash refused the request (403)."),
            HttpStatusCode.NotFound => new ProviderException(ProviderErrorKind.NoResults, "No photos found for that topic."),
            _ => new ProviderException(ProviderErrorKind.BadResponse, $"Unsplash returned HTTP {(int)status}."),
        };
    }

    ProviderException RateLimited()
    {
        // Limits reset on the hour.
        var now = DateTimeOffset.UtcNow;
        _rateLimitedUntil = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(1);
        return new ProviderException(ProviderErrorKind.RateLimited, "Unsplash hourly limit reached. Will try again later.");
    }

    void ReadRateLimit(HttpResponseMessage resp)
    {
        if (resp.Headers.TryGetValues("X-Ratelimit-Remaining", out var values) &&
            int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining))
        {
            _remaining = remaining;
            if (remaining <= 0) RateLimited();
        }
    }

    internal static PhotoCandidate Parse(JsonElement p)
    {
        var user = p.GetProperty("user");
        var urls = p.GetProperty("urls");
        var links = p.GetProperty("links");

        string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        return new PhotoCandidate
        {
            Provider = "unsplash",
            Id = p.GetProperty("id").GetString()!,
            Width = p.GetProperty("width").GetInt32(),
            Height = p.GetProperty("height").GetInt32(),
            Likes = p.TryGetProperty("likes", out var likes) && likes.ValueKind == JsonValueKind.Number ? likes.GetInt32() : 0,
            Description = JoinNonEmpty(Str(p, "alt_description"), Str(p, "description")),
            Color = Str(p, "color"),
            RawUrl = urls.GetProperty("raw").GetString()!,
            ThumbnailUrl = Str(urls, "small") ?? urls.GetProperty("thumb").GetString()!,
            PhotoPageUrl = AddUtm(links.GetProperty("html").GetString()!),
            PhotographerName = Str(user, "name") ?? Str(user, "username") ?? "Unknown",
            PhotographerUrl = AddUtm(user.GetProperty("links").GetProperty("html").GetString()!),
            DownloadTrackingUrl = Str(links, "download_location"),
        };
    }

    static string? JoinNonEmpty(params string?[] parts)
    {
        var kept = parts.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return kept.Count == 0 ? null : string.Join(" — ", kept);
    }

    static string AddUtm(string url) => url + (url.Contains('?') ? '&' : '?') + Utm;
}
