using Oriel.Core.Wallpaper;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Oriel.UI.Services;

/// <summary>Decodes the photo at thumbnail size (plenty for finding its main color, and fast) and picks its accent.</summary>
public sealed class AccentSampler : IAccentSampler
{
    (string Path, string? Hex)? _last;

    public async Task<string?> SampleAsync(string imagePath)
    {
        if (_last is { } last && last.Path == imagePath) return last.Hex;
        var hex = await DecodeAsync(imagePath);
        _last = (imagePath, hex);
        return hex;
    }

    static async Task<string?> DecodeAsync(string imagePath)
    {
        if (!File.Exists(imagePath)) return null;
        var file = await StorageFile.GetFileFromPathAsync(imagePath);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform { ScaledWidth = 96, ScaledHeight = 54 };
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        return AccentPicker.ToHex(AccentPicker.Pick(pixels.DetachPixelData()));
    }
}
