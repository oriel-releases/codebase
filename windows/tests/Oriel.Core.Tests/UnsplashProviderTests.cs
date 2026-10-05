using System.Net;
using Oriel.Core.Models;
using Oriel.Core.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Oriel.Core.Tests;

public class UnsplashProviderTests
{
    const string OnePhoto = """
        {"total": 1, "total_pages": 1, "results": [{
          "id": "abc123", "width": 6000, "height": 4000, "likes": 321, "color": "#0c2626",
          "description": null, "alt_description": "a mountain at dawn",
          "urls": { "raw": "https://images.unsplash.com/photo-1?ixid=foo", "full": "x", "regular": "x", "small": "https://s", "thumb": "https://t" },
          "links": { "html": "https://unsplash.com/photos/abc123", "download_location": "https://api.unsplash.com/photos/abc123/download?ixid=foo" },
          "user": { "name": "Jane Doe", "username": "jane", "links": { "html": "https://unsplash.com/@jane" } }
        }]}
        """;

    static (UnsplashProvider, StubHandler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond, string? key = "secret")
    {
        var handler = new StubHandler(respond);
        return (new UnsplashProvider(new HttpClient(handler), new FakeKeyStore(key), NullLogger<UnsplashProvider>.Instance), handler);
    }

    [Fact]
    public async Task Parses_random_photos_and_sends_auth()
    {
        var (p, h) = Create(_ => StubHandler.Json(OnePhoto));

        var list = await p.GetRandomAsync(new ImageQuery("night sky", new PixelSize(2560, 1440)), default);

        var photo = Assert.Single(list);
        Assert.Equal("abc123", photo.Id);
        Assert.Equal(6000, photo.Width);
        Assert.Equal(321, photo.Likes);
        Assert.Equal("Jane Doe", photo.PhotographerName);
        Assert.Equal("a mountain at dawn", photo.Description);
        Assert.Contains("utm_source=oriel", photo.PhotographerUrl);
        Assert.StartsWith("https://unsplash.com/@jane?", photo.PhotographerUrl);

        var req = Assert.Single(h.Requests);
        Assert.Equal("Client-ID secret", req.Headers.Authorization!.ToString());
        Assert.Contains("orientation=landscape", req.RequestUri!.Query);
        Assert.Contains("query=night%20sky", req.RequestUri.Query);
        Assert.Equal(49, p.RemainingRequests);
    }

    [Fact]
    public async Task Missing_key_fails_without_calling_api()
    {
        var (p, h) = Create(_ => StubHandler.Json(OnePhoto), key: null);
        var ex = await Assert.ThrowsAsync<ProviderException>(() => p.GetRandomAsync(new("x", new(1, 1)), default));
        Assert.Equal(ProviderErrorKind.MissingKey, ex.Kind);
        Assert.Empty(h.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 10, ProviderErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, 0, ProviderErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.NotFound, 10, ProviderErrorKind.NoResults)]
    [InlineData(HttpStatusCode.InternalServerError, 10, ProviderErrorKind.BadResponse)]
    public async Task Maps_http_errors(HttpStatusCode code, int remaining, ProviderErrorKind expected)
    {
        var (p, _) = Create(_ => StubHandler.Json("{}", code, remaining));
        var ex = await Assert.ThrowsAsync<ProviderException>(() => p.GetRandomAsync(new("x", new(1, 1)), default));
        Assert.Equal(expected, ex.Kind);
    }

    [Fact]
    public async Task Stops_calling_api_once_rate_limited()
    {
        var (p, h) = Create(_ => StubHandler.Json(OnePhoto, remaining: 0));
        await p.GetRandomAsync(new("x", new(1, 1)), default); // succeeds, but that was the last request

        var ex = await Assert.ThrowsAsync<ProviderException>(() => p.GetRandomAsync(new("x", new(1, 1)), default));
        Assert.Equal(ProviderErrorKind.RateLimited, ex.Kind);
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task Offline_maps_to_network_error()
    {
        var (p, _) = Create(_ => throw new HttpRequestException("no route"));
        var ex = await Assert.ThrowsAsync<ProviderException>(() => p.GetRandomAsync(new("x", new(1, 1)), default));
        Assert.Equal(ProviderErrorKind.Network, ex.Kind);
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public void Download_url_requests_exact_size_and_quality()
    {
        var url = UnsplashProvider.BuildDownloadUrl("https://images.unsplash.com/photo-1?ixid=foo", new PixelSize(3840, 2160));
        Assert.Equal("https://images.unsplash.com/photo-1?ixid=foo&w=3840&h=2160&fit=crop&crop=entropy&q=90&fm=jpg", url);
    }

    [Fact]
    public async Task Tracking_hits_download_location()
    {
        var (p, h) = Create(_ => StubHandler.Json("{\"url\":\"x\"}"));
        await p.TrackUsageAsync(FakeProvider.Photo("abc"), default);
        Assert.Equal("https://api.unsplash.com/photos/abc/download", h.Requests.Single().RequestUri!.ToString());
    }
}
