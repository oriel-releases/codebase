using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Oriel.UI.Services;

public static class ImageLoader
{
    /// <summary>Loads a local file without locking it; falls back to a remote URL. Call on the UI thread.</summary>
    public static async Task<ImageSource?> LoadAsync(string? file, string? fallbackUrl, int decodeWidth,
        DecodePixelType type = DecodePixelType.Logical)
    {
        try
        {
            if (file is not null && File.Exists(file))
            {
                var bmp = new BitmapImage { DecodePixelWidth = decodeWidth, DecodePixelType = type };
                byte[] bytes = await File.ReadAllBytesAsync(file);
                using var ms = new MemoryStream(bytes);
                await bmp.SetSourceAsync(ms.AsRandomAccessStream());
                return bmp;
            }
            if (!string.IsNullOrEmpty(fallbackUrl))
                return new BitmapImage(new Uri(fallbackUrl)) { DecodePixelWidth = decodeWidth, DecodePixelType = type };
        }
        catch (Exception)
        {
            // Broken image -> just show the placeholder.
        }
        return null;
    }

    /// <summary>Downloads and decodes a picture before handing it over, so it shows the moment it's set. Call on the UI thread.</summary>
    public static async Task<ImageSource?> LoadUrlAsync(string url, int decodeWidth, DecodePixelType type = DecodePixelType.Logical)
    {
        try
        {
            var bytes = await App.Get<HttpClient>().GetByteArrayAsync(url);
            var bmp = new BitmapImage { DecodePixelWidth = decodeWidth, DecodePixelType = type };
            using var ms = new MemoryStream(bytes);
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void ShowInExplorer(string file)
    {
        if (File.Exists(file))
            Process.Start("explorer.exe", $"/select,\"{file}\"");
        else if (Path.GetDirectoryName(file) is { } dir && Directory.Exists(dir))
            OpenFolder(dir);
    }

    public static void OpenFolder(string dir)
    {
        Directory.CreateDirectory(dir);
        Process.Start("explorer.exe", $"\"{dir}\"");
    }
}
