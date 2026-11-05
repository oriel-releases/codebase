using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Oriel.UI.Services;

/// <summary>A photo's pixels, decoded once at a modest size, to read the color at any spot (Home's eyedropper).</summary>
public sealed class PhotoPixels
{
    readonly byte[] _bgra;
    public int Width { get; }
    public int Height { get; }

    PhotoPixels(byte[] bgra, int width, int height) => (_bgra, Width, Height) = (bgra, width, height);

    /// <summary>Null when the file is gone or can't be decoded.</summary>
    public static Task<PhotoPixels?> LoadAsync(string path, int maxWidth = 1000) => Task.Run(() =>
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            using var bitmap = Bitmap.DecodeToWidth(stream, maxWidth, BitmapInterpolationMode.HighQuality);
            var size = bitmap.PixelSize;
            var stride = size.Width * 4;
            var pixels = new byte[stride * size.Height];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), pixels.Length, stride);
            }
            finally
            {
                handle.Free();
            }
            // Read as B,G,R,A; Skia may hand back R,G,B,A (like AccentSampler).
            if (bitmap.Format == Avalonia.Platform.PixelFormats.Rgba8888)
                for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            return new PhotoPixels(pixels, size.Width, size.Height);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return (PhotoPixels?)null;
        }
    });

    /// <summary>The pixel at a spot given as 0..1 of the width and height.</summary>
    public (int X, int Y) PixelAt(double fx, double fy) =>
        (Math.Clamp((int)(fx * Width), 0, Width - 1), Math.Clamp((int)(fy * Height), 0, Height - 1));

    /// <summary>One pixel's color; outside the photo, transparent.</summary>
    public Avalonia.Media.Color Color(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return default;
        var i = (y * Width + x) * 4;
        return Avalonia.Media.Color.FromRgb(_bgra[i + 2], _bgra[i + 1], _bgra[i]);
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
