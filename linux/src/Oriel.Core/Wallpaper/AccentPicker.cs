namespace Oriel.Core.Wallpaper;

/// <summary>Picks an accent color from a small copy of a photo, and builds Windows' shade palette from it.</summary>
public static class AccentPicker
{
    const int HueBuckets = 24;

    /// <summary>The photo's most striking color, adjusted so it works as a UI accent (white text stays readable).</summary>
    /// <param name="bgra">Pixels as B,G,R,A bytes (what the WinRT decoder returns).</param>
    public static (byte R, byte G, byte B) Pick(ReadOnlySpan<byte> bgra) => PickTop(bgra, 1)[0];

    /// <summary>
    /// The photo's most used striking colors, best first, each from its own part of the color wheel when the photo has
    /// enough of them (so three blues don't fill the choice), and adjusted like <see cref="Pick"/>. Always
    /// <paramref name="count"/>: a photo with fewer colors gets neighboring hues, then lighter and darker shades.
    /// </summary>
    public static IReadOnlyList<(byte R, byte G, byte B)> PickTop(ReadOnlySpan<byte> bgra, int count = 3)
    {
        var weight = new double[HueBuckets];
        var sumR = new double[HueBuckets];
        var sumG = new double[HueBuckets];
        var sumB = new double[HueBuckets];
        double allR = 0, allG = 0, allB = 0;
        var n = bgra.Length / 4;

        for (var i = 0; i < n; i++)
        {
            double b = bgra[i * 4], g = bgra[i * 4 + 1], r = bgra[i * 4 + 2];
            allR += r; allG += g; allB += b;

            var (h, s, v) = ToHsv(r, g, b);
            // Skip near-black, near-white and washed-out pixels: they make dull accents.
            if (v < 0.2 || s < 0.25 || (v > 0.95 && s < 0.4)) continue;

            var bucket = Math.Min(HueBuckets - 1, (int)(h / 360 * HueBuckets));
            var w = s * s * v;   // favor saturated, bright pixels
            weight[bucket] += w;
            sumR[bucket] += r * w; sumG[bucket] += g * w; sumB[bucket] += b * w;
        }

        var top = weight.Max();
        if (top <= 0)
        {
            if (n == 0) return Shades((0x00, 0x78, 0xD4), count);
            var (ah, as_, av) = ToHsv(allR / n, allG / n, allB / n);   // a grayscale photo: its average, in shades
            return Shades(FromHsv(ah, as_, Math.Clamp(av, 0.55, 0.85)), count);
        }

        var picked = new List<int>();
        foreach (var bucket in Enumerable.Range(0, HueBuckets).OrderByDescending(i => weight[i]))
        {
            if (picked.Count == count) break;
            // A color the photo barely has isn't worth offering.
            if (weight[bucket] < top * 0.04) break;
            // Its own hue: not next to one already picked (the wheel wraps around).
            if (picked.Any(p => Math.Min(Math.Abs(p - bucket), HueBuckets - Math.Abs(p - bucket)) <= 1)) continue;
            picked.Add(bucket);
        }
        // Few colors: the next most used hues, even next to one already picked.
        foreach (var bucket in Enumerable.Range(0, HueBuckets).OrderByDescending(i => weight[i]))
        {
            if (picked.Count == count || weight[bucket] <= 0) break;
            if (!picked.Contains(bucket)) picked.Add(bucket);
        }

        var colors = picked.Select(i =>
        {
            var (hue, sat, val) = ToHsv(sumR[i] / weight[i], sumG[i] / weight[i], sumB[i] / weight[i]);
            return FromHsv(hue, Math.Max(sat, 0.55), Math.Clamp(val, 0.55, 0.85));
        }).Distinct().ToList();
        // Still short (one hue only): its lighter and darker shades.
        foreach (var shade in Shades(colors[0], count).Skip(1))
            if (colors.Count < count && !colors.Contains(shade)) colors.Add(shade);
        return colors;
    }

    /// <summary>The color, then a darker and a lighter shade of it, and so on: <paramref name="count"/> in all.</summary>
    static List<(byte R, byte G, byte B)> Shades((byte R, byte G, byte B) color, int count)
    {
        var (h, s, v) = ToHsv(color.R, color.G, color.B);
        var shades = new List<(byte R, byte G, byte B)> { color };
        for (var i = 1; shades.Count < count && i <= count; i++)
        {
            if (shades.Count < count) shades.Add(FromHsv(h, s, Math.Max(0.3, v - 0.2 * i)));
            if (shades.Count < count) shades.Add(FromHsv(h, s * 0.7, Math.Min(1, v + 0.15 * i)));
        }
        return shades;
    }

    public static string ToHex((byte R, byte G, byte B) c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    static (double H, double S, double V) ToHsv(double r, double g, double b)
    {
        r /= 255; g /= 255; b /= 255;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double h;
        if (d == 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / d % 6 + 6) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        return (h, max == 0 ? 0 : d / max, max);
    }

    static (byte R, byte G, byte B) FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        byte B(double d) => (byte)Math.Round(Math.Clamp((d + m) * 255, 0, 255));
        return (B(r), B(g), B(b));
    }
}
