namespace Oriel.Core.Models;

/// <summary>The link a user sends a friend: the website's share page, which opens Oriel to set the photo as wallpaper.</summary>
public static class ShareLink
{
    /// <summary>Null for photos that can't be shared (only Unsplash photos have a public image the page can show).</summary>
    /// <param name="from">Who is sharing: their first name, shown to the friend ("Akbar shared a wallpaper with you").</param>
    public static string? Build(HistoryEntry entry, string? from = null)
    {
        if (!Uri.TryCreate(entry.ThumbnailUrl, UriKind.Absolute, out var image) || image.Host != "images.unsplash.com") return null;
        var path = image.AbsolutePath.TrimStart('/');
        var link = $"{OrielLinks.Share}?id={Uri.EscapeDataString(entry.PhotoId)}&i={Uri.EscapeDataString(path)}&by={Uri.EscapeDataString(entry.PhotographerName)}";
        // The photographer's Unsplash handle, so the page can link their name to their profile.
        if (Uri.TryCreate(entry.PhotographerUrl, UriKind.Absolute, out var profile) && profile.Host == "unsplash.com" &&
            profile.AbsolutePath.TrimStart('/') is ['@', .. var handle] && handle.Length is > 0 and <= 40 &&
            handle.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
            link += $"&u={Uri.EscapeDataString(handle)}";
        return CleanName(from) is { Length: > 0 } name ? $"{link}&from={Uri.EscapeDataString(name)}" : link;
    }

    /// <summary>A first name that is safe to put in a link and show as text: no control characters or angle brackets, at most 30 characters.</summary>
    public static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var first = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var clean = new string(first.Where(c => !char.IsControl(c) && c is not '<' and not '>').ToArray());
        return clean.Length <= 30 ? clean : clean[..30];
    }
}
