namespace Oriel.Core.Models;

/// <summary>An Unsplash photo as the website's share and follow pages show it: its ID, image path and photographer.</summary>
public sealed record SharedPhoto(string Id, string Path, string Photographer, string Handle);

/// <summary>
/// The links a user sends a friend. They only carry an ID (<c>/share/AB3K9QXZ</c>, <c>/follow/K7MQ2XPA</c>); the page
/// reads the rest from Firestore once the friend has signed in on the website.
/// </summary>
public static class ShareLink
{
    /// <summary>A shared wallpaper's page (its <c>shares/{id}</c> document).</summary>
    public static string ForShare(string id) => $"{OrielLinks.Share}/{id}";

    /// <summary>Someone's follow page (their <c>codes/{code}</c> document).</summary>
    public static string ForFollow(string code) => $"{OrielLinks.Follow}/{code}";

    /// <summary>The photo as a page shows it. Null for photos that can't be shared (only Unsplash photos have a public image).</summary>
    public static SharedPhoto? Photo(HistoryEntry entry)
    {
        if (!Uri.TryCreate(entry.ThumbnailUrl, UriKind.Absolute, out var image) || image.Host != "images.unsplash.com") return null;
        var path = image.AbsolutePath.TrimStart('/');
        if (path.Length is < 6 or > 120 || !path.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return null;
        // The photographer's Unsplash handle, so the page can link their name to their profile.
        var handle = Uri.TryCreate(entry.PhotographerUrl, UriKind.Absolute, out var profile) && profile.Host == "unsplash.com" &&
            profile.AbsolutePath.TrimStart('/') is ['@', .. var h] && h.Length is > 0 and <= 40 &&
            h.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? h : "";
        var by = entry.PhotographerName.Length <= 80 ? entry.PhotographerName : entry.PhotographerName[..80];
        return new SharedPhoto(entry.PhotoId, path, by, handle);
    }

    /// <summary>A Google profile picture to show on those pages, or "" (any other address is never stored).</summary>
    public static string Picture(string? url) =>
        url is { Length: <= 400 } && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host == "lh3.googleusercontent.com"
            ? url : "";

    /// <summary>A first name that is safe to put in a link and show as text: no control characters or angle brackets, at most 30 characters.</summary>
    public static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var first = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var clean = new string(first.Where(c => !char.IsControl(c) && c is not '<' and not '>').ToArray());
        return clean.Length <= 30 ? clean : clean[..30];
    }
}
