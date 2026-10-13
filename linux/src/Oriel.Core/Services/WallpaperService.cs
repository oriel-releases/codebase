using Oriel.Core.Auth;
using Oriel.Core.Models;
using Oriel.Core.Providers;
using Oriel.Core.Scheduling;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Oriel.Core.Wallpaper;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

/// <summary>What a change is doing right now, for progress UI.</summary>
/// <summary>What a change is doing right now, for the "Finding a photo… → Downloading… → Changing… → Syncing…" text.</summary>
public enum ApplyPhase
{
    Finding,
    Downloading,
    Changing,
    /// <summary>Sharing the new photo with the account's other devices.</summary>
    Syncing,
    /// <summary>Another device set a new photo; it is being fetched here.</summary>
    Receiving,
    /// <summary>Nothing in progress any more.</summary>
    Done,
}

/// <summary>The words shown for each <see cref="ApplyPhase"/>: on Home's button and in the change popup.</summary>
public static class PhaseText
{
    public static string For(ApplyPhase phase) => phase switch
    {
        ApplyPhase.Finding => "Finding a photo…",
        ApplyPhase.Downloading => "Downloading…",
        ApplyPhase.Changing => "Changing…",
        ApplyPhase.Syncing => "Syncing…",
        ApplyPhase.Receiving => "Receiving…",
        _ => "",
    };

    public static readonly string[] All =
        [.. Enum.GetValues<ApplyPhase>().Where(p => p != ApplyPhase.Done).Select(For)];
}

/// <summary>
/// Runs a change started from a button (Search, Wallpaper of the day, a shared photo) and reports its steps, the same
/// words as Home: "Downloading…", "Changing…", "Syncing…". It returns once the change is fully done, sharing with the
/// other devices included, so the button doesn't say "set" while it's still syncing.
/// </summary>
public static class PhaseWatch
{
    /// <param name="report">Called from any thread with the step's words, then null at the end: post it to the UI.</param>
    public static async Task<ApplyResult> RunAsync(WallpaperService wallpapers, Func<Task<ApplyResult>> change, Action<string?> report)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPhase(object? sender, ApplyPhase phase)
        {
            if (phase == ApplyPhase.Done) done.TrySetResult();
            else report(PhaseText.For(phase));
        }
        wallpapers.PhaseChanged += OnPhase;
        try
        {
            var result = await change();
            if (result.Status == ApplyStatus.Applied) await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            return result;
        }
        finally
        {
            wallpapers.PhaseChanged -= OnPhase;
            report(null);
        }
    }
}

