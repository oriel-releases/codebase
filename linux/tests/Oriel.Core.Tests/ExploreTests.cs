using Oriel.Core.Services;

namespace Oriel.Core.Tests;

public sealed class ExploreTests
{
    const string Feed = """
        {"version":1,"hour":"2026-10-21T14:00:00Z","landscape":[
          {"id":"a1","width":6000,"height":4000,"likes":12,"color":"#2E4A3F","alt_description":"foggy forest","topic":"forest",
           "urls":{"raw":"https://images.unsplash.com/photo-1?ixid=x"},
           "links":{"html":"https://unsplash.com/photos/a1","download_location":"https://api.unsplash.com/photos/a1/download?ixid=x"},
           "user":{"name":"Ann","username":"ann","links":{"html":"https://unsplash.com/@ann"}}},
          {"id":"broken"},
          {"id":"p1","width":6000,"height":4000,"urls":{"raw":"https://plus.unsplash.com/premium_photo-1"},
           "links":{"html":"h"},"user":{"name":"B","links":{"html":"u"}}}
        ],"portrait":[]}
        """;

    [Fact]
    public void Reads_the_landscape_photos_and_skips_broken_or_paid_ones()
    {
        var feed = ExploreService.Parse(Feed);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 14, 0, 0, TimeSpan.Zero), feed.Hour);
        var item = Assert.Single(feed.Photos);
        Assert.Equal("forest", item.Topic);
        Assert.Equal("a1", item.Photo.Id);
        Assert.Equal("https://images.unsplash.com/photo-1?ixid=x&w=720&q=75&fm=jpg&fit=max", item.Photo.ThumbnailUrl);
        Assert.Equal("https://api.unsplash.com/photos/a1/download?ixid=x", item.Photo.DownloadTrackingUrl);
        Assert.Equal("https://unsplash.com/@ann?utm_source=oriel&utm_medium=referral", item.Photo.PhotographerUrl);
        Assert.Empty(ExploreService.Parse("not json").Photos);
    }

    [Fact]
    public void A_feed_is_current_until_the_next_hour_starts()
    {
        var feed = ExploreService.Parse(Feed);
        Assert.True(ExploreService.IsCurrent(feed, new DateTimeOffset(2026, 10, 21, 14, 59, 0, TimeSpan.Zero)));
        Assert.True(ExploreService.IsCurrent(feed, new DateTimeOffset(2026, 10, 21, 19, 30, 0, TimeSpan.FromHours(5))));
        Assert.False(ExploreService.IsCurrent(feed, new DateTimeOffset(2026, 10, 21, 15, 0, 0, TimeSpan.Zero)));
        Assert.False(ExploreService.IsCurrent(null, new DateTimeOffset(2026, 10, 21, 14, 0, 0, TimeSpan.Zero)));
    }
}
