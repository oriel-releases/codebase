using Oriel.Core.Services;

namespace Oriel.Core.Tests;

public sealed class TodayTests
{
    [Fact]
    public void Picks_the_same_photo_as_the_website_for_a_date()
    {
        // 10 October 2026 is day 20,736 since 1 January 1970; the website's /today takes position 20,736 mod length.
        var list = Enumerable.Range(0, 287).ToList();
        Assert.Equal(72, TodayService.Pick(list, new DateTime(2026, 10, 10, 0, 1, 0)));
        Assert.Equal(72, TodayService.Pick(list, new DateTime(2026, 10, 10, 23, 59, 0)));
        Assert.Equal(73, TodayService.Pick(list, new DateTime(2026, 10, 11, 0, 0, 0)));
    }

    [Fact]
    public void Reads_the_published_list_and_skips_broken_entries()
    {
        var list = TodayService.Parse("""{"photos":[{"id":"a1","path":"photo-1","by":"Ann","handle":"ann"},{"id":"","path":"x"}]}""");
        var photo = Assert.Single(list);
        Assert.Equal("https://images.unsplash.com/photo-1?w=480&q=75&fm=jpg&auto=format&fit=crop", photo.ThumbnailUrl(480));
        Assert.Empty(TodayService.Parse("not json"));
    }
}
