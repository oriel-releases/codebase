using Oriel.Core.Models;

namespace Oriel.Core.Providers;

public sealed record ImageQuery(string Query, PixelSize TargetSize, int Count = 10);

/// <summary>One page of search results, most relevant first.</summary>
public sealed record SearchPage(IReadOnlyList<PhotoCandidate> Photos, int TotalPages);

/// <summary>
/// A source of random photos. Unsplash today; Pexels / Bing / Wikimedia later.
/// </summary>
public interface IImageProvider
{
    string Name { get; }

    /// <summary>True when the provider has what it needs (e.g. an API key).</summary>
    bool IsConfigured { get; }

    /// <summary>Requests left in the current hourly window, if the provider has told us yet.</summary>
    int? RemainingRequests => null;

    Task<IReadOnlyList<PhotoCandidate>> GetRandomAsync(ImageQuery query, CancellationToken ct);

    /// <summary>Photos matching the words, most relevant first. Pages start at 1.</summary>
    Task<SearchPage> SearchAsync(string query, int page, CancellationToken ct);

    /// <summary>Downloads the photo sized for <paramref name="size"/> as JPEG bytes.</summary>
    Task<byte[]> DownloadAsync(PhotoCandidate photo, PixelSize size, CancellationToken ct);

    /// <summary>Tells the provider the photo was used (required by Unsplash guidelines).</summary>
    Task TrackUsageAsync(PhotoCandidate photo, CancellationToken ct);
}

public enum ProviderErrorKind { MissingKey, Unauthorized, RateLimited, NoResults, Network, BadResponse }

public sealed class ProviderException(ProviderErrorKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ProviderErrorKind Kind { get; } = kind;

    /// <summary>Errors worth retrying later with backoff.</summary>
    public bool IsTransient => Kind is ProviderErrorKind.Network or ProviderErrorKind.RateLimited or ProviderErrorKind.BadResponse;
}
