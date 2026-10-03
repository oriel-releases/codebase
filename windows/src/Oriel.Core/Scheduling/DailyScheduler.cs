using Oriel.Core.Services;
using Oriel.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Scheduling;

public static class ScheduleRules
{
    /// <summary>Longest sleep between checks: catches sleep/resume and clock changes without system hooks.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Should we change the wallpaper now?
    /// At login (first check after start) if today's isn't set yet; otherwise once the daily time has passed.
    /// </summary>
    public static bool IsDue(OrielSettings settings, OrielState state, DateTimeOffset now, bool isStartupCheck)
    {
        if (settings.Paused) return false;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        if (state.LastAppliedDate == today) return false;
        if (state.NextRetryAt is { } retry && now < retry) return false;
        if (isStartupCheck && settings.ApplyAtLogin) return true;
        return TimeOnly.FromDateTime(now.LocalDateTime) >= settings.GetDailyTime();
    }

    /// <summary>How long the scheduler can sleep before the next check (1s..MaxWait).</summary>
    public static TimeSpan NextCheckIn(OrielSettings settings, OrielState state, DateTimeOffset now)
    {
        if (settings.Paused) return MaxWait;
        var local = now.LocalDateTime;
        var dailyAt = local.Date + settings.GetDailyTime().ToTimeSpan();
        if (state.LastAppliedDate == DateOnly.FromDateTime(local) || local >= dailyAt) dailyAt = dailyAt.AddDays(1);

        var next = dailyAt - local;
        if (state.NextRetryAt is { } retry && state.LastAppliedDate != DateOnly.FromDateTime(local))
            next = retry - now;
        return next < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : next > MaxWait ? MaxWait : next;
    }
}

/// <summary>
/// In-app scheduler. Sleeps until the next due time (at most <see cref="ScheduleRules.MaxWait"/>, which
/// copes with sleep/resume and clock changes) and wakes early when settings change. Idle in between.
/// </summary>
public sealed class DailyScheduler(WallpaperService wallpapers, RetentionService retention, ISettingsStore store,
    IClock clock, ILogger<DailyScheduler> log) : IDisposable
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
                wait = ScheduleRules.NextCheckIn(store.Load(), store.LoadState(), clock.Now);
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

        if (!ScheduleRules.IsDue(store.Load(), store.LoadState(), clock.Now, isStartup)) return;

        log.LogInformation("Scheduled change is due (startup={Startup})", isStartup);
        var result = await wallpapers.ApplyAsync(force: false, ct);
        Attempted?.Invoke(this, result);
    }

    public void Dispose()
    {
        store.SettingsChanged -= OnSettingsChanged;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
