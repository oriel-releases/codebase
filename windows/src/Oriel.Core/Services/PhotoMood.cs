using System.Globalization;
using Oriel.Core.Models;

namespace Oriel.Core.Services;

/// <summary>Which kind of photo suits the moment.</summary>
public enum Mood { Any, Dark, Bright }

/// <summary>
/// Darker, calmer photos in the evening and at night (and whenever the system is in dark mode), brighter ones in the
/// morning. Only an order of preference: when no photo fits, any good photo is still used.
/// </summary>
public static class PhotoMood
{
    public static Mood For(DateTime local, bool systemDark)
    {
        var h = local.Hour;
        if (h >= 19 || h < 6 || systemDark) return Mood.Dark;
        return h < 11 ? Mood.Bright : Mood.Any;
    }

    /// <summary>Relative luminance (0 black to 1 white) of a "#RRGGBB" color, or null.</summary>
    public static double? Luminance(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#' ||
            !int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return null;
        static double Lin(int c) { var v = c / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(rgb >> 16 & 0xFF) + 0.7152 * Lin(rgb >> 8 & 0xFF) + 0.0722 * Lin(rgb & 0xFF);
    }

    /// <summary>True when the photo suits the mood. A photo without a known color always does.</summary>
    public static bool Fits(PhotoCandidate photo, Mood mood) => mood switch
    {
        Mood.Dark => Luminance(photo.Color) is not { } l || l < 0.18,
        Mood.Bright => Luminance(photo.Color) is not { } l || l > 0.3,
        _ => true,
    };

    /// <summary>The photos that suit the mood first, in their original order, then the rest.</summary>
    public static IEnumerable<PhotoCandidate> Order(IReadOnlyList<PhotoCandidate> photos, Mood mood) =>
        mood == Mood.Any ? photos : photos.Where(p => Fits(p, mood)).Concat(photos.Where(p => !Fits(p, mood)));
}
