using Oriel.Core.Auth;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Scheduling;

public static class ScheduleRules
{
    /// <summary>Longest sleep between checks: catches sleep/resume and clock changes without system hooks.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How often a signed-in app looks at the account's <c>sync/current</c> (one small document), so a change on any
    /// other device (phone, other computer) shows here within a few minutes.
    /// </summary>
    public static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(3);

    /// <summary>A timer or Task Scheduler run a moment before the tick still counts as that tick.</summary>
    static readonly TimeSpan Slack = TimeSpan.FromSeconds(30);

    // Every device of an account changes at the same moments: the daily time in the shared time zone (e.g. 09:00
    // Asia/Tashkent), then every interval from there, starting over at each day's daily time. Android's Schedule.kt
    // computes the very same ticks, so "first device wins" (the slot) can tell which change a device is making.

    static DateTimeOffset Anchor(DateOnly date, OrielSettings s, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(s.GetDailyTime());
        if (zone.IsInvalidTime(local)) local = local.AddHours(1); // skipped by a clock change: the hour after
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    static DateOnly LocalDate(DateTimeOffset at, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    /// <summary>The latest tick at or before <paramref name="now"/>.</summary>
    public static DateTimeOffset PreviousTick(OrielSettings s, DateTimeOffset now)
    {
        var zone = s.Zone();
        var today = LocalDate(now, zone);
        var anchor = Anchor(today, s, zone);
        if (anchor > now) anchor = Anchor(today.AddDays(-1), s, zone);
        if (s.Interval is not { } step) return anchor;
        var steps = (now - anchor).Ticks / step.Ticks;
        return anchor + TimeSpan.FromTicks(steps * step.Ticks);
    }

    /// <summary>The first tick after <paramref name="now"/>.</summary>
    public static DateTimeOffset NextTick(OrielSettings s, DateTimeOffset now)
    {
        var zone = s.Zone();
        var prev = PreviousTick(s, now);
        var prevDate = LocalDate(prev, zone);
        if (s.Interval is not { } step) return Anchor(prevDate.AddDays(1), s, zone);
        var candidate = prev + step;
        var sameDay = Anchor(prevDate, s, zone);
        var nextAnchor = sameDay > prev ? sameDay : Anchor(prevDate.AddDays(1), s, zone);
        return candidate > nextAnchor ? nextAnchor : candidate;
    }

    /// <summary>The tick a change made now belongs to.</summary>
    public static DateTimeOffset DueTick(OrielSettings s, DateTimeOffset now) => PreviousTick(s, now + Slack);

    /// <summary>The tick's name, the same on every device: "2026-10-14T09:00" in the shared time zone.</summary>
    public static string SlotId(OrielSettings s, DateTimeOffset tick) =>
        TimeZoneInfo.ConvertTime(tick, s.Zone()).ToString("yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    static DateTimeOffset? Baseline(OrielState state) =>
        state.LastAppliedAt is { } applied && (state.ScheduleStartedAt is not { } started || applied >= started) ? applied : state.ScheduleStartedAt;

    /// <summary>True when the current tick's change was already made (here, or the schedule started after it).</summary>
    public static bool IsFresh(OrielSettings settings, OrielState state, DateTimeOffset now) =>
        Baseline(state) is { } done && done >= DueTick(settings, now);

    /// <summary>Should the scheduled change happen now? Yes once a tick has passed without one (also one missed while off).</summary>
    public static bool IsDue(OrielSettings settings, OrielState state, DateTimeOffset now, bool isStartupCheck)
    {
        if (settings.Paused || settings.IsFollowing) return false;
        if (IsFresh(settings, state, now)) return false;
        if (state.NextRetryAt is { } retry && now < retry) return false;
        // Missed while the computer was off: caught up at login only when "change at login" is on.
        if (isStartupCheck && !settings.ApplyAtLogin) return false;
        return true;
    }

    /// <summary>How long the scheduler can sleep before the next check (1s..MaxWait).</summary>
    public static TimeSpan NextCheckIn(OrielSettings settings, OrielState state, DateTimeOffset now)
    {
        if (settings.Paused) return MaxWait;
        // Following a friend is a poll of one small document every few minutes.
        if (settings.IsFollowing) return SyncInterval;
        var due = IsFresh(settings, state, now) ? NextTick(settings, now) : now;
        if (state.NextRetryAt is { } retry && retry > due) due = retry;
        var wait = due - now;
        return wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait > MaxWait ? MaxWait : wait;
    }
}

/// <summary>
/// In-app scheduler. Sleeps until the next due time (at most <see cref="ScheduleRules.MaxWait"/>, which
/// copes with sleep/resume and clock changes) and wakes early when settings change. Idle in between.
/// </summary>
public sealed class DailyScheduler(WallpaperService wallpapers, RetentionService retention, ISettingsStore store,
    IClock clock, ILogger<DailyScheduler> log, IAuthState? auth = null, FollowService? follow = null, StatsReporter? stats = null,
    PulseListener? pulse = null) : IDisposable
{
    CancellationTokenSource? _cts;
    TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    DateOnly? _lastCleanup;

    /// <summary>Raised after every scheduled attempt (success or failure) so the UI can update.</summary>
    public event EventHandler<ApplyResult>? Attempted;

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        store.SettingsChanged += OnSettingsChanged;
        if (auth is not null) auth.Changed += OnSettingsChanged;
        _ = RunAsync(_cts.Token);
    }

    void OnSettingsChanged(object? sender, EventArgs e) => _wake.TrySetResult();

    async Task RunAsync(CancellationToken ct)
    {
        var first = true;
        while (!ct.IsCancellationRequested)
        {
            _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var wait = ScheduleRules.MaxWait;
            try
            {
                await CheckAsync(first, ct);
                if (auth is not { IsSignedIn: false })
                {
                    var settings = store.Load();
                    wait = ScheduleRules.NextCheckIn(settings, store.LoadState(), clock.Now);
                    // Signed in: also wake up to pick up a wallpaper another device of the account set.
                    // The live connection brings changes at once; then a rare poll is only a safety net. Also while
                    // locked: a photo set by hand elsewhere still shows everywhere.
                    var poll = pulse is { IsConnected: true } ? ScheduleRules.MaxWait : ScheduleRules.SyncInterval;
                    if (follow is not null && wait > poll) wait = poll;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Scheduler check failed");
            }
            first = false;
            try { await Task.WhenAny(Task.Delay(wait, ct), _wake.Task); }
            catch (OperationCanceledException) { return; }
        }
    }

    async Task CheckAsync(bool isStartup, CancellationToken ct)
    {
        if (_lastCleanup != clock.Today)
        {
            retention.Cleanup();
            _lastCleanup = clock.Today;
        }

        if (auth is { IsSignedIn: false }) return;
        if (stats is not null) await stats.MarkActiveAsync();
        // The account's photo first (or the followed friend's): when another device already changed it, for this very
        // tick too, that one shows here and no second photo is picked.
        if (follow is not null) await follow.CheckAsync(ct);
        if (store.Load().IsFollowing) return;
        // Missed while off and "change at login" is off: wait for the next tick instead of catching up later.
        if (isStartup && !store.Load().ApplyAtLogin && !ScheduleRules.IsFresh(store.Load(), store.LoadState(), clock.Now))
        {
            var skipped = store.LoadState();
            skipped.ScheduleStartedAt = clock.Now;
            store.SaveState(skipped);
            return;
        }
        if (!ScheduleRules.IsDue(store.Load(), store.LoadState(), clock.Now, isStartup)) return;

        log.LogInformation("Scheduled change is due (startup={Startup})", isStartup);
        var result = await wallpapers.ApplyAsync(force: false, ct);
        // Another device took this moment: its photo arrives through sync/current (the doorbell, or this check).
        if (result.Status == ApplyStatus.TakenByOtherDevice && follow is not null) await follow.CheckAsync(ct);
        Attempted?.Invoke(this, result);
    }

    public void Dispose()
    {
        store.SettingsChanged -= OnSettingsChanged;
        if (auth is not null) auth.Changed -= OnSettingsChanged;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
