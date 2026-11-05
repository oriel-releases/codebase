using System.Net;
using Oriel.Core.Models;

namespace Oriel.Core.Providers;

/// <summary>
/// AI wallpapers this app doesn't make, but shows when another device of the account (the Mac app) sets one: Pollinations
/// keeps each at a public link (media.pollinations.ai), synced like an Unsplash photo. Downloading it needs no key.
/// </summary>
public sealed class AiPhotoLinks(HttpClient http)
{
    public const string Provider = "ai";
    const string MediaHost = "media.pollinations.ai";

    /// <summary>True for an AI wallpaper's photo ID ("ai-" and its Pollinations media id).</summary>
    public static bool IsAiPhoto(string photoId) => photoId.StartsWith("ai-", StringComparison.Ordinal);

    /// <summary>True for a link to a picture Pollinations keeps (media.pollinations.ai/{id}).</summary>
    public static bool IsMediaLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == MediaHost;

    /// <summary>
    /// The photo record of an AI wallpaper from its public link (it arrives through <c>sync/current</c> or a favorite).
    /// Null when the link isn't one of Pollinations'.
    /// </summary>
    public static PhotoCandidate? FromLink(string link, string? prompt, PixelSize? size = null)
    {
        if (!IsMediaLink(link)) return null;
        var mediaId = new Uri(link).AbsolutePath.Trim('/');
        if (mediaId.Length is < 16 or > 128 || !mediaId.All(Uri.IsHexDigit)) return null;
        var clean = $"https://{MediaHost}/{mediaId}";
        return new PhotoCandidate
        {
            Provider = Provider,
            Id = "ai-" + mediaId,
            Width = size?.Width ?? 0,
            Height = size?.Height ?? 0,
            Description = prompt,
            RawUrl = clean,
            ThumbnailUrl = clean,
            PhotoPageUrl = clean,
            PhotographerName = "Pollinations AI",
            PhotographerUrl = "https://pollinations.ai",
        };
    }

    /// <summary>The picture behind an AI wallpaper's public link. Pollinations keeps it 30 days after last use.</summary>
    public async Task<byte[]> DownloadAsync(PhotoCandidate photo, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        try
        {
            using var resp = await http.GetAsync(photo.RawUrl, timeout.Token);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                throw new ProviderException(ProviderErrorKind.NoResults, "This AI wallpaper isn't kept online any more.");
            if (!resp.IsSuccessStatusCode)
                throw new ProviderException(ProviderErrorKind.BadResponse, "Couldn't get the AI wallpaper. Try again in a moment.");
            var bytes = await resp.Content.ReadAsByteArrayAsync(timeout.Token);
            return IsImage(bytes) ? bytes
                : throw new ProviderException(ProviderErrorKind.BadResponse, "The AI wallpaper's link didn't send back a picture.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ProviderException(ProviderErrorKind.Network, "Couldn't download the AI wallpaper. Check your connection.", ex);
        }
    }

    public static bool IsImage(byte[] b) => b.Length > 1024 && (IsJpeg(b) || IsPng(b));
    public static bool IsJpeg(byte[] b) => b.Length > 2 && b[0] == 0xFF && b[1] == 0xD8;
    public static bool IsPng(byte[] b) => b.Length > 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
}
