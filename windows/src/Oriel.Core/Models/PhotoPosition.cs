using System.Globalization;

namespace Oriel.Core.Models;

public enum PositionMode { Smart, Center, Focus }

/// <summary>
/// Where a photo is cropped to a screen: Unsplash's smart crop, the middle, or around a point picked in the position
/// editor (<see cref="X"/>, <see cref="Y"/>: 0..1 across and down the photo). A point fits every screen's shape, so it is
/// the same on every device of the account: it travels in <c>sync/current</c> (crop, focusX, focusY) with the photo.
/// </summary>
public sealed record PhotoPosition(PositionMode Mode, double X = 0.5, double Y = 0.5)
{
    public static readonly PhotoPosition Smart = new(PositionMode.Smart);
    public static readonly PhotoPosition Center = new(PositionMode.Center);

    public static PhotoPosition Focus(double x, double y) => new(PositionMode.Focus, Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));

    /// <summary>The word in <c>sync/current.crop</c>.</summary>
    public string CropWord => Mode switch
    {
        PositionMode.Center => "center",
        PositionMode.Focus => "focus",
        _ => "smart",
    };

    /// <summary>From <c>sync/current</c>'s fields; null when there's no (usable) position there.</summary>
    public static PhotoPosition? FromSync(string? crop, double? x, double? y) => crop switch
    {
        "smart" => Smart,
        "center" => Center,
        "focus" when x is { } fx && y is { } fy && double.IsFinite(fx) && double.IsFinite(fy) => Focus(fx, fy),
        _ => null,
    };

    /// <summary>
    /// Unsplash's CDN parameters that crop around it (with <c>fit=crop</c>): its smart crop, nothing (the middle), or the
    /// focal point.
    /// </summary>
    public string CropQuery => Mode switch
    {
        PositionMode.Center => "",
        PositionMode.Focus => $"crop=focalpoint&fp-x={Number(X)}&fp-y={Number(Y)}",
        _ => "crop=entropy",
    };

    static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
