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
    public void Share_link_carries_id_image_path_and_photographer()
    {
        var link = ShareLink.Build(Entry());
        Assert.NotNull(link);
        var uri = new Uri(link!);
        Assert.Equal("oriel.akbarjon.uz", uri.Host);
        Assert.Equal("/share", uri.AbsolutePath);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("AbC123xyz", query["id"]);
        Assert.Equal("photo-1506905925346-21bda4d32df4", query["i"]);
        Assert.Equal("Ann Lee & Co", query["by"]);
        Assert.Equal("ann", query["u"]);
    }

    [Fact]
    public void Photos_not_on_unsplash_cannot_be_shared() =>
        Assert.Null(ShareLink.Build(Entry("https://example.com/a.jpg")));

    [Fact]
    public void Share_link_carries_the_senders_first_name()
    {
        var link = ShareLink.Build(Entry(), "Akbar Jorayev");
        Assert.Contains("&from=Akbar", link);
        Assert.DoesNotContain("Jorayev", link);
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
