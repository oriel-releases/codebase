using Oriel.Core.Wallpaper;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Oriel.UI.Services;

/// <summary>Decodes the photo at thumbnail size (plenty for finding its main colors, and fast) and picks its accents.</summary>
public sealed class AccentSampler : IAccentSampler
{
    (string Path, IReadOnlyList<string> Hexes)? _last;

    public async Task<string?> SampleAsync(string imagePath) => (await SampleTopAsync(imagePath)).FirstOrDefault();

    public async Task<IReadOnlyList<string>> SampleTopAsync(string imagePath)
    {
        if (_last is { } last && last.Path == imagePath) return last.Hexes;
        var hexes = await DecodeAsync(imagePath);
        _last = (imagePath, hexes);
        return hexes;
    }

    static async Task<IReadOnlyList<string>> DecodeAsync(string imagePath)
    {
        if (!File.Exists(imagePath)) return [];
        var file = await StorageFile.GetFileFromPathAsync(imagePath);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform { ScaledWidth = 96, ScaledHeight = 54 };
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        return [.. AccentPicker.PickTop(pixels.DetachPixelData()).Select(AccentPicker.ToHex)];
    }
}
