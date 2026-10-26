using Oriel.Core.Models;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Services;

/// <summary>
/// The picture that goes along with a share link: the wallpaper file already on this computer when there is one, else
/// the photo downloaded once (full quality, at most 2160 wide) into the temp folder.
/// </summary>
static class ShareImage
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>A local image file for <paramref name="entry"/>, or null when it can't be had (the link still goes).</summary>
    public static async Task<string?> FileForAsync(HistoryEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath)) return entry.FilePath;
        var url = PhotoText.UnsplashAt(entry.ThumbnailUrl, 2160) ?? entry.ThumbnailUrl;
        if (string.IsNullOrEmpty(url)) return null;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Oriel", "share");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"oriel-{string.Concat(entry.PhotoId.Where(char.IsLetterOrDigit))}.jpg");
            if (!File.Exists(file)) await File.WriteAllBytesAsync(file, await Http.GetByteArrayAsync(url));
            return file;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
