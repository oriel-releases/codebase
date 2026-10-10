using Oriel.Core.Wallpaper;

namespace Oriel.Core.Tests;

public sealed class AccentPickerTests
{
    static byte[] Solid(byte r, byte g, byte b, int count)
    {
        var px = new byte[count * 4];
        for (var i = 0; i < count; i++) { px[i * 4] = b; px[i * 4 + 1] = g; px[i * 4 + 2] = r; px[i * 4 + 3] = 255; }
        return px;
    }

    [Fact]
    public void Picks_the_vivid_color_over_a_gray_background()
    {
        var gray = Solid(120, 120, 120, 900);
        var blue = Solid(20, 90, 220, 100);
        var (r, g, b) = AccentPicker.Pick(gray.Concat(blue).ToArray());
        Assert.True(b > r && b > g, $"expected blue, got {r},{g},{b}");
    }

    [Fact]
    public void A_gray_photo_still_gives_a_color()
    {
        var (r, g, b) = AccentPicker.Pick(Solid(100, 100, 100, 500));
        Assert.Equal(r, g);
        Assert.Equal(g, b);
    }

    [Fact]
    public void Palette_has_eight_shades_with_the_accent_in_the_middle()
    {
        var palette = AccentPicker.Palette((0, 120, 212));
        Assert.Equal(8, palette.Length);
        Assert.Equal(((byte)0, (byte)120, (byte)212), palette[3]);
    }

    [Fact]
    public void Hex_is_uppercase_rrggbb() => Assert.Equal("#0078D4", AccentPicker.ToHex((0, 120, 212)));
}
