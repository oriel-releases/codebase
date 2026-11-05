using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Oriel.UI.Services;

/// <summary>A photo's pixels, decoded once at a modest size, to read the color at any spot (Home's eyedropper).</summary>
public sealed class PhotoPixels
{
    readonly byte[] _bgra;
    public int Width { get; }
    public int Height { get; }

    PhotoPixels(byte[] bgra, int width, int height) => (_bgra, Width, Height) = (bgra, width, height);

    /// <summary>Null when the file is gone or can't be decoded.</summary>
    public static async Task<PhotoPixels?> LoadAsync(string path, uint maxWidth = 1000)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var scale = Math.Min(1.0, (double)maxWidth / decoder.PixelWidth);
            var width = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * scale));
            var height = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale));
            var transform = new BitmapTransform
            {
                ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            return new PhotoPixels(pixels.DetachPixelData(), (int)width, (int)height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The pixel at a spot given as 0..1 of the width and height.</summary>
    public (int X, int Y) PixelAt(double fx, double fy) =>
        (Math.Clamp((int)(fx * Width), 0, Width - 1), Math.Clamp((int)(fy * Height), 0, Height - 1));

    /// <summary>One pixel's color; outside the photo, transparent.</summary>
    public Windows.UI.Color Color(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return default;
        var i = (y * Width + x) * 4;
        return Windows.UI.Color.FromArgb(0xFF, _bgra[i + 2], _bgra[i + 1], _bgra[i]);
    }

    /// <summary>
    /// "#RRGGBB" of the one pixel at a spot given as 0..1 of the width and height: exactly the one boxed in the
    /// magnifier's middle (an average of the pixels around it was a color you couldn't point at).
    /// </summary>
    public string HexAt(double fx, double fy)
    {
        var (x, y) = PixelAt(fx, fy);
        var i = (y * Width + x) * 4;
        return $"#{_bgra[i + 2]:X2}{_bgra[i + 1]:X2}{_bgra[i]:X2}";
    }
}
