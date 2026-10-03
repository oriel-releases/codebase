using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Oriel.UI.Services;

public static class ImageLoader
{
    /// <summary>Loads a local file without locking it; falls back to a remote URL. Call on the UI thread.</summary>
    public static async Task<ImageSource?> LoadAsync(string? file, string? fallbackUrl, int decodeWidth)
    {
        try
        {
            if (file is not null && File.Exists(file))
            {
                var bmp = new BitmapImage { DecodePixelWidth = decodeWidth, DecodePixelType = DecodePixelType.Logical };
                byte[] bytes = await File.ReadAllBytesAsync(file);
                using var ms = new MemoryStream(bytes);
                await bmp.SetSourceAsync(ms.AsRandomAccessStream());
                return bmp;
            }
            if (!string.IsNullOrEmpty(fallbackUrl))
                return new BitmapImage(new Uri(fallbackUrl)) { DecodePixelWidth = decodeWidth, DecodePixelType = DecodePixelType.Logical };
        }
        catch (Exception)
        {
            // Broken image -> just show the placeholder.
        }
        return null;
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
