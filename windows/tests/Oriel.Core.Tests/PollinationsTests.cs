using Oriel.Core.Providers;

namespace Oriel.Core.Tests;

public sealed class PollinationsTests
{
    const string Link = "https://media.pollinations.ai/e774ccc41b39159943fc0e2073130ce013fa363cbc05a4ed3058bcbdc677218d";

    [Fact]
    public void A_media_link_becomes_an_ai_photo_any_device_can_download()
    {
        var photo = PollinationsClient.FromLink(Link + "?w=640&q=80", "cat on moon");
        Assert.NotNull(photo);
        Assert.Equal("ai-e774ccc41b39159943fc0e2073130ce013fa363cbc05a4ed3058bcbdc677218d", photo.Id);
        Assert.Equal(PollinationsClient.Provider, photo.Provider);
        Assert.Equal(Link, photo.RawUrl); // sizing added by other apps is dropped
        Assert.True(PollinationsClient.IsAiPhoto(photo.Id));
    }

    [Theory]
    [InlineData("https://images.unsplash.com/photo-123")]
    [InlineData("http://media.pollinations.ai/e774ccc41b39159943fc0e2073130ce0")]
    [InlineData("https://media.pollinations.ai/not-an-id")]
    [InlineData("https://evil.example/e774ccc41b39159943fc0e2073130ce0")]
    public void Other_links_are_not_ai_photos(string link) => Assert.Null(PollinationsClient.FromLink(link, null));
}
