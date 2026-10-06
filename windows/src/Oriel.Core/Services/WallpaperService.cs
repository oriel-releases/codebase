using Oriel.Core.Auth;
using Oriel.Core.Models;
using Oriel.Core.Providers;
using Oriel.Core.Scheduling;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Oriel.Core.Wallpaper;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

/// <summary>What a change is doing right now, for progress UI.</summary>
public enum ApplyPhase { Finding, Changing }

public enum ApplyStatus { Applied, AlreadyAppliedToday, Paused, Failed, Busy, SignInRequired, RateLimited }

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
    ILogger<WallpaperService> log,
    ILockScreenSetter? lockScreen = null,
    IAuthState? auth = null)
{
    const int MaxQueriesPerTarget = 3;

    /// <summary>"Change now" (button, tray, shortcut) is limited to this many successful changes per hour.</summary>
    const int MaxManualChangesPerHour = 2;
    static readonly TimeSpan ManualChangeWindow = TimeSpan.FromHours(1);

    /// <summary>Whole minutes left until <paramref name="at"/>, rounded up, at least 1 (so never "0 min").</summary>
    public static int MinutesUntil(DateTimeOffset at, DateTimeOffset now) =>
        Math.Max(1, (int)Math.Ceiling((at - now).TotalMinutes));

    /// <summary>When "Change now" works again, or null if it is available right now.</summary>
    public static DateTimeOffset? ManualChangeAvailableAt(OrielState state, DateTimeOffset now)
    {
        var recent = state.ManualChanges.Where(t => now - t < ManualChangeWindow).Order().ToList();
        return recent.Count >= MaxManualChangesPerHour ? recent[^MaxManualChangesPerHour] + ManualChangeWindow : null;
    }

    /// <summary>
    /// Requests kept back for the daily wallpaper: the Unsplash demo key allows 50 an hour, and a search
    /// page costs one each, so searching stops a little early rather than starving the scheduled change.
    /// </summary>
    const int SearchReserve = 10;

    static readonly ApplyResult SignInRequired = new(ApplyStatus.SignInRequired, [], "Sign in to Oriel first.");

    /// <summary>Raised after a new wallpaper is applied (from any trigger in this process).</summary>
    public event EventHandler<ApplyResult>? Applied;

    /// <summary>Raised from whatever thread is working: finding and downloading a photo, then setting it.</summary>
    public event EventHandler<ApplyPhase>? PhaseChanged;

    /// <param name="force">Change even if today's wallpaper was already set ("Change now").</param>
    public async Task<ApplyResult> ApplyAsync(bool force, CancellationToken ct = default)
    {
        if (auth is { IsSignedIn: false }) return SignInRequired;

        var settings = settingsStore.Load();
        var state = settingsStore.LoadState();

        if (!force && settings.Paused) return new(ApplyStatus.Paused, []);
        if (!force && ScheduleRules.IsFresh(settings, state, clock.Now)) return new(ApplyStatus.AlreadyAppliedToday, []);

        // Cross-process guard: the tray app and a Task Scheduler run can fire at the same moment.
        using var gate = ApplyLock.TryAcquire(paths.LockFile);
        if (gate is null) return new(ApplyStatus.Busy, [], "Another change is already in progress.");

        // Re-check under the lock; the other process may have just finished.
        state = settingsStore.LoadState();
        if (!force && ScheduleRules.IsFresh(settings, state, clock.Now)) return new(ApplyStatus.AlreadyAppliedToday, []);

        if (force)
        {
            state.ManualChanges.RemoveAll(t => clock.Now - t >= ManualChangeWindow);
            if (ManualChangeAvailableAt(state, clock.Now) is { } next)
                return new(ApplyStatus.RateLimited, [],
                    $"You can use Change now twice an hour. Try again in {MinutesUntil(next, clock.Now)} min.");
        }

        // The schedule belongs to the app and "Change now" to the user, so they stay apart: only an automatic
        // change moves the schedule (last applied, retries); a manual one only counts toward its own limit.
        if (!force) state.LastAttemptAt = clock.Now;
        try
        {
            var entries = await ApplyCoreAsync(settings, ct);
            if (force)
            {
                state.ManualChanges.Add(clock.Now);
            }
            else
            {
                state.LastAppliedDate = clock.Today;
                state.LastAppliedAt = clock.Now;
                state.LastError = null;
                state.ConsecutiveFailures = 0;
                state.NextRetryAt = null;
            }
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

            // A failed "Change now" is reported to the user right away; it must not delay the automatic change.
            if (!force)
            {
                state.LastError = message;
                state.ConsecutiveFailures++;
                state.NextRetryAt = clock.Now + RetryBackoff.Delay(state.ConsecutiveFailures);
                settingsStore.SaveState(state);
            }
            return new(ApplyStatus.Failed, [], message, kind);
        }
    }

    /// <summary>Photos for the words, most relevant first. Only ones big enough for the screen, so they look sharp.</summary>
    public async Task<SearchPage> SearchAsync(string query, int page, CancellationToken ct = default)
    {
        if (!provider.IsConfigured)
            throw new ProviderException(ProviderErrorKind.MissingKey, "This build of Oriel has no Unsplash access key.");
        if (provider.RemainingRequests is < SearchReserve)
            throw new ProviderException(ProviderErrorKind.RateLimited,
                "Searching is paused for a while so your daily wallpaper still gets its photos. Try again in the next hour.");

        var size = PlanTargets(setter.GetMonitors(), settingsStore.Load())[0].Size;
        var found = await provider.SearchAsync(query, page, ct);
        var fits = found.Photos.Where(c => c.IsLandscape && c.Width >= size.Width && c.Height >= size.Height).ToList();
        return found with { Photos = fits };
    }

    /// <summary>Sets one specific photo (chosen by the user) as the wallpaper.</summary>
    public async Task<ApplyResult> ApplyPhotoAsync(PhotoCandidate photo, string? topic = null, CancellationToken ct = default)
    {
        if (auth is { IsSignedIn: false }) return SignInRequired;

        var settings = settingsStore.Load();
        using var gate = ApplyLock.TryAcquire(paths.LockFile);
        if (gate is null) return new(ApplyStatus.Busy, [], "Another change is already in progress.");

        try
        {
            var size = PlanTargets(setter.GetMonitors(), settings)[0].Size;
            var bytes = await provider.DownloadAsync(photo, size, ct);
            if (!LooksLikeJpeg(bytes))
                throw new WallpaperException("Downloaded file isn't a valid image; keeping your current wallpaper.");
            var file = await SaveAsync(photo, bytes, 0, paths.WallpaperFolder(settings), ct);

            // Lock screen first: it is the slow one, and the desktop changing is the user's "done" signal.
            await SetLockScreenAsync(file, settings);
            try
            {
                setter.SetForAll(file, settings.Fit);
                SetOtherDesktops(file);
            }
            catch (Exception ex) when (ex is not WallpaperException)
            {
                throw new WallpaperException($"Windows refused to set the wallpaper: {ex.Message}", ex);
            }

            var entry = RecordEntry(photo, file, topic ?? "search", null, clock.Now);
            await provider.TrackUsageAsync(photo, ct);

            // Counts as today's change, so the scheduler doesn't replace it right away.
            var state = settingsStore.LoadState();
            state.LastAppliedDate = clock.Today;
            state.LastAppliedAt = clock.Now;
            state.LastError = null;
            state.ConsecutiveFailures = 0;
            state.NextRetryAt = null;
            settingsStore.SaveState(state);

            var result = new ApplyResult(ApplyStatus.Applied, [entry]);
            Applied?.Invoke(this, result);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var kind = (ex as ProviderException)?.Kind;
            var message = ex is ProviderException or WallpaperException ? ex.Message : $"Something went wrong: {ex.Message}";
            log.LogWarning(ex, "Setting the chosen wallpaper failed ({Kind})", kind);
            return new(ApplyStatus.Failed, [], message, kind);
        }
    }

    HistoryEntry RecordEntry(PhotoCandidate photo, string file, string topic, string? monitorPath, DateTimeOffset at)
    {
        var entry = new HistoryEntry
        {
            Provider = photo.Provider,
            PhotoId = photo.Id,
            FilePath = file,
            ThumbnailUrl = photo.ThumbnailUrl,
            PhotoPageUrl = photo.PhotoPageUrl,
            PhotographerName = photo.PhotographerName,
            PhotographerUrl = photo.PhotographerUrl,
            Description = photo.Description,
            Topic = topic,
            MonitorPath = monitorPath,
            AppliedAt = at,
        };
        return entry with { Id = history.Add(entry) };
    }

    async Task<IReadOnlyList<HistoryEntry>> ApplyCoreAsync(OrielSettings settings, CancellationToken ct)
    {
        PhaseChanged?.Invoke(this, ApplyPhase.Finding);
        if (settings.FavoritesOnly && await TryApplyFavoriteAsync() is { } favorite) return [favorite];

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

        PhaseChanged?.Invoke(this, ApplyPhase.Changing);
        // 2. Apply. Lock screen first: it is the slow one, and the desktop changing is the user's "done" signal.
        await SetLockScreenAsync(picked[0].File, settings);
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
            entries.Add(RecordEntry(p.Photo, p.File, p.Topic, p.Target.MonitorPath, now));
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

    /// <summary>
    /// A random favorite other than the one on screen, applied without any download. Null when there
    /// are no favorites, so the caller falls back to fetching a new photo.
    /// </summary>
    async Task<HistoryEntry?> TryApplyFavoriteAsync()
    {
        PhaseChanged?.Invoke(this, ApplyPhase.Changing);
        var favorites = history.List().Where(e => e.IsFavorite && File.Exists(e.FilePath)).ToList();
        if (favorites.Count == 0) return null;

        var current = setter.GetCurrent();
        var pool = favorites.Count > 1
            ? favorites.Where(e => !string.Equals(e.FilePath, current, StringComparison.OrdinalIgnoreCase)).ToList()
            : favorites;
        var pick = pool[Random.Shared.Next(pool.Count)];

        await ReapplyCoreAsync(pick);
        var entry = pick with { AppliedAt = clock.Now };
        history.Add(entry);
        log.LogInformation("Applied favorite {Id}", pick.PhotoId);
        return entry;
    }

    /// <summary>
    /// Re-applies an image from history. It becomes the newest entry, so Home shows it,
    /// and <see cref="Applied"/> is raised like for any other change.
    /// </summary>
    public async Task ReapplyAsync(HistoryEntry entry)
    {
        await ReapplyCoreAsync(entry);
        var touched = entry with { AppliedAt = clock.Now };
        history.Add(touched);
        Applied?.Invoke(this, new ApplyResult(ApplyStatus.Applied, [touched]));
    }

    async Task ReapplyCoreAsync(HistoryEntry entry)
    {
        if (!File.Exists(entry.FilePath))
            throw new WallpaperException("That image file is gone from disk.");
        var settings = settingsStore.Load();
        await SetLockScreenAsync(entry.FilePath, settings);
        var monitors = setter.GetMonitors();
        if (entry.MonitorPath is not null && monitors.Any(m => m.DevicePath == entry.MonitorPath))
            setter.SetForMonitor(entry.MonitorPath, entry.FilePath, settings.Fit);
        else
        {
            setter.SetForAll(entry.FilePath, settings.Fit);
            SetOtherDesktops(entry.FilePath);
        }
    }

    async Task SetLockScreenAsync(string file, OrielSettings settings)
    {
        if (!settings.ChangeLockScreen || lockScreen is null) return;
        try
        {
            if (!await lockScreen.TrySetAsync(file))
                log.LogInformation("Windows didn't allow changing the lock screen picture");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't change the lock screen picture");
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
