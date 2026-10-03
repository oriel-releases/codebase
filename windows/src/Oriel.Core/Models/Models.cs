namespace Oriel.Core.Models;

/// <summary>A photo offered by an image provider, before it is downloaded.</summary>
public sealed record PhotoCandidate
{
    public required string Provider { get; init; }
    public required string Id { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public int Likes { get; init; }
    public string? Description { get; init; }
    public string? Color { get; init; }

    /// <summary>Base URL that accepts sizing params (Unsplash "raw").</summary>
    public required string RawUrl { get; init; }
    public required string ThumbnailUrl { get; init; }
    public required string PhotoPageUrl { get; init; }
    public required string PhotographerName { get; init; }
    public required string PhotographerUrl { get; init; }

    /// <summary>Provider endpoint that must be pinged when the photo is used (Unsplash download tracking).</summary>
    public string? DownloadTrackingUrl { get; init; }

    public bool IsLandscape => Width > Height;
}

public enum WallpaperFit { Fill, Fit, Stretch, Tile, Center, Span }

public enum MultiMonitorMode { SameOnAll, PerMonitor }

public sealed record MonitorInfo(string DevicePath, int Left, int Top, int Width, int Height)
{
    public bool IsPrimary => Left == 0 && Top == 0;
}

public sealed record PixelSize(int Width, int Height);

/// <summary>A wallpaper that Oriel has applied at some point.</summary>
public sealed record HistoryEntry
{
    public long Id { get; init; }
    public required string Provider { get; init; }
    public required string PhotoId { get; init; }
    public required string FilePath { get; init; }
    public required string ThumbnailUrl { get; init; }
    public required string PhotoPageUrl { get; init; }
    public required string PhotographerName { get; init; }
    public required string PhotographerUrl { get; init; }
    public string? Description { get; init; }
    public string? Topic { get; init; }
    public string? MonitorPath { get; init; }
    public required DateTimeOffset AppliedAt { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsDeleted { get; init; }
}
