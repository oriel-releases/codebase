using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Oriel.UI.Services;

public static class ImageLoader
{
    /// <summary>
    /// Decodes a local photo straight to a small bitmap (a 4K wallpaper shown as a 320px thumbnail never exists
    /// at full size in memory); falls back to downloading <paramref name="fallbackUrl"/>. Runs off the UI thread.
    /// </summary>
    public static async Task<Bitmap?> LoadAsync(string? file, string? fallbackUrl, int decodeWidth,
        BitmapInterpolationMode quality = BitmapInterpolationMode.MediumQuality)
    {
        try
        {
            if (file is not null && File.Exists(file))
                return await Task.Run(() =>
                {
                    using var stream = File.OpenRead(file);
                    return Bitmap.DecodeToWidth(stream, decodeWidth, quality);
                });
            if (!string.IsNullOrEmpty(fallbackUrl))
                return await LoadUrlAsync(fallbackUrl, decodeWidth, quality);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            // Broken image: the placeholder shows instead.
        }
        return null;
    }

    public static async Task<Bitmap?> LoadUrlAsync(string url, int decodeWidth,
        BitmapInterpolationMode quality = BitmapInterpolationMode.MediumQuality)
    {
        try
        {
            var bytes = await App.Get<HttpClient>().GetByteArrayAsync(url);
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes);
                return Bitmap.DecodeToWidth(stream, decodeWidth, quality);
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or NotSupportedException or ArgumentException or TaskCanceledException)
        {
            return null;
        }
    }
}
