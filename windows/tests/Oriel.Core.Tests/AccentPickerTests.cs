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

    [Fact]
    public void Top_colors_come_from_different_hues_most_used_first()
    {
        var blue = Solid(20, 90, 220, 600);
        var orange = Solid(230, 120, 20, 300);
        var green = Solid(40, 180, 60, 100);
        var bluish = Solid(30, 110, 230, 200); // next to blue: not offered on its own
        var top = AccentPicker.PickTop(blue.Concat(orange).Concat(green).Concat(bluish).ToArray());
        Assert.Equal(3, top.Count);
        Assert.True(top[0].B > top[0].R, "blue first");
        Assert.True(top[1].R > top[1].B, "orange second");
        Assert.True(top[2].G > top[2].R && top[2].G > top[2].B, "green third");
        Assert.Equal(top[0], AccentPicker.Pick(blue.Concat(orange).Concat(green).Concat(bluish).ToArray()));
    }

    [Fact]
    public void A_photo_of_one_color_still_offers_three()
    {
        var top = AccentPicker.PickTop(Solid(20, 90, 220, 1000).ToArray());
        Assert.Equal(3, top.Count);
        Assert.Equal(3, top.Distinct().Count());
        Assert.Equal(3, AccentPicker.PickTop(Solid(100, 100, 100, 500)).Distinct().Count());
    }

    [Fact]
    public void Ai_pictures_keep_the_screen_shape_at_most_2048_wide()
    {
        Assert.Equal(new Models.PixelSize(2048, 1152), Providers.PollinationsClient.SizeFor(new Models.PixelSize(3840, 2160)));
        Assert.Equal(new Models.PixelSize(1920, 1088), Providers.PollinationsClient.SizeFor(new Models.PixelSize(1920, 1080)));
        Assert.Equal(new Models.PixelSize(2048, 864), Providers.PollinationsClient.SizeFor(new Models.PixelSize(3440, 1440)));
    }
}
