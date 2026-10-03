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
    public async Task Filters_small_portrait_low_likes_and_excluded()
    {
        var s = _store.Load();
        s.SkipLowLikes = true;
        s.MinLikes = 10;
        s.ExcludedKeywords = ["people"];
        _store.Save(s);

        _provider.Responses.Enqueue(() =>
        [
            FakeProvider.Photo("small", 1920, 1080),
            FakeProvider.Photo("portrait", 4000, 6000),
            FakeProvider.Photo("unloved", likes: 2),
            FakeProvider.Photo("crowd", desc: "Group of PEOPLE on a beach"),
            FakeProvider.Photo("good"),
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
    public async Task Per_monitor_mode_sets_each_monitor_with_different_photos()
    {
        _setter.Monitors.Add(new("MON2", 2560, 0, 1920, 1080));
        var s = _store.Load();
        s.MonitorMode = MultiMonitorMode.PerMonitor;
        _store.Save(s);
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("a"), FakeProvider.Photo("b")]);
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("a"), FakeProvider.Photo("b")]);

        var r = await _svc.ApplyAsync(true);

        Assert.Equal(["MON1", "MON2"], _setter.Calls.Select(c => c.Monitor));
        Assert.Equal(["a", "b"], r.Entries.Select(e => e.PhotoId));
        // Other virtual desktops get the primary monitor's photo.
        Assert.Equal([_setter.Calls[0].Path], _setter.AllDesktopCalls);
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
