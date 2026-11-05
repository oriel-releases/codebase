using Oriel.Core.Models;

namespace Oriel.Core.Wallpaper;

/// <summary>
/// The position editor's window: this screen's shape laid over the whole photo, as big as fits. It moves along one
/// side only: across a photo wider than the screen, up and down a taller one (the other side always fills).
/// Any unit, as long as the photo and the window use the same one.
/// </summary>
public sealed record CropWindow(double Width, double Height, bool Across)
{
    /// <summary>Within this share of the middle, the window snaps to it.</summary>
    const double Snap = 0.025;

    public static CropWindow Fit(double photoWidth, double photoHeight, double screenAspect)
    {
        var across = photoWidth / photoHeight > screenAspect;
        return across ? new(photoHeight * screenAspect, photoHeight, true) : new(photoWidth, photoWidth / screenAspect, false);
    }

    /// <summary>The window's top left with its middle at <paramref name="center"/> (0..1 along the side it moves on), kept on the photo.</summary>
    public (double X, double Y) Place(double photoWidth, double photoHeight, double center)
    {
        var length = Across ? photoWidth : photoHeight;
        var window = Across ? Width : Height;
        var offset = Math.Clamp(length * center - window / 2, 0, Math.Max(0, length - window));
        return Across ? (offset, 0) : (0, offset);
    }

    /// <summary>
    /// Where the window's middle goes for a pointer at <paramref name="along"/> (from the photo's left, or top): as far
    /// as the photo's edges allow, snapping to the middle when close to it.
    /// </summary>
    public double CenterAt(double photoWidth, double photoHeight, double along)
    {
        var length = Across ? photoWidth : photoHeight;
        var half = (Across ? Width : Height) / 2 / length;
        var center = Math.Clamp(along / length, half, Math.Max(half, 1 - half));
        return Math.Abs(center - 0.5) < Snap ? 0.5 : center;
    }

    /// <summary>The focus point for a window centered at <paramref name="center"/>: the other side stays in the middle.</summary>
    public PhotoPosition FocusAt(double center) => Across ? PhotoPosition.Focus(center, 0.5) : PhotoPosition.Focus(0.5, center);

    /// <summary>Where the window's middle is (0..1 along its side) for a position; <paramref name="smartAt"/> for Smart.</summary>
    public double CenterOf(PhotoPosition position, double smartAt) => position.Mode switch
    {
        PositionMode.Focus => Across ? position.X : position.Y,
        PositionMode.Center => 0.5,
        _ => smartAt,
    };
}

/// <summary>
/// Where the screen's shape is cut out of a whole photo, in its pixels, and the size it ends up (the screen's, or
/// smaller when the photo is: never enlarged). The position editor's Set cuts it here instead of asking Unsplash for
/// a new crop (which takes it seconds to make).
/// </summary>
public sealed record LocalCrop(int X, int Y, int Width, int Height, int OutWidth, int OutHeight)
{
    public static LocalCrop Plan(int wholeWidth, int wholeHeight, PixelSize screen, PhotoPosition position, double smartAt)
    {
        var window = CropWindow.Fit(wholeWidth, wholeHeight, (double)screen.Width / screen.Height);
        var (x, y) = window.Place(wholeWidth, wholeHeight, window.CenterOf(position, smartAt));
        var width = Math.Min(wholeWidth, (int)Math.Round(window.Width));
        var height = Math.Min(wholeHeight, (int)Math.Round(window.Height));
        var left = Math.Clamp((int)Math.Round(x), 0, wholeWidth - width);
        var top = Math.Clamp((int)Math.Round(y), 0, wholeHeight - height);
        return width > screen.Width
            ? new(left, top, width, height, screen.Width, screen.Height)
            : new(left, top, width, height, width, height);
    }

    /// <summary>The whole photo's width to download so its cut covers <paramref name="screen"/> (as wide as the photo allows).</summary>
    public static int WholeWidthFor(double photoAspect, PixelSize screen) =>
        photoAspect > (double)screen.Width / screen.Height
            ? (int)Math.Ceiling(screen.Height * photoAspect)
            : screen.Width;
}

/// <summary>
/// Where Unsplash's smart crop puts this screen's shape on a photo. Unsplash doesn't say, so it's found by looking:
/// the smart-cropped picture and the whole photo, both small with the same fixed side, and the place along the photo
/// where the crop matches best. The same way as the phone.
/// </summary>
public static class SmartCrop
{
    /// <summary>The side both small pictures share (pixels).</summary>
    public const int Side = 160;

    /// <summary>The two small pictures: the whole photo, and Unsplash's smart crop of it in this screen's shape.</summary>
    public static (string Whole, string Crop, bool Across) SampleUrls(string rawUrl, double photoAspect, double screenAspect)
    {
        var sep = rawUrl.Contains('?') ? '&' : '?';
        var across = photoAspect > screenAspect;
        var (ww, wh) = across ? ((int)Math.Round(Side * photoAspect), Side) : (Side, (int)Math.Round(Side / photoAspect));
        var (cw, ch) = across ? ((int)Math.Round(Side * screenAspect), Side) : (Side, (int)Math.Round(Side / screenAspect));
        var smart = PhotoPosition.Smart.CropQuery;
        return ($"{rawUrl}{sep}w={ww}&h={wh}&fit=crop&q=80&fm=jpg",
            $"{rawUrl}{sep}w={cw}&h={ch}&fit=crop&{smart}&q=80&fm=jpg", across);
    }

    /// <summary>
    /// The smart crop's middle, 0..1 along the side the window moves on, from the two pictures' brightness (one byte a
    /// pixel, rows top to bottom).
    /// </summary>
    public static double Locate(byte[] whole, int wholeWidth, int wholeHeight, byte[] crop, int cropWidth, int cropHeight, bool across)
    {
        var length = across ? wholeWidth : wholeHeight;
        var window = across ? cropWidth : cropHeight;
        var side = Math.Min(across ? wholeHeight : wholeWidth, across ? cropHeight : cropWidth);
        if (window >= length) return 0.5;
        int At(int along, int other, int width) => across ? other * width + along : along * width + other;
        var best = 0;
        var bestCost = long.MaxValue;
        for (var shift = 0; shift <= length - window; shift++)
        {
            long cost = 0;
            // Every 3rd pixel both ways: plenty to tell the places apart, and quick.
            for (var a = 0; a < window; a += 3)
                for (var b = 0; b < side; b += 3)
                    cost += Math.Abs(crop[At(a, b, cropWidth)] - whole[At(a + shift, b, wholeWidth)]);
            if (cost < bestCost) { bestCost = cost; best = shift; }
        }
        return (best + window / 2.0) / length;
    }
}
