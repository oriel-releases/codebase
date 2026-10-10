using Oriel.Core.Wallpaper;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Oriel.UI.Services;

/// <summary>Decodes the photo at thumbnail size (plenty for finding its main color, and fast) and picks its accent.</summary>
public sealed class AccentSampler : IAccentSampler
{
    public async Task<string?> SampleAsync(string imagePath)
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
