using Oriel.Core.Auth;
using Oriel.Core.Models;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Oriel.Core.Tests;

/// <summary>Time-of-day photos, one photo per monitor, the photo prepared ahead of time, following, and the daily counts.</summary>
public sealed class PersonalTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly FakeProvider _provider = new();
    readonly FakeSetter _setter = new();
    // 9 pm wherever the tests run: the mood reads local time.
    readonly FakeClock _clock = new(new DateTimeOffset(new DateTime(2026, 10, 3, 21, 0, 0, DateTimeKind.Local)));
    readonly JsonSettingsStore _store;
    readonly SqliteHistoryStore _history;
    readonly WallpaperService _svc;

    public PersonalTests()
    {
        var paths = _dir.Paths;
        paths.EnsureCreated();
        _store = new JsonSettingsStore(paths);
        _history = new SqliteHistoryStore(paths);
        _svc = new WallpaperService(_provider, _setter, _history, _store, new RandomTopicPicker(new Random(1)),
            _clock, paths, NullLogger<WallpaperService>.Instance);
    }

    public void Dispose() => _dir.Dispose();

    static PhotoCandidate Colored(string id, string color) => FakeProvider.Photo(id) with { Color = color };

    // ---------------------------------------------------------------- time of day

    [Theory]
    [InlineData(21, false, Mood.Dark)]
    [InlineData(3, false, Mood.Dark)]
    [InlineData(8, false, Mood.Bright)]
    [InlineData(14, false, Mood.Any)]
    [InlineData(14, true, Mood.Dark)]
    public void Mood_follows_the_hour_and_dark_mode(int hour, bool dark, Mood expected) =>
        Assert.Equal(expected, PhotoMood.For(new DateTime(2026, 10, 3, hour, 0, 0), dark));

    [Fact]
    public void Luminance_reads_hex_colors()
    {
        Assert.Equal(0, PhotoMood.Luminance("#000000"));
        Assert.Equal(1, PhotoMood.Luminance("#FFFFFF")!.Value, 3);
        Assert.Null(PhotoMood.Luminance("red"));
    }

    [Fact]
    public void Photos_that_suit_the_mood_come_first_and_none_are_dropped()
    {
        var ordered = PhotoMood.Order([Colored("bright", "#F2F2F2"), Colored("dark", "#141414")], Mood.Dark).Select(p => p.Id).ToList();
        Assert.Equal(["dark", "bright"], ordered);
    }

    [Fact]
    public async Task In_the_evening_a_dark_photo_is_picked()
    {
        _provider.Responses.Enqueue(() => [Colored("bright", "#F2F2F2"), Colored("dark", "#141414")]);
        var r = await _svc.ApplyAsync(force: true);
        Assert.Equal("dark", Assert.Single(r.Entries).PhotoId);
    }

    [Fact]
    public async Task Time_of_day_can_be_turned_off()
    {
        _store.Update(s => s.MatchTimeOfDay = false);
        _provider.Responses.Enqueue(() => [Colored("bright", "#F2F2F2"), Colored("dark", "#141414")]);
        var r = await _svc.ApplyAsync(force: true);
        Assert.Equal("bright", Assert.Single(r.Entries).PhotoId);
    }

    // ---------------------------------------------------------------- monitors

    [Fact]
    public void Each_monitor_can_get_its_own_photo_sized_for_it()
    {
        var monitors = new List<MonitorInfo> { new("A", 0, 0, 1920, 1080), new("B", 1920, 0, 3840, 2160) };
        var each = WallpaperService.PlanTargets(monitors, new OrielSettings { PhotoPerMonitor = true });
        Assert.Equal([new PixelSize(1920, 1080), new PixelSize(3840, 2160)], each.Select(t => t.Size));
        Assert.Equal(["A", "B"], each.Select(t => t.MonitorPath));
        // A photo chosen by hand is still one photo, sized for the biggest screen.
        Assert.Equal(new PixelSize(3840, 2160), Assert.Single(WallpaperService.PlanTargets(monitors, new OrielSettings { PhotoPerMonitor = true }, perMonitor: false)).Size);
    }

    [Fact]
    public async Task With_two_monitors_each_gets_a_different_photo()
    {
        _setter.Monitors.Add(new("MON2", 2560, 0, 2560, 1440));
        _store.Update(s => s.PhotoPerMonitor = true);
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("a")]);
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("b")]);
        var r = await _svc.ApplyAsync(force: true);
        Assert.Equal(["a", "b"], r.Entries.Select(e => e.PhotoId));
        Assert.Equal(["MON1", "MON2"], _setter.Calls.Select(c => c.Monitor));
    }

    // ---------------------------------------------------------------- ahead of time

    [Fact]
    public async Task The_prepared_photo_is_used_next_without_searching()
    {
        _svc.PrefetchEnabled = true;
        _store.Update(s => s.MatchTimeOfDay = false);
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("ready")]);
        await _svc.PrefetchAsync();

        var r = await _svc.ApplyAsync(force: true); // no search response queued: it must come from the prepared one
        Assert.Equal(ApplyStatus.Applied, r.Status);
        Assert.Equal("ready", Assert.Single(r.Entries).PhotoId);
        // Still counted as a download on Unsplash when it is used, just after the change instead of holding it up.
        await _svc.AfterChangeDone;
        Assert.Equal(["ready"], _provider.Tracked);
        Assert.True(File.Exists(Assert.Single(_setter.Calls).Path));
    }

    [Fact]
    public async Task A_prepared_photo_for_a_topic_turned_off_is_thrown_away()
    {
        _svc.PrefetchEnabled = true;
        _store.Update(s => s.MatchTimeOfDay = false);
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("ready")]);
        await _svc.PrefetchAsync();
        _store.Update(s => s.Topics = [new TopicSetting { Query = "forest", Enabled = true }]);

        _provider.Responses.Enqueue(() => [FakeProvider.Photo("fresh")]);
        var r = await _svc.ApplyAsync(force: true);
        Assert.Equal("fresh", Assert.Single(r.Entries).PhotoId);
    }

    [Fact]
    public async Task Two_photos_are_prepared_so_both_changes_of_the_hour_are_instant()
    {
        _svc.PrefetchEnabled = true;
        _store.Update(s => s.MatchTimeOfDay = false);
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("first")]);
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("first"), FakeProvider.Photo("second")]);
        await _svc.PrefetchAsync();
        Assert.Equal(2, _provider.Queries.Count);

        // No search response queued: both come from the prepared ones, each a different photo.
        var a = await _svc.ApplyAsync(force: true);
        var b = await _svc.ApplyAsync(force: true);
        Assert.Equal(["first", "second"], new[] { a, b }.Select(r => Assert.Single(r.Entries).PhotoId));
        Assert.Equal(2, _provider.Queries.Count);
    }

    [Fact]
    public async Task A_prepared_photo_is_used_even_if_it_doesnt_suit_the_time_of_day()
    {
        // Time of day is a preference: a bright photo prepared earlier still makes the evening change instant.
        _svc.PrefetchEnabled = true;
        _provider.Responses.Enqueue(() => [Colored("bright", "#F2F2F2")]);
        await _svc.PrefetchAsync();

        var r = await _svc.ApplyAsync(force: true);
        Assert.Equal("bright", Assert.Single(r.Entries).PhotoId);
    }

    [Fact]
    public async Task Of_two_prepared_photos_the_one_that_suits_the_moment_goes_first()
    {
        _svc.PrefetchEnabled = true;
        _store.Update(s => s.MatchTimeOfDay = false);
        _provider.Responses.Enqueue(() => [Colored("bright", "#F2F2F2")]);
        _provider.Responses.Enqueue(() => [Colored("dark", "#141414")]);
        await _svc.PrefetchAsync();
        _store.Update(s => s.MatchTimeOfDay = true); // 9 pm: dark first

        var r = await _svc.ApplyAsync(force: true);
        Assert.Equal("dark", Assert.Single(r.Entries).PhotoId);
    }

    [Fact]
    public void Saved_interests_follow_the_new_built_in_list()
    {
        var s = new OrielSettings
        {
            Topics =
            [
                new TopicSetting { Query = "nature", Enabled = true },
                new TopicSetting { Query = "minimal", Enabled = true },   // no longer built in, but on: kept as your own
                new TopicSetting { Query = "abstract", Enabled = false }, // no longer built in, off: gone
                new TopicSetting { Query = "tokyo night", Enabled = false, IsCustom = true },
            ],
        };
        s.UpdateBuiltInTopics();
        Assert.Equal(OrielSettings.BuiltInTopics, s.Topics.Where(t => !t.IsCustom).Select(t => t.Query));
        Assert.True(s.Topics.Single(t => t.Query == "nature").Enabled);
        Assert.False(s.Topics.Single(t => t.Query == "waterfalls").Enabled); // new ones start off
        Assert.True(s.Topics.Single(t => t.Query == "minimal").IsCustom);
        Assert.DoesNotContain(s.Topics, t => t.Query == "abstract");
        Assert.Contains(s.Topics, t => t.Query == "tokyo night" && t.IsCustom);
    }

    // ---------------------------------------------------------------- following

    [Fact]
    public async Task While_following_a_friend_the_schedule_does_nothing()
    {
        _store.Update(s => s.FollowUid = "friend");
        Assert.Equal(ApplyStatus.Paused, (await _svc.ApplyAsync(force: false)).Status);
    }

    [Theory]
    [InlineData("k7mq-2xpa", "K7MQ2XPA")]
    [InlineData(" K7MQ 2XPA ", "K7MQ2XPA")]
    [InlineData("K7MQ2XP", null)]
    [InlineData("K7MQ2XP0", null)] // 0 is never used: it looks like O
    public void Follow_codes_are_forgiving_to_type(string input, string? expected) => Assert.Equal(expected, FollowService.Normalize(input));

    [Theory]
    [InlineData("k7mq", "K7MQ")]
    [InlineData("k7mq2", "K7MQ-2")]
    [InlineData(" k7 mq-2xpa ", "K7MQ-2XPA")]
    [InlineData("K7MQ-2XPA-EXTRA", "K7MQ-2XPA")]
    [InlineData("", "")]
    public void A_code_is_tidied_as_it_is_typed(string typed, string shown) => Assert.Equal(shown, FollowService.FormatInput(typed));

    [Theory]
    [InlineData("oriel://follow?code=K7MQ2XPA", "K7MQ2XPA")]
    [InlineData("oriel://follow?code=k7mq-2xpa", "K7MQ2XPA")]
    [InlineData("oriel://auth?token=t&follow=K7MQ2XPA", "K7MQ2XPA")]
    [InlineData("oriel://follow?code=nope", null)]
    [InlineData("oriel://wallpaper?id=abcdef&code=K7MQ2XPA", null)]
    public void Follow_links_are_read_from_the_page_and_through_sign_in(string arg, string? expected)
    {
        var found = DeepLink.TryGetFollowCode([arg], out var code);
        Assert.Equal(expected is not null, found);
        if (found) Assert.Equal(expected, code);
    }

    [Fact]
    public void New_follow_codes_are_valid_and_shown_in_two_halves()
    {
        var code = FollowService.NewCode();
        Assert.Equal(code, FollowService.Normalize(code));
        Assert.Equal($"{code[..4]}-{code[4..]}", FollowService.Format(code));
    }

    // ---------------------------------------------------------------- daily counts

    static readonly DateOnly Day = new(2026, 10, 3);

    [Fact]
    public void A_new_device_counts_as_new_and_active_once_a_day()
    {
        var first = StatsMemory.ForActiveDay(new StatsMemory(), Day, "windows", null)!.Value;
        Assert.Equal(["windows_active", "windows_new"], first.Fields);
        Assert.Null(StatsMemory.ForActiveDay(first.Next, Day, "windows", null));
    }

    [Fact]
    public void An_existing_install_is_not_counted_as_new()
    {
        var step = StatsMemory.ForActiveDay(new StatsMemory(), Day, "linux", Day.AddDays(-40))!.Value;
        Assert.Equal(["linux_active", StatFields.Retained7, StatFields.Retained30], step.Fields);
    }

    [Fact]
    public void Retention_is_counted_once_after_7_and_30_days()
    {
        var m = new StatsMemory { FirstDay = Day, ActiveDay = Day };
        var week = StatsMemory.ForActiveDay(m, Day.AddDays(8), "windows", null)!.Value;
        Assert.Contains(StatFields.Retained7, week.Fields);
        Assert.DoesNotContain(StatFields.Retained30, week.Fields);
        var later = StatsMemory.ForActiveDay(week.Next, Day.AddDays(9), "windows", null)!.Value;
        Assert.Equal(["windows_active"], later.Fields);
        var month = StatsMemory.ForActiveDay(later.Next, Day.AddDays(31), "windows", null)!.Value;
        Assert.Equal(["windows_active", StatFields.Retained30], month.Fields);
    }

    // ---------------------------------------------------------------- share carried through sign-in

    [Fact]
    public void A_shared_photo_survives_signing_in()
    {
        Assert.True(DeepLink.TryGetPendingShare(["oriel://auth?token=abc&share=AbC123xyz&from=Akbar"], out var id, out var from));
        Assert.Equal("AbC123xyz", id);
        Assert.Equal("Akbar", from);
        Assert.False(DeepLink.TryGetPendingShare(["oriel://auth?token=abc"], out _, out _));
        Assert.False(DeepLink.TryGetPendingShare(["oriel://auth?token=abc&share=../x"], out _, out _));
    }
}
