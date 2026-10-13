using Oriel.Core.Auth;
using Oriel.Core.Models;

namespace Oriel.Core.Tests;

public sealed class ShareTests
{
    static HistoryEntry Entry(string thumb = "https://images.unsplash.com/photo-1506905925346-21bda4d32df4?ixid=abc&w=400") => new()
    {
        Provider = "unsplash", PhotoId = "AbC123xyz", FilePath = "x.jpg", ThumbnailUrl = thumb,
        PhotoPageUrl = "https://unsplash.com/photos/AbC123xyz", PhotographerName = "Ann Lee & Co",
        PhotographerUrl = "https://unsplash.com/@ann", AppliedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void Links_are_short_and_carry_only_an_id()
    {
        Assert.Equal("https://oriel.akbarjon.uz/share/AB3K9QXZ", ShareLink.ForShare("AB3K9QXZ"));
        Assert.Equal("https://oriel.akbarjon.uz/follow/K7MQ2XPA", ShareLink.ForFollow("K7MQ2XPA"));
    }

    [Fact]
    public void The_page_gets_the_id_image_path_and_photographer()
    {
        var photo = ShareLink.Photo(Entry());
        Assert.Equal(new SharedPhoto("AbC123xyz", "photo-1506905925346-21bda4d32df4", "Ann Lee & Co", "ann"), photo);
    }

    [Fact]
    public void Photos_not_on_unsplash_cannot_be_shared() =>
        Assert.Null(ShareLink.Photo(Entry("https://example.com/a.jpg")));

    [Theory]
    [InlineData("https://lh3.googleusercontent.com/a/ACg8oc=s96-c", "https://lh3.googleusercontent.com/a/ACg8oc=s96-c")]
    [InlineData("https://evil.example/a.png", "")]
    [InlineData("http://lh3.googleusercontent.com/a/x", "")]
    [InlineData(null, "")]
    public void Only_a_Google_profile_picture_is_kept(string? url, string expected) => Assert.Equal(expected, ShareLink.Picture(url));

    [Fact]
    public void A_wallpaper_link_carries_the_senders_first_name()
    {
        Assert.True(DeepLink.TryGetSharedPhoto(["oriel://wallpaper?id=AbC123xyz&from=Akbar"], out _, out var from));
        Assert.Equal("Akbar", from);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    [InlineData("<b>Ann</b> Lee", "bAnn/b")]
    public void Names_are_cleaned_for_links(string? name, string expected) => Assert.Equal(expected, ShareLink.CleanName(name));

    [Theory]
    [InlineData("oriel://wallpaper?id=AbC123xyz", true, "AbC123xyz")]
    [InlineData("\"oriel://wallpaper?id=AbC123xyz\"", true, "AbC123xyz")]
    [InlineData("oriel://WALLPAPER?id=a-b_c123", true, "a-b_c123")]
    [InlineData("oriel://wallpaper?id=../etc", false, "")]
    [InlineData("oriel://wallpaper?id=ab", false, "")]
    [InlineData("oriel://wallpaper", false, "")]
    [InlineData("oriel://auth?token=x", false, "")]
    public void Wallpaper_link_is_parsed_strictly(string arg, bool ok, string id)
    {
        Assert.Equal(ok, DeepLink.TryGetSharedPhoto([arg], out var found));
        Assert.Equal(id, found);
    }
}