public enum ApplyStatus { Applied, AlreadyAppliedToday, Paused, Failed, Busy, SignInRequired, RateLimited, TakenByOtherDevice }

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
    IAuthState? auth = null,
    ISyncPublisher? sync = null,
    ISystemTheme? theme = null,
    ISlotClaimer? slots = null)
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
        try { return await ApplyNowAsync(force, ct); }
        finally { FinishPhase(); }
    }

    async Task<ApplyResult> ApplyNowAsync(bool force, CancellationToken ct)
    {
        if (auth is { IsSignedIn: false }) return SignInRequired;

        var settings = settingsStore.Load();
        var state = settingsStore.LoadState();

        if (!force && settings.Paused) return new(ApplyStatus.Paused, []);
        // Following a friend: their wallpaper is used instead of the schedule (see FollowService).
        if (!force && settings.IsFollowing) return new(ApplyStatus.Paused, []);
        if (!force && ScheduleRules.IsFresh(settings, state, clock.Now)) return new(ApplyStatus.AlreadyAppliedToday, []);

        // Cross-process guard: the tray app and a the systemd timer run can fire at the same moment.
        using var gate = ApplyLock.TryAcquire(paths.LockFile);
        if (gate is null) return new(ApplyStatus.Busy, [], "Another change is already in progress.");

        // Re-check under the lock; the other process may have just finished.
        state = settingsStore.LoadState();
        if (!force && ScheduleRules.IsFresh(settings, state, clock.Now)) return new(ApplyStatus.AlreadyAppliedToday, []);

        // "First device wins": every device ticks at the same moment, and only the one that claims it picks a photo.
        if (!force && slots is not null)
        {
            var slot = ScheduleRules.SlotId(settings, ScheduleRules.DueTick(settings, clock.Now));
            if (await slots.TryClaimAsync(slot, ct) == false)
            {
                // Done for this tick: the other device's photo comes through sync/current.
                state.ScheduleStartedAt = clock.Now;
                state.NextRetryAt = null;
                settingsStore.SaveState(state);
                return new(ApplyStatus.TakenByOtherDevice, []);
            }
        }

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

            // Let the account's other devices (the browser extension) show the same photo. Never fails the change.
            if (entries.Count > 0) await PublishSoon(entries[0]);

            var result = new ApplyResult(ApplyStatus.Applied, entries);
            Applied?.Invoke(this, result);
            SchedulePrefetch();
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

        var size = PlanTargets(setter.GetMonitors(), settingsStore.Load(), perMonitor: false)[0].Size;
        var found = await provider.SearchAsync(query, page, ct);
        var fits = found.Photos.Where(c => c.IsLandscape && c.Width >= size.Width && c.Height >= size.Height).ToList();
        return found with { Photos = fits };
    }

    /// <summary>
    /// A photo for the tile of one of your own interests: the top search result's thumbnail. One request, made once
    /// per interest (the link is kept with it), and only with requests to spare (null otherwise, to try later).
    /// </summary>
    public async Task<string?> FindCoverAsync(string query, CancellationToken ct = default)
    {
        if (!provider.IsConfigured || provider.RemainingRequests is < SearchReserve) return null;
        var found = await provider.SearchAsync(query, 1, ct);
        return (found.Photos.FirstOrDefault(p => p.IsLandscape) ?? found.Photos.FirstOrDefault())?.ThumbnailUrl;
    }

    /// <summary>A photo someone shared with a link, looked up by its Unsplash ID.</summary>
    public Task<PhotoCandidate> GetSharedPhotoAsync(string id, CancellationToken ct = default)
    {
        if (!provider.IsConfigured)
            throw new ProviderException(ProviderErrorKind.MissingKey, "This build of Oriel has no Unsplash access key.");
        return provider.GetPhotoAsync(id, ct);
    }

    /// <summary>
    /// Sets one specific photo (chosen by the user, or from another device) as the wallpaper. <paramref name="publish"/>
    /// shares it with the account's other devices; a photo that came from one of them isn't sent back.
    /// </summary>
    public async Task<ApplyResult> ApplyPhotoAsync(PhotoCandidate photo, string? topic = null, CancellationToken ct = default, bool publish = true)
    {
        try { return await ApplyPhotoNowAsync(photo, topic, publish, ct); }
        finally { FinishPhase(); }
    }

    async Task<ApplyResult> ApplyPhotoNowAsync(PhotoCandidate photo, string? topic, bool publish, CancellationToken ct)
    {
        if (auth is { IsSignedIn: false }) return SignInRequired;

        var settings = settingsStore.Load();
        using var gate = ApplyLock.TryAcquire(paths.LockFile);
        if (gate is null) return new(ApplyStatus.Busy, [], "Another change is already in progress.");

        try
        {
            var size = PlanTargets(setter.GetMonitors(), settings, perMonitor: false)[0].Size;
            PhaseChanged?.Invoke(this, ApplyPhase.Downloading);
            var bytes = await provider.DownloadAsync(photo, size, ct);
            if (!LooksLikeJpeg(bytes))
                throw new WallpaperException("Downloaded file isn't a valid image; keeping your current wallpaper.");
            var file = await SaveAsync(photo, bytes, 0, paths.WallpaperFolder(settings), ct);
            PhaseChanged?.Invoke(this, ApplyPhase.Changing);

            // Lock screen first: it is the slow one, and the desktop changing is the user's "done" signal.
            await SetLockScreenAsync(file, settings);
            try
            {
                setter.SetForAll(file, settings.Fit);
            }
            catch (Exception ex) when (ex is not WallpaperException)
            {
                throw new WallpaperException($"Your desktop refused the wallpaper: {ex.Message}", ex);
            }

            var entry = RecordEntry(photo, file, topic ?? "search", null, clock.Now);
            await AfterChange(() => provider.TrackUsageAsync(photo, CancellationToken.None));

            // Counts as today's change, so the scheduler doesn't replace it right away.
            var state = settingsStore.LoadState();
            state.LastAppliedDate = clock.Today;
            state.LastAppliedAt = clock.Now;
            state.LastError = null;
            state.ConsecutiveFailures = 0;
            state.NextRetryAt = null;
            settingsStore.SaveState(state);

            if (publish) await PublishSoon(entry);

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

        var mood = MoodNow(settings);
        foreach (var target in targets)
        {
            // A photo prepared ahead of time, when one still fits: no search, no download.
            if (targets.Count == 1 && TakePrefetch(target.Size, enabled, settings, mood) is { } ready)
            {
                picked.Add((target, ready.Photo, ready.Topic, ready.File));
                log.LogInformation("Using the photo prepared ahead of time: {Id}", ready.Photo.Id);
                continue;
            }
            var (photo, topic) = await FindCandidateAsync(target.Size, enabled, usedTopics, pickedIds, settings, mood, ct);
            usedTopics.Add(topic);
            pickedIds.Add(photo.Id);

            PhaseChanged?.Invoke(this, ApplyPhase.Downloading);
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
                setter.SetForAll(picked[0].File, settings.Fit);
            else
                foreach (var p in picked)
                    setter.SetForMonitor(p.Target.MonitorPath!, p.File, settings.Fit);
        }
        catch (Exception ex) when (ex is not WallpaperException)
        {
            throw new WallpaperException($"Your desktop refused the wallpaper: {ex.Message}", ex);
        }

        // 3. Record + tell the provider.
        var now = clock.Now;
        var entries = new List<HistoryEntry>();
        foreach (var p in picked)
        {
            entries.Add(RecordEntry(p.Photo, p.File, p.Topic, p.Target.MonitorPath, now));
            await AfterChange(() => provider.TrackUsageAsync(p.Photo, CancellationToken.None));
        }

        log.LogInformation("Applied {Count} wallpaper(s): {Ids}", entries.Count, string.Join(", ", entries.Select(e => e.PhotoId)));
        return entries;
    }

    async Task<(PhotoCandidate, string)> FindCandidateAsync(PixelSize size, IReadOnlyList<string> enabled,
        HashSet<string> usedTopics, HashSet<string> pickedIds, OrielSettings settings, Mood mood, CancellationToken ct)
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

            // Photos that suit the moment (dark in the evening, bright in the morning) come first.
            foreach (var c in PhotoMood.Order(candidates, mood))
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
            ? favorites.Where(e => !string.Equals(e.FilePath, current, StringComparison.Ordinal)).ToList()
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
        // A favorite from another device (or a file cleaned up since): fetched again, like a shared photo.
        if (!File.Exists(entry.FilePath) && entry.Provider == "unsplash")
        {
            var photo = await GetSharedPhotoAsync(entry.PhotoId);
            var result = await ApplyPhotoAsync(photo, entry.Topic ?? "favorite");
            if (result.Status != ApplyStatus.Applied) throw new WallpaperException(result.Error ?? "Couldn't set it. Try again.");
            return;
        }
        try { await ReapplyNowAsync(entry); }
        finally { FinishPhase(); }
    }

    async Task ReapplyNowAsync(HistoryEntry entry)
    {
        PhaseChanged?.Invoke(this, ApplyPhase.Changing);
        await ReapplyCoreAsync(entry);
        var touched = entry with { AppliedAt = clock.Now };
        history.Add(touched);
        // History is another way to change the wallpaper: share it with the account's other devices like the rest.
        await PublishSoon(touched);
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
            setter.SetForAll(entry.FilePath, settings.Fit);
    }

    async Task SetLockScreenAsync(string file, OrielSettings settings)
    {
        if (!settings.ChangeLockScreen || lockScreen is null) return;
        try
        {
            if (!await lockScreen.TrySetAsync(file))
                log.LogInformation("This desktop doesn't allow changing the lock screen picture");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't change the lock screen picture");
        }
    }

    // ---------------------------------------------------------------- the next wallpapers, ahead of time

    /// <summary>
    /// The app (a long-running process) turns this on. Photos for the next changes are then found and downloaded in the
    /// background, so a change (scheduled or "Change now") is instant and works offline, and network work that doesn't
    /// change the screen (syncing, Unsplash's download count) runs after the change instead of holding it up. Off for
    /// one-shot runs, which exit right away and so do everything before returning.
    /// </summary>
    public bool PrefetchEnabled { get; set; }

    /// <summary>How many photos wait ready: "Change now" works twice an hour, so both changes are instant.</summary>
    internal const int PrefetchDepth = MaxManualChangesPerHour;

    /// <summary>One filler at a time.</summary>
    readonly SemaphoreSlim _prefetchGate = new(1, 1);
    /// <summary>Guards the queue file and its photos only, so taking a photo never waits for a download.</summary>
    readonly Lock _queueLock = new();
    static readonly TimeSpan PrefetchMaxAge = TimeSpan.FromDays(3);

    sealed record Prefetched(PhotoCandidate Photo, string Topic, int Width, int Height, string File, DateTimeOffset At);

    string QueueFile => Path.Combine(paths.PrefetchDir, "queue.json");

    /// <summary>
    /// Tops up the photos prepared ahead of time (up to <see cref="PrefetchDepth"/>): finds and downloads them, without
    /// touching the desktop.
    /// </summary>
    public async Task PrefetchAsync(CancellationToken ct = default)
    {
        if (!PrefetchEnabled || auth is { IsSignedIn: false } || !await _prefetchGate.WaitAsync(0, ct)) return;
        try
        {
            var settings = settingsStore.Load();
            if (settings.FavoritesOnly || settings.IsFollowing || !provider.IsConfigured) return;
            var targets = PlanTargets(setter.GetMonitors(), settings);
            if (targets.Count != 1) return;
            var size = targets[0].Size;
            lock (_queueLock) ForgetStrayFiles();

            while (true)
            {
                List<Prefetched> queue;
                lock (_queueLock) queue = ReadQueue();
                if (queue.Count >= PrefetchDepth) return;
                // Only with requests to spare: never at the cost of the scheduled change or a search.
                if (provider.RemainingRequests is < SearchReserve * 2) return;

                // The first suits right now (the next "Change now"), the second the next scheduled change.
                var at = queue.Count == 0 ? clock.Now : NextChangeAt(settings);
                var mood = settings.MatchTimeOfDay ? PhotoMood.For(at.LocalDateTime, theme?.IsDark ?? false) : Mood.Any;
                // Different photos, from different topics when there are enough, so two quick changes don't look alike.
                var (photo, topic) = await FindCandidateAsync(size, settings.EnabledQueries(),
                    queue.Select(q => q.Topic).ToHashSet(StringComparer.OrdinalIgnoreCase), queue.Select(q => q.Photo.Id).ToHashSet(),
                    settings, mood, ct);
                var bytes = await provider.DownloadAsync(photo, size, ct);
                if (!LooksLikeJpeg(bytes)) return;

                Directory.CreateDirectory(paths.PrefetchDir);
                var file = Path.Combine(paths.PrefetchDir, $"{Sanitize(photo.Id)}.jpg");
                await File.WriteAllBytesAsync(file + ".part", bytes, ct);
                File.Move(file + ".part", file, overwrite: true);
                // Only listed once it is whole on disk, so whatever is in the queue can be used right away.
                lock (_queueLock)
                {
                    var now = ReadQueue();
                    now.Add(new Prefetched(photo, topic, size.Width, size.Height, file, clock.Now));
                    WriteQueue(now);
                }
                log.LogInformation("Prepared a wallpaper ahead of time: {Id} ({Count} ready)", photo.Id, queue.Count + 1);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogInformation("Couldn't prepare the next wallpaper: {Reason}", ex.Message);
        }
        finally
        {
            _prefetchGate.Release();
        }
    }

    void SchedulePrefetch()
    {
        if (!PrefetchEnabled) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10)); // the change itself (and what follows it) comes first
            await PrefetchAsync();
        });
    }

    /// <summary>
    /// A prepared photo that still fits (same screen size, a topic that is still on, the filters, not used since),
    /// moved into the wallpaper folder. Time of day is only a preference: one that suits the moment comes first, else
    /// any. Photos that no longer fit at all are thrown away.
    /// </summary>
    Prefetched? TakePrefetch(PixelSize size, IReadOnlyList<string> enabled, OrielSettings settings, Mood mood)
    {
        lock (_queueLock)
        {
            try
            {
                var queue = ReadQueue();
                if (queue.Count == 0) return null;
                var usable = new List<Prefetched>();
                foreach (var ready in queue)
                {
                    var fits = File.Exists(ready.File)
                        && ready.Width == size.Width && ready.Height == size.Height
                        && clock.Now - ready.At < PrefetchMaxAge
                        && (enabled.Count == 0 ? ready.Topic == RandomTopicPicker.Fallback : enabled.Contains(ready.Topic, StringComparer.OrdinalIgnoreCase))
                        && CandidateFilter.Reject(ready.Photo, size, settings) is null
                        && !history.WasUsed(ready.Photo.Provider, ready.Photo.Id);
                    if (fits) usable.Add(ready);
                    else TryDelete(ready.File);
                }
                var pick = usable.FirstOrDefault(r => PhotoMood.Fits(r.Photo, mood)) ?? usable.FirstOrDefault();
                if (pick is not null) usable.Remove(pick);
                WriteQueue(usable);
                return pick is null ? null : pick with { File = KeepPrefetched(pick, settings) };
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    /// <summary>Moves the prepared file into the wallpaper folder, named like any other wallpaper.</summary>
    string KeepPrefetched(Prefetched ready, OrielSettings settings)
    {
        var folder = paths.WallpaperFolder(settings);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{clock.Now:yyyy-MM-dd}_{ready.Photo.Provider}_{Sanitize(ready.Photo.Id)}.jpg");
        File.Move(ready.File, file, overwrite: true);
        return file;
    }

    List<Prefetched> ReadQueue()
    {
        try
        {
            return File.Exists(QueueFile) ? JsonSerializer.Deserialize<List<Prefetched>>(File.ReadAllText(QueueFile)) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }

    void WriteQueue(List<Prefetched> queue)
    {
        Directory.CreateDirectory(paths.PrefetchDir);
        File.WriteAllText(QueueFile + ".part", JsonSerializer.Serialize(queue));
        File.Move(QueueFile + ".part", QueueFile, overwrite: true);
    }

    /// <summary>Photos no longer in the queue (half-written ones, or the single "next" photo of older versions).</summary>
    void ForgetStrayFiles()
    {
        if (!Directory.Exists(paths.PrefetchDir)) return;
        var listed = ReadQueue().Select(q => q.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(paths.PrefetchDir))
            if (file != QueueFile && !listed.Contains(file)) TryDelete(file);
    }

    static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---------------------------------------------------------------- after the change

    readonly Lock _afterLock = new();
    Task _after = Task.CompletedTask;

    /// <summary>
    /// Network work that doesn't change what's on screen. In the app it runs after the change, one at a time in order
    /// (so the account's devices see the changes in the order they happened), and never fails it. A one-shot run waits
    /// for it, as it is about to exit.
    /// </summary>
    Task AfterChange(Func<Task> work)
    {
        if (!PrefetchEnabled) return work();
        lock (_afterLock)
            _after = _after.ContinueWith(async _ =>
            {
                try { await work(); }
                catch (Exception ex) { log.LogInformation("Background work after a change failed: {Reason}", ex.Message); }
            }, TaskScheduler.Default).Unwrap();
        return Task.CompletedTask;
    }

    readonly Lock _syncLock = new();
    Task _syncs = Task.CompletedTask;
    int _publishing;

    /// <summary>
    /// Shares a new wallpaper with the account's other devices at once: its own queue (in order), not behind the lock
    /// screen and accent color, which take seconds. Shows "Syncing…" until it's done. Never fails the change.
    /// </summary>
    Task PublishSoon(HistoryEntry entry)
    {
        if (sync is null) return Task.CompletedTask;
        Interlocked.Increment(ref _publishing);
        PhaseChanged?.Invoke(this, ApplyPhase.Syncing);
        async Task Work()
        {
            try { await sync.PublishAsync(entry, CancellationToken.None); }
            catch (Exception ex) { log.LogInformation("Sharing the wallpaper failed: {Reason}", ex.Message); }
            finally
            {
                if (Interlocked.Decrement(ref _publishing) == 0) PhaseChanged?.Invoke(this, ApplyPhase.Done);
            }
        }
        if (!PrefetchEnabled) return Work();
        lock (_syncLock) _syncs = _syncs.ContinueWith(_ => Work(), TaskScheduler.Default).Unwrap();
        return Task.CompletedTask;
    }

    /// <summary>Shown by <see cref="FollowService"/> while it fetches a photo another device set.</summary>
    public void ReportPhase(ApplyPhase phase) => PhaseChanged?.Invoke(this, phase);

    /// <summary>A change ended: "Done", unless its photo is still being shared ("Syncing…" ends that one).</summary>
    public void FinishPhase()
    {
        if (Volatile.Read(ref _publishing) == 0) PhaseChanged?.Invoke(this, ApplyPhase.Done);
    }

    /// <summary>Done once everything queued by <see cref="AfterChange"/> so far has finished (for tests).</summary>
    internal Task AfterChangeDone
    {
        get { lock (_afterLock) return _after; }
    }

    /// <summary>Roughly when the next scheduled change happens, to pick a photo that suits that moment.</summary>
    DateTimeOffset NextChangeAt(OrielSettings settings)
    {
        var now = clock.Now;
        if (settings.Interval is { } interval) return now + interval;
        var local = now.LocalDateTime;
        var at = local.Date + settings.GetDailyTime().ToTimeSpan();
        if (at <= local) at = at.AddDays(1);
        return new DateTimeOffset(at);
    }

    Mood MoodNow(OrielSettings settings) =>
        settings.MatchTimeOfDay ? PhotoMood.For(clock.Now.LocalDateTime, theme?.IsDark ?? false) : Mood.Any;

    internal sealed record Target(int Index, string? MonitorPath, PixelSize Size);

    /// <param name="perMonitor">False for one photo chosen by the user: then it is sized for the biggest screen.</param>
    internal static IReadOnlyList<Target> PlanTargets(IReadOnlyList<MonitorInfo> monitors, OrielSettings settings, bool perMonitor = true)
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

        // A photo of its own on each monitor, each sized for that screen.
        if (perMonitor && settings.PhotoPerMonitor && monitors.Count > 1)
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
