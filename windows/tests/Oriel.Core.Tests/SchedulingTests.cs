using Oriel.Core.Providers;
using Oriel.Core.Scheduling;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Oriel.Core.Tests;

public sealed class SchedulingTests : IDisposable
{
    const string Exe = @"C:\Oriel\Oriel.exe";

    readonly TempDir _dir = new();
    readonly FakeProvider _provider = new();
    readonly FakeSetter _setter = new();
    readonly FakeTasks _tasks = new();
    readonly FakeStartup _startup = new();
    readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.FromHours(5)));
    readonly JsonSettingsStore _store;
    readonly ScheduledRun _run;

    public SchedulingTests()
    {
        var paths = _dir.Paths;
        paths.EnsureCreated();
        _store = new JsonSettingsStore(paths);
        var history = new SqliteHistoryStore(paths);
        var wallpapers = new WallpaperService(_provider, _setter, history, _store, new RandomTopicPicker(new Random(1)),
            _clock, paths, NullLogger<WallpaperService>.Instance);
        var retention = new RetentionService(history, _store, _setter, _clock, NullLogger<RetentionService>.Instance);
        _run = new ScheduledRun(wallpapers, retention, _tasks, _store);
    }

    public void Dispose() => _dir.Dispose();

    static readonly TimeSpan Tz = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 3));
    static DateTimeOffset At(int h, int m = 0) => new(2026, 10, 3, h, m, 0, Tz);

    [Fact]
    public void Slideshow_ticks_on_a_grid_from_the_daily_time()
    {
        var s = new OrielSettings { IntervalMinutes = 60 };
        var state = new OrielState { LastAppliedAt = At(9) };

        Assert.False(ScheduleRules.IsDue(s, state, At(9, 30), isStartupCheck: false));
        Assert.False(ScheduleRules.IsDue(s, state, At(9, 59), isStartupCheck: false));
        Assert.True(ScheduleRules.IsDue(s, state, At(10), isStartupCheck: false));
        Assert.Equal(TimeSpan.FromMinutes(10), ScheduleRules.NextCheckIn(s, state, At(9, 50)));
        Assert.Equal("2026-10-03T10:00", ScheduleRules.SlotId(s, ScheduleRules.DueTick(s, At(10))));
    }

    [Fact]
    public void A_missed_tick_is_caught_up_once()
    {
        // Never changed: yesterday's 09:00 was missed, so the first check changes it.
        Assert.True(ScheduleRules.IsDue(new OrielSettings(), new OrielState(), At(7), isStartupCheck: true));
        Assert.True(ScheduleRules.IsDue(new OrielSettings(), new OrielState(), At(7), isStartupCheck: false));
        // Changed after today's 09:00: nothing until tomorrow's.
        Assert.False(ScheduleRules.IsDue(new OrielSettings(), new OrielState { LastAppliedAt = At(9, 1) }, At(15), false));
        // Changed before it: due once 09:00 comes.
        Assert.False(ScheduleRules.IsDue(new OrielSettings(), new OrielState { LastAppliedAt = At(8) }, At(8, 50), false));
        Assert.True(ScheduleRules.IsDue(new OrielSettings(), new OrielState { LastAppliedAt = At(8) }, At(9), false));
    }

    [Fact]
    public void Login_catch_up_can_be_disabled()
    {
        var s = new OrielSettings { ApplyAtLogin = false };
        Assert.False(ScheduleRules.IsDue(s, new OrielState(), At(7), isStartupCheck: true));
    }

    [Fact]
    public void Editing_the_schedule_starts_it_fresh()
    {
        // The time was moved to 09:00 at 09:30: that 09:00 doesn't count as missed.
        var state = new OrielState { LastAppliedAt = At(8), ScheduleStartedAt = At(9, 30) };
        Assert.False(ScheduleRules.IsDue(new OrielSettings(), state, At(9, 40), false));
    }

    [Fact]
    public void Not_due_when_paused_following_or_backing_off()
    {
        var missed = new OrielState { LastAppliedAt = At(8) };
        Assert.False(ScheduleRules.IsDue(new OrielSettings { Paused = true }, missed, At(10), false));
        Assert.False(ScheduleRules.IsDue(new OrielSettings { FollowUid = "friend" }, missed, At(10), false));
        Assert.False(ScheduleRules.IsDue(new OrielSettings(), new OrielState { LastAppliedAt = At(8), NextRetryAt = At(10, 5) }, At(10), false));
        Assert.True(ScheduleRules.IsDue(new OrielSettings(), new OrielState { LastAppliedAt = At(8), NextRetryAt = At(10, 5) }, At(10, 6), false));
    }

    [Fact]
    public void Scheduler_sleeps_until_the_next_tick_capped_at_max_wait()
    {
        var s = new OrielSettings();
        Assert.Equal(TimeSpan.FromMinutes(4), ScheduleRules.NextCheckIn(s, new OrielState { LastAppliedAt = At(8) }, At(8, 56)));
        Assert.Equal(ScheduleRules.MaxWait, ScheduleRules.NextCheckIn(s, new OrielState { LastAppliedAt = At(6) }, At(7)));
        Assert.Equal(TimeSpan.FromMinutes(3),
            ScheduleRules.NextCheckIn(s, new OrielState { LastAppliedAt = At(8), NextRetryAt = At(10, 3) }, At(10)));
        Assert.Equal(ScheduleRules.MaxWait, ScheduleRules.NextCheckIn(s, new OrielState { LastAppliedAt = At(9, 1) }, At(10)));
    }

    [Fact]
    public void Ticks_follow_the_shared_time_zone_whatever_this_computer_uses()
    {
        // 09:00 in Tokyo is 00:00 UTC: every device, wherever its clock is, ticks then and names it the same.
        var s = new OrielSettings { TimeZone = "Asia/Tokyo", DailyTime = "09:00" };
        var now = new DateTimeOffset(2026, 10, 3, 0, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), ScheduleRules.PreviousTick(s, now));
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero), ScheduleRules.NextTick(s, now));
        Assert.Equal("2026-10-03T09:00", ScheduleRules.SlotId(s, ScheduleRules.PreviousTick(s, now)));
    }

    [Fact]
    public async Task Offline_run_schedules_a_one_shot_retry()
    {
        _provider.Responses.Enqueue(() => throw new ProviderException(ProviderErrorKind.Network, "offline"));

        var r = await _run.RunAsync(force: false, Exe);

        Assert.Equal(ApplyStatus.Failed, r.Status);
        Assert.Equal(_store.LoadState().NextRetryAt, _tasks.RetryAt);
    }

    [Fact]
    public async Task Permanent_failure_and_success_schedule_no_retry()
    {
        _provider.Configured = false;
        Assert.Equal(ApplyStatus.Failed, (await _run.RunAsync(force: false, Exe)).Status);

        _provider.Configured = true;
        _provider.Responses.Enqueue(() => [FakeProvider.Photo("p1")]);
        Assert.Equal(ApplyStatus.Applied, (await _run.RunAsync(force: false, Exe)).Status);

        Assert.Empty(_tasks.Calls);
    }

    [Fact]
    public void Sync_registers_once_and_only_again_when_schedule_changes()
    {
        var sync = new ScheduleSync(_store, _startup, _tasks);

        sync.Sync(Exe);
        sync.Sync(Exe);                                     // nothing changed
        _store.Update(s => s.MinLikes = 5);
        sync.Sync(Exe);                                     // unrelated setting
        _store.Update(s => { s.DailyTime = "07:30"; s.ApplyAtLogin = false; });
        sync.Sync(Exe);

        Assert.Equal(["register 09:00 signin=False", "register 07:30 signin=False"], _tasks.Calls);
        Assert.True(_startup.Enabled);
    }

    [Fact]
    public void Pausing_removes_the_task_so_nothing_runs()
    {
        var sync = new ScheduleSync(_store, _startup, _tasks);
        sync.Sync(Exe);

        _store.Update(s => s.Paused = true);
        sync.Sync(Exe);
        _store.Update(s => s.Paused = false);
        sync.Sync(Exe);

        Assert.Equal(["register 09:00 signin=False", "unregister", "register 09:00 signin=False"], _tasks.Calls);
    }

    [Fact]
    public void Task_handles_sign_in_only_when_the_app_does_not_start_with_windows()
    {
        _store.Update(s => s.StartWithWindows = false);
        new ScheduleSync(_store, _startup, _tasks).Sync(Exe);

        Assert.False(_startup.Enabled);
        Assert.Equal(["register 09:00 signin=True"], _tasks.Calls);
    }

    [Fact]
    public void Forced_sync_reregisters_to_point_at_the_current_exe()
    {
        var sync = new ScheduleSync(_store, _startup, _tasks);
        sync.Sync(Exe);
        sync.Sync(Exe, force: true);
        Assert.Equal(2, _tasks.Calls.Count);
    }
}
