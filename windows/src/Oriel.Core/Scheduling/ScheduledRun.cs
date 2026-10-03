using Oriel.Core.Services;
using Oriel.Core.Settings;

namespace Oriel.Core.Scheduling;

/// <summary>What one Task Scheduler launch does: change today's wallpaper if needed, tidy up, line up a retry on failure.</summary>
public sealed class ScheduledRun(WallpaperService wallpapers, RetentionService retention,
    IScheduledTasks tasks, ISettingsStore store)
{
    public async Task<ApplyResult> RunAsync(bool force, string exePath, CancellationToken ct = default)
    {
        var result = await wallpapers.ApplyAsync(force, ct);
        if (result.Status == ApplyStatus.Applied)
            retention.Cleanup();
        else if (result.IsTransientFailure && store.LoadState().NextRetryAt is { } at)
            tasks.ScheduleRetry(exePath, at);
        return result;
    }
}
