using Oriel.Core.Models;
using Oriel.Core.Wallpaper;

namespace Oriel.Core.Tests;

public sealed class CropWindowTests
{
    [Fact]
    public void A_wide_photo_moves_the_window_across()
    {
        var window = CropWindow.Fit(3000, 1000, 16 / 9.0);
        Assert.True(window.Across);
        Assert.Equal(1000, window.Height);
        Assert.Equal(1000 * 16 / 9.0, window.Width, 6);
    }

    [Fact]
    public void A_tall_photo_moves_it_up_and_down()
    {
        var window = CropWindow.Fit(1000, 3000, 16 / 9.0);
        Assert.False(window.Across);
        Assert.Equal(1000, window.Width);
    }

    [Fact]
    public void The_window_stays_on_the_photo()
    {
        var window = CropWindow.Fit(3000, 1000, 1);
        Assert.Equal((0d, 0d), window.Place(3000, 1000, 0));
        Assert.Equal((2000d, 0d), window.Place(3000, 1000, 1));
        Assert.Equal((1000d, 0d), window.Place(3000, 1000, 0.5));
    }

    [Fact]
    public void Dragging_snaps_to_the_middle_and_stops_at_the_edges()
    {
        var window = CropWindow.Fit(3000, 1000, 1);
        Assert.Equal(0.5, window.CenterAt(3000, 1000, 1540));
        Assert.Equal(1 / 6.0, window.CenterAt(3000, 1000, 0), 6);
        Assert.Equal(5 / 6.0, window.CenterAt(3000, 1000, 3000), 6);
    }

    [Fact]
    public void Its_focus_point_keeps_the_other_side_in_the_middle()
    {
        Assert.Equal(PhotoPosition.Focus(0.3, 0.5), CropWindow.Fit(3000, 1000, 1).FocusAt(0.3));
        Assert.Equal(PhotoPosition.Focus(0.5, 0.3), CropWindow.Fit(1000, 3000, 1).FocusAt(0.3));
    }

    [Fact]
    public void Cuts_the_screen_shape_out_of_a_wide_photo_where_it_was_placed()
    {
        // A 3000 x 1000 photo for a 1920 x 1080 screen: the cut is 1000 tall, 1778 wide, then made the screen's size.
        var cut = LocalCrop.Plan(3000, 1000, new PixelSize(1920, 1080), PhotoPosition.Focus(1, 0.5), 0.5);
        Assert.Equal(1000, cut.Height);
        Assert.Equal(1778, cut.Width);
        Assert.Equal(3000 - 1778, cut.X);
        Assert.Equal((1778, 1000), (cut.OutWidth, cut.OutHeight));
    }

    [Fact]
    public void A_bigger_cut_is_made_the_screens_size()
    {
        var cut = LocalCrop.Plan(6000, 3000, new PixelSize(1920, 1080), PhotoPosition.Center, 0.5);
        Assert.Equal((1920, 1080), (cut.OutWidth, cut.OutHeight));
        Assert.InRange(cut.X, (6000 - cut.Width) / 2 - 1, (6000 - cut.Width) / 2 + 1);
    }

    [Fact]
    public void Asks_for_a_whole_photo_big_enough_for_the_screen()
    {
        Assert.Equal(3240, LocalCrop.WholeWidthFor(3, new PixelSize(1920, 1080)));
        Assert.Equal(1920, LocalCrop.WholeWidthFor(0.75, new PixelSize(1920, 1080)));
    }

    [Fact]
    public void Finds_where_the_crop_came_from()
    {
        // A whole photo 30 wide with a bright band at 18..23; the crop (6 wide) is that band.
        const int w = 30, h = 6, cw = 6;
        var whole = new byte[w * h];
        for (var y = 0; y < h; y++) for (var x = 18; x < 24; x++) whole[y * w + x] = 255;
        var crop = new byte[cw * h];
        Array.Fill(crop, (byte)255);
        Assert.Equal(21 / 30.0, SmartCrop.Locate(whole, w, h, crop, cw, h, across: true), 6);
    }
}
