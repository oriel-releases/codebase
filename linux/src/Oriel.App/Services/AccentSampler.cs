using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Oriel.Core.Wallpaper;

namespace Oriel.UI.Services;

/// <summary>Decodes the photo at thumbnail size (plenty for finding its main color, and fast) and picks its accent.</summary>
public sealed class AccentSampler : IAccentSampler
{
    const int Width = 96;

    public Task<string?> SampleAsync(string imagePath) => Task.Run<string?>(() =>
    {
        if (!File.Exists(imagePath)) return null;
        using var stream = File.OpenRead(imagePath);
        using var bitmap = Bitmap.DecodeToWidth(stream, Width, BitmapInterpolationMode.MediumQuality);

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

        // The picker reads B,G,R,A; Skia may hand back R,G,B,A.
        if (bitmap.Format == Avalonia.Platform.PixelFormats.Rgba8888)
            for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        return AccentPicker.ToHex(AccentPicker.Pick(pixels));
    });
}
