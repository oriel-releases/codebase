using Oriel.Core.Models;
using Oriel.Core.Providers;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Oriel.Core.Tests;

public sealed class WallpaperServiceTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly FakeProvider _provider = new();
    readonly FakeSetter _setter = new();
    readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.FromHours(5)));
    readonly JsonSettingsStore _store;
    readonly SqliteHistoryStore _history;
    readonly WallpaperService _svc;

    public WallpaperServiceTests()
    {
        var paths = _dir.Paths;
        paths.EnsureCreated();
        _store = new JsonSettingsStore(paths);
        _history = new SqliteHistoryStore(paths);
        _svc = new WallpaperService(_provider, _setter, _history, _store, new RandomTopicPicker(new Random(1)),
            _clock, paths, NullLogger<WallpaperService>.Instance);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Applies_downloads_records_and_tracks()
    {
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("p1")]);

        var r = await _svc.ApplyAsync(force: false);

        Assert.Equal(ApplyStatus.Applied, r.Status);
        var call = Assert.Single(_setter.Calls);
        Assert.Null(call.Monitor);
        Assert.True(File.Exists(call.Path));
        Assert.EndsWith("2026-10-03_unsplash_p1.jpg", call.Path);
        Assert.Equal([call.Path], _setter.AllDesktopCalls);
        Assert.True(_history.WasUsed("unsplash", "p1"));
        Assert.Equal(["p1"], _provider.Tracked);
        Assert.Equal(_clock.Today, _store.LoadState().LastAppliedDate);
    }

    [Fact]
    public async Task Applying_a_chosen_photo_sets_it_and_counts_as_todays_change()
    {
        var r = await _svc.ApplyPhotoAsync(FakeProvider.Photo("pick"), "northern lights");

        Assert.Equal(ApplyStatus.Applied, r.Status);
        Assert.EndsWith("_unsplash_pick.jpg", Assert.Single(_setter.Calls).Path);
        Assert.Equal("northern lights", Assert.Single(r.Entries).Topic);
        Assert.Equal(["pick"], _provider.Tracked);
        Assert.Equal(ApplyStatus.AlreadyAppliedToday, (await _svc.ApplyAsync(false)).Status);

        // Choosing the same photo again just refreshes its history row.
        Assert.Equal(ApplyStatus.Applied, (await _svc.ApplyPhotoAsync(FakeProvider.Photo("pick"))).Status);
    }

    [Fact]
    public async Task Search_keeps_only_landscape_photos_big_enough_for_the_screen()
    {
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("big"), FakeProvider.Photo("small", 800, 600), FakeProvider.Photo("tall", 3000, 4000)]);

        var page = await _svc.SearchAsync("mountains", 1);

        Assert.Equal(["big"], page.Photos.Select(p => p.Id));
    }

    [Fact]
    public async Task Favorites_source_reuses_a_starred_photo_without_downloading()
    {
        var files = new[] { "fav1", "fav2" }.Select(id =>
        {
            var file = Path.Combine(_dir.Paths.DataRoot, id + ".jpg");
            File.WriteAllBytes(file, FakeProvider.Jpeg());
            _history.SetFavorite(_history.Add(new HistoryEntry
            {
                Provider = "unsplash", PhotoId = id, FilePath = file, ThumbnailUrl = "t", PhotoPageUrl = "p",
                PhotographerName = "n", PhotographerUrl = "u", AppliedAt = _clock.Now.AddDays(-1),
            }), true);
            return file;
        }).ToList();
        _store.Update(s => s.FavoritesOnly = true);

        var r = await _svc.ApplyAsync(force: true);

        Assert.Equal(ApplyStatus.Applied, r.Status);
        Assert.Contains(Assert.Single(_setter.Calls).Path, files);
        Assert.Empty(_provider.Queries);
        Assert.Empty(_provider.Tracked);
    }

    [Fact]
    public async Task Favorites_source_falls_back_to_new_photos_when_nothing_is_starred()
    {
        _store.Update(s => s.FavoritesOnly = true);
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("p1")]);

        Assert.Equal(ApplyStatus.Applied, (await _svc.ApplyAsync(force: true)).Status);
        Assert.Single(_provider.Queries);
    }

    [Fact]
    public async Task Second_run_same_day_is_noop_unless_forced()
    {
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("p1")]);
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("p2")]);
        await _svc.ApplyAsync(false);

        Assert.Equal(ApplyStatus.AlreadyAppliedToday, (await _svc.ApplyAsync(false)).Status);
        Assert.Equal(ApplyStatus.Applied, (await _svc.ApplyAsync(true)).Status);
        Assert.Equal(2, _setter.Calls.Count);
    }

    [Fact]
    public async Task Never_reuses_a_photo()
    {
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("p1")]);
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("p1"), FakeProvider.Photo("p2")]);
        await _svc.ApplyAsync(true);
        var r = await _svc.ApplyAsync(true);
        Assert.Equal("p2", r.Entries.Single().PhotoId);
    }

    [Fact]
    public async Task Filters_small_portrait_and_low_likes()
    {
        var s = _store.Load();
        s.SkipLowLikes = true;
        // Older settings may hold less than 300, the least the slider goes down to: 300 still applies.
        s.MinLikes = 10;
        _store.Save(s);

        _provider.Responses.Enqueue(() =>
        [
            FakeProvider.Photo("small", 1920, 1080, likes: 500),
            FakeProvider.Photo("portrait", 4000, 6000, likes: 500),
            FakeProvider.Photo("unloved", likes: 120),
            FakeProvider.Photo("good", likes: 500),
        ]);

        var r = await _svc.ApplyAsync(true);
        Assert.Equal("good", r.Entries.Single().PhotoId);
    }

    [Fact]
    public async Task Failure_keeps_current_wallpaper_and_schedules_retry()
    {
        // No responses -> no candidates on every attempt.
        var r = await _svc.ApplyAsync(false);

        Assert.Equal(ApplyStatus.Failed, r.Status);
        Assert.Empty(_setter.Calls);
        var state = _store.LoadState();
        Assert.Null(state.LastAppliedDate);
        Assert.Equal(1, state.ConsecutiveFailures);
        Assert.Equal(_clock.Now + RetryBackoff.Delay(1), state.NextRetryAt);
        Assert.NotNull(state.LastError);
    }

    [Fact]
    public async Task Bad_download_never_reaches_the_desktop()
    {
        _provider.Bytes = "<html>oops</html>"u8.ToArray();
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("p1")]);

        var r = await _svc.ApplyAsync(false);

        Assert.Equal(ApplyStatus.Failed, r.Status);
        Assert.Empty(_setter.Calls);
        Assert.False(_history.WasUsed("unsplash", "p1"));
    }

    [Fact]
    public async Task Missing_key_is_not_transient()
    {
        _provider.Configured = false;
        var r = await _svc.ApplyAsync(false);
        Assert.Equal(ProviderErrorKind.MissingKey, r.ErrorKind);
        Assert.False(r.IsTransientFailure);
    }

    [Fact]
    public async Task Paused_skips_scheduled_but_not_forced()
    {
        var s = _store.Load();
        s.Paused = true;
        _store.Save(s);
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("p1")]);

        Assert.Equal(ApplyStatus.Paused, (await _svc.ApplyAsync(false)).Status);
        Assert.Equal(ApplyStatus.Applied, (await _svc.ApplyAsync(true)).Status);
    }

    [Fact]
    public async Task Change_now_is_limited_to_two_per_hour()
    {
        for (var i = 1; i <= 3; i++)
        {
            var id = $"p{i}";
            _provider.Responses.Enqueue(() => [FakeProvider.Photo(id)]);
        }

        Assert.Equal(ApplyStatus.Applied, (await _svc.ApplyAsync(true)).Status);
        Assert.Equal(ApplyStatus.Applied, (await _svc.ApplyAsync(true)).Status);
        Assert.Equal(ApplyStatus.RateLimited, (await _svc.ApplyAsync(true)).Status);
        Assert.Equal(2, _setter.Calls.Count);

        var at = WallpaperService.ManualChangeAvailableAt(_store.LoadState(), _clock.Now);
        Assert.Equal(_clock.Now + TimeSpan.FromHours(1), at);
    }

    [Fact]
    public async Task Manual_change_does_not_move_the_schedule_and_automatic_still_runs_when_limited()
    {
        for (var i = 1; i <= 3; i++)
        {
            var id = $"p{i}";
            _provider.Responses.Enqueue(() => [FakeProvider.Photo(id)]);
        }
        await _svc.ApplyAsync(true);
        await _svc.ApplyAsync(true);

        var state = _store.LoadState();
        Assert.Null(state.LastAppliedDate);
        Assert.Null(state.LastAppliedAt);
        Assert.NotNull(WallpaperService.ManualChangeAvailableAt(state, _clock.Now));

        // Change now is blocked, but the app's own change still happens, and is not counted against the limit.
        Assert.Equal(ApplyStatus.Applied, (await _svc.ApplyAsync(false)).Status);
        Assert.Equal(_clock.Today, _store.LoadState().LastAppliedDate);
        Assert.Equal(2, _store.LoadState().ManualChanges.Count);
    }

    [Fact]
    public async Task Failed_manual_change_does_not_hold_back_the_automatic_one()
    {
        var r = await _svc.ApplyAsync(true); // no photos available

        Assert.Equal(ApplyStatus.Failed, r.Status);
        var state = _store.LoadState();
        Assert.Null(state.NextRetryAt);
        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.Empty(state.ManualChanges);
    }

    [Fact]
    public void Span_targets_whole_virtual_desktop()
    {
        var monitors = new List<MonitorInfo> { new("A", 0, 0, 2560, 1440), new("B", 2560, 0, 1920, 1080) };
        var t = WallpaperService.PlanTargets(monitors, new OrielSettings { Fit = WallpaperFit.Span });
        Assert.Equal(new PixelSize(4480, 1440), Assert.Single(t).Size);
    }

    [Fact]
    public void Same_on_all_sizes_for_biggest_monitor()
    {
        var monitors = new List<MonitorInfo> { new("A", 0, 0, 1920, 1080), new("B", 1920, 0, 3840, 2160) };
        var t = WallpaperService.PlanTargets(monitors, new OrielSettings());
        Assert.Equal(new PixelSize(3840, 2160), Assert.Single(t).Size);
    }
}
