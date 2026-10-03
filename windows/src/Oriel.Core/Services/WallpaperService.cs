using Oriel.Core.Models;
using Oriel.Core.Providers;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Oriel.Core.Wallpaper;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

public enum ApplyStatus { Applied, AlreadyAppliedToday, Paused, Failed, Busy }

public sealed record ApplyResult(ApplyStatus Status, IReadOnlyList<HistoryEntry> Entries, string? Error = null,
    ProviderErrorKind? ErrorKind = null)
{
    public bool IsTransientFailure => Status == ApplyStatus.Failed &&
        ErrorKind is null or ProviderErrorKind.Network or ProviderErrorKind.RateLimited or ProviderErrorKind.BadResponse;
}

public sealed class WallpaperService(
    IImageProvider provider,
    IWallpaperSetter setter,
    IHistoryStore history,
    ISettingsStore settingsStore,
    ITopicPicker topics,
    IClock clock,
    AppPaths paths,
    ILogger<WallpaperService> log)
{
    const int MaxQueriesPerTarget = 3;

    /// <summary>Raised after a new wallpaper is applied (from any trigger in this process).</summary>
    public event EventHandler<ApplyResult>? Applied;

    /// <param name="force">Change even if today's wallpaper was already set ("Change now").</param>
    public async Task<ApplyResult> ApplyAsync(bool force, CancellationToken ct = default)
    {
        var settings = settingsStore.Load();
        var state = settingsStore.LoadState();

        if (!force && settings.Paused) return new(ApplyStatus.Paused, []);
        if (!force && state.LastAppliedDate == clock.Today) return new(ApplyStatus.AlreadyAppliedToday, []);

        // Cross-process guard: the tray app and a Task Scheduler run can fire at the same moment.
        using var gate = ApplyLock.TryAcquire(paths.LockFile);
        if (gate is null) return new(ApplyStatus.Busy, [], "Another change is already in progress.");

        // Re-check under the lock; the other process may have just finished.
        state = settingsStore.LoadState();
        if (!force && state.LastAppliedDate == clock.Today) return new(ApplyStatus.AlreadyAppliedToday, []);

        state.LastAttemptAt = clock.Now;
        try
        {
            var entries = await ApplyCoreAsync(settings, ct);
            state.LastAppliedDate = clock.Today;
            state.LastError = null;
            state.ConsecutiveFailures = 0;
            state.NextRetryAt = null;
            settingsStore.SaveState(state);

            var result = new ApplyResult(ApplyStatus.Applied, entries);
            Applied?.Invoke(this, result);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var kind = (ex as ProviderException)?.Kind;
            var message = ex is ProviderException or WallpaperException ? ex.Message : $"Something went wrong: {ex.Message}";
            log.LogWarning(ex, "Wallpaper change failed ({Kind})", kind);

            state.LastError = message;
            state.ConsecutiveFailures++;
            state.NextRetryAt = clock.Now + RetryBackoff.Delay(state.ConsecutiveFailures);
            settingsStore.SaveState(state);
            return new(ApplyStatus.Failed, [], message, kind);
        }
    }

    async Task<IReadOnlyList<HistoryEntry>> ApplyCoreAsync(OrielSettings settings, CancellationToken ct)
    {
        if (!provider.IsConfigured)
            throw new ProviderException(ProviderErrorKind.MissingKey, "This build of Oriel has no Unsplash access key.");

        var monitors = setter.GetMonitors();
        var targets = PlanTargets(monitors, settings);
        var enabled = settings.EnabledQueries();

        // 1. Pick + download everything first. Only touch the desktop once all files are on disk,
        //    so a failure halfway never leaves a blank or half-changed desktop.
        var picked = new List<(Target Target, PhotoCandidate Photo, string Topic, string File)>();
        var usedTopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pickedIds = new HashSet<string>();

        foreach (var target in targets)
        {
            var (photo, topic) = await FindCandidateAsync(target.Size, enabled, usedTopics, pickedIds, settings, ct);
            usedTopics.Add(topic);
            pickedIds.Add(photo.Id);

            var bytes = await provider.DownloadAsync(photo, target.Size, ct);
            if (!LooksLikeJpeg(bytes))
                throw new WallpaperException("Downloaded file isn't a valid image; keeping your current wallpaper.");

            var file = await SaveAsync(photo, bytes, target.Index, paths.WallpaperFolder(settings), ct);
            picked.Add((target, photo, topic, file));
        }

        // 2. Apply.
        try
        {
            if (targets.Count == 1 && targets[0].MonitorPath is null)
            {
                setter.SetForAll(picked[0].File, settings.Fit);
                SetOtherDesktops(picked[0].File);
            }
            else
            {
                // Other virtual desktops hold one image each, so they get the primary monitor's.
                // Done first: it would overwrite the per-monitor images on the current desktop.
                var primary = monitors.FirstOrDefault(m => m.IsPrimary)?.DevicePath;
                SetOtherDesktops(picked.FirstOrDefault(p => p.Target.MonitorPath == primary).File ?? picked[0].File);
                foreach (var p in picked)
                    setter.SetForMonitor(p.Target.MonitorPath!, p.File, settings.Fit);
            }
        }
        catch (Exception ex) when (ex is not WallpaperException)
        {
            throw new WallpaperException($"Windows refused to set the wallpaper: {ex.Message}", ex);
        }

        // 3. Record + tell the provider.
        var now = clock.Now;
        var entries = new List<HistoryEntry>();
        foreach (var p in picked)
        {
            var entry = new HistoryEntry
            {
                Provider = p.Photo.Provider,
                PhotoId = p.Photo.Id,
                FilePath = p.File,
                ThumbnailUrl = p.Photo.ThumbnailUrl,
                PhotoPageUrl = p.Photo.PhotoPageUrl,
                PhotographerName = p.Photo.PhotographerName,
                PhotographerUrl = p.Photo.PhotographerUrl,
                Description = p.Photo.Description,
                Topic = p.Topic,
                MonitorPath = p.Target.MonitorPath,
                AppliedAt = now,
            };
            entries.Add(entry with { Id = history.Add(entry) });
            await provider.TrackUsageAsync(p.Photo, ct);
        }

        log.LogInformation("Applied {Count} wallpaper(s): {Ids}", entries.Count, string.Join(", ", entries.Select(e => e.PhotoId)));
        return entries;
    }

    async Task<(PhotoCandidate, string)> FindCandidateAsync(PixelSize size, IReadOnlyList<string> enabled,
        HashSet<string> usedTopics, HashSet<string> pickedIds, OrielSettings settings, CancellationToken ct)
    {
        var tried = new HashSet<string>(usedTopics, StringComparer.OrdinalIgnoreCase);
        ProviderException? lastNoResults = null;

        for (var attempt = 0; attempt < MaxQueriesPerTarget; attempt++)
        {
            var topic = topics.Pick(enabled, tried);
            tried.Add(topic);

            IReadOnlyList<PhotoCandidate> candidates;
            try
            {
                candidates = await provider.GetRandomAsync(new ImageQuery(topic, size), ct);
            }
            catch (ProviderException ex) when (ex.Kind == ProviderErrorKind.NoResults)
            {
                lastNoResults = ex;
                continue;
            }

            foreach (var c in candidates)
            {
                if (pickedIds.Contains(c.Id) || history.WasUsed(c.Provider, c.Id))
                {
                    log.LogDebug("Skip {Id}: already used", c.Id);
                    continue;
                }
                if (CandidateFilter.Reject(c, size, settings) is { } reason)
                {
                    log.LogDebug("Skip {Id}: {Reason}", c.Id, reason);
                    continue;
                }
                return (c, topic);
            }
        }

        throw lastNoResults ?? new ProviderException(ProviderErrorKind.NoResults,
            "Couldn't find a new photo that passes your filters. Try more topics or looser filters.");
    }

    /// <summary>Re-applies an image from history.</summary>
    public void Reapply(HistoryEntry entry)
    {
        if (!File.Exists(entry.FilePath))
            throw new WallpaperException("That image file is gone from disk.");
        var settings = settingsStore.Load();
        var monitors = setter.GetMonitors();
        if (entry.MonitorPath is not null && monitors.Any(m => m.DevicePath == entry.MonitorPath))
            setter.SetForMonitor(entry.MonitorPath, entry.FilePath, settings.Fit);
        else
        {
            setter.SetForAll(entry.FilePath, settings.Fit);
            SetOtherDesktops(entry.FilePath);
        }
    }

    void SetOtherDesktops(string file)
    {
        try
        {
            if (!setter.SetForAllDesktops(file))
                log.LogInformation("This Windows build can't set every virtual desktop; only the current one changed");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't set the wallpaper on other virtual desktops");
        }
    }

    internal sealed record Target(int Index, string? MonitorPath, PixelSize Size);

    internal static IReadOnlyList<Target> PlanTargets(IReadOnlyList<MonitorInfo> monitors, OrielSettings settings)
    {
        if (monitors.Count == 0)
            return [new Target(0, null, new PixelSize(1920, 1080))];

        if (settings.Fit == WallpaperFit.Span)
        {
            var left = monitors.Min(m => m.Left);
            var top = monitors.Min(m => m.Top);
            var right = monitors.Max(m => m.Left + m.Width);
            var bottom = monitors.Max(m => m.Top + m.Height);
            return [new Target(0, null, new PixelSize(right - left, bottom - top))];
        }

        if (settings.MonitorMode == MultiMonitorMode.PerMonitor && monitors.Count > 1)
            return monitors.Select((m, i) => new Target(i, m.DevicePath, new PixelSize(m.Width, m.Height))).ToList();

        // Same image everywhere: size for the biggest screen so nothing gets upscaled.
        var biggest = monitors.MaxBy(m => (long)m.Width * m.Height)!;
        return [new Target(0, null, new PixelSize(biggest.Width, biggest.Height))];
    }

    async Task<string> SaveAsync(PhotoCandidate photo, byte[] bytes, int index, string folder, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var suffix = index > 0 ? $"_{index}" : "";
        var name = $"{clock.Now:yyyy-MM-dd}_{photo.Provider}_{Sanitize(photo.Id)}{suffix}.jpg";
        var file = Path.Combine(folder, name);
        var tmp = file + ".part";
        await File.WriteAllBytesAsync(tmp, bytes, ct);
        File.Move(tmp, file, overwrite: true);
        return file;
    }

    static string Sanitize(string s) => string.Concat(s.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));

    internal static bool LooksLikeJpeg(byte[] bytes) =>
        bytes.Length > 1024 && bytes[0] == 0xFF && bytes[1] == 0xD8;
}

public sealed class WallpaperException(string message, Exception? inner = null) : Exception(message, inner);

public static class RetryBackoff
{
    static readonly TimeSpan[] Steps =
    [
        TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
    ];

    public static TimeSpan Delay(int consecutiveFailures) =>
        Steps[Math.Clamp(consecutiveFailures - 1, 0, Steps.Length - 1)];
}

/// <summary>Exclusive lock file shared between processes.</summary>
sealed class ApplyLock : IDisposable
{
    readonly FileStream _stream;
    ApplyLock(FileStream stream) => _stream = stream;

    public static ApplyLock? TryAcquire(string file)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            return new ApplyLock(new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.DeleteOnClose));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
