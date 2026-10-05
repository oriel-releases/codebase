using Oriel.Core.Auth;
using Oriel.Core.Providers;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Oriel.Core.Tests;

public sealed class AuthTests : IDisposable
{
    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("oriel://auth?token=abc.def-ghi", "abc.def-ghi")]
    [InlineData("\"oriel://auth?token=a%2Bb%3D\"", "a+b=")]
    [InlineData("ORIEL://AUTH?token=x", "x")]
    public void Reads_the_token_from_the_link(string arg, string expected)
    {
        Assert.True(DeepLink.TryGetGoogleToken(["--background", arg], out var token));
        Assert.Equal(expected, token);
    }

    [Theory]
    [InlineData("oriel://auth")]
    [InlineData("oriel://auth?token=")]
    [InlineData("oriel://other?token=x")]
    [InlineData("https://auth?token=x")]
    [InlineData("--background")]
    public void Ignores_links_without_a_usable_token(string arg) =>
        Assert.False(DeepLink.TryGetGoogleToken([arg], out _));

    [Fact]
    public void Recognises_links_even_without_a_token()
    {
        Assert.True(DeepLink.IsLink(["oriel://auth"]));
        Assert.False(DeepLink.IsLink(["--background"]));
    }

    [Fact]
    public void Account_is_stored_encrypted_and_read_back()
    {
        var store = new DpapiAccountStore(_dir.Paths);
        Assert.Null(store.Load());

        store.Save(new Account { Uid = "u1", Email = "a@b.c", RefreshToken = "secret-refresh-token", SessionId = "s1" });

        Assert.DoesNotContain("secret-refresh-token", File.ReadAllText(_dir.Paths.AccountFile));
        var loaded = store.Load();
        Assert.Equal("a@b.c", loaded?.Email);
        Assert.Equal("secret-refresh-token", loaded?.RefreshToken);

        store.Clear();
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task Changing_the_wallpaper_requires_sign_in()
    {
        var paths = _dir.Paths;
        paths.EnsureCreated();
        var provider = new FakeProvider();
        provider.Responses.Enqueue(() => [FakeProvider.Photo("p1")]);
        var svc = new WallpaperService(provider, new FakeSetter(), new SqliteHistoryStore(paths), new JsonSettingsStore(paths),
            new RandomTopicPicker(new Random(1)), new FakeClock(DateTimeOffset.Now), paths,
            NullLogger<WallpaperService>.Instance, auth: new SignedOut());

        var result = await svc.ApplyAsync(force: true);

        Assert.Equal(ApplyStatus.SignInRequired, result.Status);
        Assert.Empty(provider.Tracked);
    }

    sealed class SignedOut : IAuthState
    {
        public bool IsSignedIn => false;
        public event EventHandler? Changed { add { } remove { } }
    }
}
