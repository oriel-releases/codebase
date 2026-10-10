namespace Oriel.Core.Wallpaper;

/// <summary>Picks an accent color from a small copy of a photo, and builds Windows' shade palette from it.</summary>
public static class AccentPicker
{
    const int HueBuckets = 24;

    /// <summary>The photo's most striking color, adjusted so it works as a UI accent (white text stays readable).</summary>
    /// <param name="bgra">Pixels as B,G,R,A bytes (what the WinRT decoder returns).</param>
    public static (byte R, byte G, byte B) Pick(ReadOnlySpan<byte> bgra)
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

        var best = Array.IndexOf(weight, weight.Max());
        double mr, mg, mb;
        if (weight[best] > 0)
        {
            mr = sumR[best] / weight[best]; mg = sumG[best] / weight[best]; mb = sumB[best] / weight[best];
        }
        else if (n > 0)
        {
            mr = allR / n; mg = allG / n; mb = allB / n;   // a grayscale photo: use its average
        }
        else
        {
            return (0x00, 0x78, 0xD4);
        }

        var (hue, sat, val) = ToHsv(mr, mg, mb);
        if (weight[best] > 0) sat = Math.Max(sat, 0.55);
        val = Math.Clamp(val, 0.55, 0.85);
        return FromHsv(hue, sat, val);
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
