using System.ComponentModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;

namespace Oriel.UI.ViewModels;

/// <summary>
/// A photo opened on its own page (<see cref="Views.PhotoDetailView"/>) from Explore, History or Favorites: the card it
/// came from, seen bigger, with what's known about it and the same actions. The same as on Windows.
/// </summary>
public interface IPhotoDetail : INotifyPropertyChanged
{
    /// <summary>The card's picture, already loaded: shown at once (and flown into place) while the big one loads.</summary>
    Bitmap? Preview { get; }
    /// <summary>Width over height.</summary>
    double AspectRatio { get; }
    string Headline { get; }
    /// <summary>"6000 × 4000 · 812 likes", or "Wed, 21 Oct 2026 · nature".</summary>
    string Facts { get; }
    string PhotographerName { get; }
    /// <summary>The line under the name on a card: the topic, or "Wed, 21 Oct 2026 · nature".</summary>
    string Subtitle { get; }
    Uri PhotographerUri { get; }
    Uri PhotoPageUri { get; }
    string ButtonText { get; }
    bool IsIdle { get; }
    IAsyncRelayCommand SetCommand { get; }
    bool CanFavorite { get; }
    bool IsFavorite { get; set; }
    /// <summary>The photo as History keeps it, for a share link (only Unsplash photos can be shared).</summary>
    Oriel.Core.Models.HistoryEntry ShareEntry { get; }
    /// <summary>A History photo: it can be deleted from here, like on the phone.</summary>
    bool CanDelete { get; }
    /// <summary>History photos have a file on this computer.</summary>
    bool CanOpenFolder { get; }
    void OpenFolder();
    /// <summary>Asks with <paramref name="confirm"/> first; true once it's gone.</summary>
    Task<bool> RemoveAsync(Func<string, Task<bool>> confirm);
    /// <summary>Small facts shown as pills on its page, each with its icon: "6000 × 4000", "812", "Nature".</summary>
    IReadOnlyList<PhotoFact> Chips { get; }
    /// <summary>The photo, sharp at <paramref name="pixelWidth"/> real screen pixels; the caller disposes it.</summary>
    Task<Bitmap?> LoadLargeAsync(int pixelWidth);
}

public enum PhotoFactKind { Size, Likes, Date, Topic }

/// <summary>One fact on a photo's page, shown as a small pill: what it is (its icon) and its words.</summary>
public sealed record PhotoFact(PhotoFactKind Kind, string Text)
{
    /// <summary>Its icon (from Styles/Icons.axaml).</summary>
    public Avalonia.Media.Geometry? Icon => Avalonia.Application.Current is { } app
        ? Avalonia.Controls.ResourceNodeExtensions.FindResource(app, Kind switch
        {
            PhotoFactKind.Size => "IconImage",
            PhotoFactKind.Likes => "IconHeart",
            PhotoFactKind.Date => "IconClock",
            _ => "IconCompass",
        }) as Avalonia.Media.Geometry
        : null;

    /// <summary>Likes the way people read them at a glance: 812, 1.2K, 34K, 1.5M.</summary>
    public static string Count(int n) => n switch
    {
        < 1_000 => n.ToString("N0"),
        < 10_000 => $"{n / 1000.0:0.#}K",
        < 1_000_000 => $"{n / 1000}K",
        _ => $"{n / 1_000_000.0:0.#}M",
    };
}

static class PhotoText
{
    /// <summary>What History calls where a photo came from, in words: "synced" → "From your other devices".</summary>
    public static string? Topic(string? topic) => topic?.Trim() switch
    {
        null or "" => null,
        "synced" => "From your other devices",
        "shared" => "Shared with you",
        "search" => "Search",
        "explore" => "Explore",
        var t when t.StartsWith("from ", StringComparison.OrdinalIgnoreCase) => "From " + t[5..],
        var t => char.ToUpper(t[0]) + t[1..],
    };

    /// <summary>
    /// An Unsplash picture at <paramref name="width"/> pixels, from any link to it (a small thumbnail included); null when
    /// it isn't one.
    /// </summary>
    public static string? UnsplashAt(string? url, int width)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "images.unsplash.com") return null;
        var keep = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.StartsWith("ixid=", StringComparison.Ordinal) || p.StartsWith("ixlib=", StringComparison.Ordinal));
        return $"https://images.unsplash.com{uri.AbsolutePath}?{string.Join('&', keep.Append($"w={width}&q=90&fm=jpg&fit=max"))}";
    }

    /// <summary>The first part of a description ("a foggy forest — Morning walk" → "A foggy forest"), else the topic.</summary>
    public static string Headline(string? description, string? topic)
    {
        var text = description?.Split(" — ")[0].Trim();
        if (string.IsNullOrEmpty(text)) text = topic?.Trim();
        if (string.IsNullOrEmpty(text)) return "Wallpaper";
        return char.ToUpper(text[0]) + text[1..];
    }
}
