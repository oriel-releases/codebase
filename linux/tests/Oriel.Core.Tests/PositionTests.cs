using Oriel.Core.Models;
using Oriel.Core.Providers;
using Oriel.Core.Storage;

namespace Oriel.Core.Tests;

public sealed class PositionTests : IDisposable
{
    readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("smart", null, null, PositionMode.Smart)]
    [InlineData("center", null, null, PositionMode.Center)]
    [InlineData("focus", 0.25, 0.75, PositionMode.Focus)]
    public void Reads_the_position_sync_current_carries(string crop, double? x, double? y, PositionMode mode)
    {
        var position = PhotoPosition.FromSync(crop, x, y);
        Assert.NotNull(position);
        Assert.Equal(mode, position.Mode);
        Assert.Equal(crop, position.CropWord);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("focus", null, 0.5)]
    [InlineData("sideways", 0.5, 0.5)]
    public void Ignores_a_missing_or_broken_position(string? crop, double? x, double? y) =>
        Assert.Null(PhotoPosition.FromSync(crop, x, y));

    [Fact]
    public void A_focus_point_stays_on_the_photo() =>
        Assert.Equal(PhotoPosition.Focus(1, 0), PhotoPosition.Focus(1.4, -0.2));

    [Fact]
    public void Smart_downloads_the_same_crop_as_before() =>
        Assert.Equal("https://images.unsplash.com/photo-1?ixid=a&w=1920&h=1080&fit=crop&crop=entropy&q=90&fm=jpg",
            UnsplashProvider.BuildDownloadUrl("https://images.unsplash.com/photo-1?ixid=a", new PixelSize(1920, 1080)));

    [Fact]
    public void Center_and_a_focus_point_crop_there()
    {
        const string raw = "https://images.unsplash.com/photo-1?ixid=a";
        Assert.Equal($"{raw}&w=1920&h=1080&fit=crop&q=90&fm=jpg",
            UnsplashProvider.BuildDownloadUrl(raw, new PixelSize(1920, 1080), PhotoPosition.Center));
        Assert.Equal($"{raw}&w=1920&h=1080&fit=crop&crop=focalpoint&fp-x=0.25&fp-y=0.6&q=90&fm=jpg",
            UnsplashProvider.BuildDownloadUrl(raw, new PixelSize(1920, 1080), PhotoPosition.Focus(0.25, 0.6)));
    }

    [Theory]
    [InlineData("https://images.unsplash.com/photo-1?ixid=a&w=720&q=75&fm=jpg&fit=max", "https://images.unsplash.com/photo-1?ixid=a")]
    [InlineData("https://images.unsplash.com/photo-1?w=400&h=800&crop=focalpoint&fp-x=0.1", "https://images.unsplash.com/photo-1")]
    [InlineData("https://images.unsplash.com/photo-1", "https://images.unsplash.com/photo-1")]
    public void Takes_the_sizing_off_a_link(string link, string raw) => Assert.Equal(raw, UnsplashProvider.RawOf(link));

    [Fact]
    public void Keeps_the_newest_pick_for_each_photo()
    {
        var store = new JsonPositionStore(_dir.Paths);
        Assert.Null(store.Get("a"));
        store.Set("a", PhotoPosition.Center);
        store.Set("a", PhotoPosition.Focus(0.3, 0.7));
        store.Set("b", PhotoPosition.Smart);

        var again = new JsonPositionStore(_dir.Paths);
        Assert.Equal(PhotoPosition.Focus(0.3, 0.7), again.Get("a"));
        Assert.Equal(PhotoPosition.Smart, again.Get("b"));
    }
}
