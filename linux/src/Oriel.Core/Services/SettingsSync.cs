using System.Text.Json;
using Oriel.Core.Auth;
using Oriel.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

/// <summary>The settings every device of an account shares (users/{uid}/sync/settings). Device-only ones stay local.</summary>
public sealed record SyncedSettings(
    string DailyTime, string TimeZone, int IntervalMinutes, bool Paused, bool FavoritesOnly, bool SkipLowLikes, int MinLikes,
    IReadOnlyList<SyncedInterest> Interests, bool MatchTimeOfDay = true)
{
    public static SyncedSettings From(OrielSettings s) => new(
        s.DailyTime, s.TimeZone, s.IntervalMinutes, s.Paused, s.FavoritesOnly, s.SkipLowLikes, s.MinLikes,
        [.. s.Topics.Where(t => !string.IsNullOrWhiteSpace(t.Query)).Select(t => new SyncedInterest(t.Query.Trim(), t.Enabled, t.IsCustom))],
        s.MatchTimeOfDay);

    /// <summary>Puts these into <paramref name="s"/>, keeping what's only this device's (your own interests' cover photos).</summary>
    public void ApplyTo(OrielSettings s)
    {
        s.DailyTime = DailyTime;
        s.TimeZone = TimeZone;
        s.IntervalMinutes = IntervalMinutes;
        s.Paused = Paused;
        s.FavoritesOnly = FavoritesOnly;
        s.SkipLowLikes = SkipLowLikes;
        s.MinLikes = MinLikes;
        s.MatchTimeOfDay = MatchTimeOfDay;
        var covers = s.Topics.Where(t => t.CoverUrl is not null)
            .GroupBy(t => t.Query, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().CoverUrl, StringComparer.OrdinalIgnoreCase);
        s.Topics = [.. Interests.Select(i => new TopicSetting
        {
            Query = i.Query, Enabled = i.Enabled, IsCustom = i.IsCustom, CoverUrl = covers.GetValueOrDefault(i.Query),
        })];
        s.UpdateBuiltInTopics();
    }

    /// <summary>The schedule part: changing it starts the schedule fresh (no catch-up of a moment that just moved).</summary>
    public bool SameSchedule(SyncedSettings other) =>
        DailyTime == other.DailyTime && TimeZone == other.TimeZone && IntervalMinutes == other.IntervalMinutes && Paused == other.Paused;

    public string Fingerprint() => JsonSerializer.Serialize(this with { Interests = [.. Interests] });
}

public sealed record SyncedInterest(string Query, bool Enabled, bool IsCustom);

/// <summary>
/// Keeps the shared settings the same on every device: a change here is written to <c>sync/settings</c> and rings the
/// other devices (<see cref="PulseListener"/>); a ring from another device, or opening the app, brings theirs here.
/// Last write wins. A device signing in for the first time adopts the account's settings, or uploads its own when the
/// account has none yet.
/// </summary>
public sealed class SettingsSync(AuthService auth, FirebaseClient firebase, ISettingsStore store, ILogger<SettingsSync> log)
{
    readonly SemaphoreSlim _gate = new(1, 1);
    SyncedSettings? _last;
    CancellationTokenSource? _debounce;

    public void Start()
    {
        _last = SyncedSettings.From(store.Load());
        store.SettingsChanged += OnLocalChange;
        auth.Changed += (_, _) => { if (auth.IsSignedIn) _ = PullAsync(); };
        _ = PullAsync();
    }

    /// <summary>A setting changed here (or a pull applied one): a schedule edit starts fresh, and a real change is uploaded.</summary>
    void OnLocalChange(object? sender, EventArgs e)
    {
        var now = SyncedSettings.From(store.Load());
        var before = _last;
        _last = now;
        if (before is not null && !before.SameSchedule(now))
        {
            var state = store.LoadState();
            state.ScheduleStartedAt = DateTimeOffset.Now;
            store.SaveState(state);
        }
        if (now.Fingerprint() == store.LoadState().SyncedSettingsHash) return; // came from the account: nothing to send
        // A slider or several toggles in a row: one upload when it settles.
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        _ = Task.Delay(TimeSpan.FromSeconds(1.2), cts.Token).ContinueWith(_ => PushAsync(), cts.Token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    /// <summary>Uploads this device's shared settings and rings the others.</summary>
    public async Task PushAsync(CancellationToken ct = default)
    {
        if (!auth.IsSignedIn) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (await auth.GetTokensAsync(ct) is not { } tokens) return;
            var mine = SyncedSettings.From(store.Load());
            var device = auth.Current?.SessionId ?? "";
            var at = DateTimeOffset.UtcNow;
            await firebase.SetSyncedSettingsAsync(tokens, mine, device, at, ct);
            var state = store.LoadState();
            state.SyncedSettingsHash = mine.Fingerprint();
            state.SettingsSyncedAt = at;
            state.SettingsDirty = false;
            store.SaveState(state);
            log.LogInformation("Shared your settings with your other devices");
            try { await firebase.RingSettingsAsync(tokens, device, ct); }
            catch (Exception ex) { log.LogInformation("Couldn't ring the other devices about settings: {Reason}", ex.Message); }
        }
        catch (Exception ex) when (ex is FirebaseException or OperationCanceledException)
        {
            // Offline: remembered, and sent the next time this device talks to the account.
            var state = store.LoadState();
            state.SettingsDirty = true;
            store.SaveState(state);
            log.LogInformation("Couldn't share your settings yet: {Reason}", ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Brings the account's settings here when they're newer (another device changed them).</summary>
    public async Task PullAsync(CancellationToken ct = default)
    {
        if (!auth.IsSignedIn) return;
        bool push;
        await _gate.WaitAsync(ct);
        try
        {
            push = await PullCoreAsync(ct);
        }
        catch (FirebaseException ex)
        {
            log.LogInformation("Couldn't read the shared settings: {Reason}", ex.Message);
            push = false;
        }
        finally
        {
            _gate.Release();
        }
        if (push) await PushAsync(ct);
    }

    /// <summary>Takes the account's settings when they're newer. True when this device's should be uploaded instead.</summary>
    async Task<bool> PullCoreAsync(CancellationToken ct)
    {
        if (await auth.GetTokensAsync(ct) is not { } tokens) return false;
        var remote = await firebase.GetSyncedSettingsAsync(tokens, ct);
        if (remote is null) return true; // the first device: the account takes this one's settings
        var (theirs, updatedAt) = remote.Value;
        var state = store.LoadState();
        var settings = store.Load();
        var mine = SyncedSettings.From(settings);
        if (theirs.Fingerprint() == mine.Fingerprint())
        {
            state.SyncedSettingsHash = mine.Fingerprint();
            state.SettingsSyncedAt = updatedAt;
            state.SettingsDirty = false;
            store.SaveState(state);
            return false;
        }
        // Changed here while offline, after the account's last change: this device's is the newer one.
        if (state.SettingsDirty && state.SettingsSyncedAt is { } synced && synced >= updatedAt) return true;
        state.SyncedSettingsHash = theirs.Fingerprint();
        state.SettingsSyncedAt = updatedAt;
        state.SettingsDirty = false;
        store.SaveState(state);
        theirs.ApplyTo(settings);
        store.Save(settings); // OnLocalChange: a schedule change starts fresh, and nothing is sent back
        log.LogInformation("Took the settings changed on another device");
        return false;
    }
}

/// <summary>
/// "First device wins" for scheduled changes: every device ticks at the same moments, and the first to claim a moment
/// (users/{uid}/sync/slot) picks the photo; the others show that one (it arrives through sync/current).
/// </summary>
public interface ISlotClaimer
{
    /// <summary>True: this device takes the moment. False: another device already did. Null: unknown (offline), go ahead.</summary>
    Task<bool?> TryClaimAsync(string slotId, CancellationToken ct = default);
}

public sealed class SlotClaimer(AuthService auth, FirebaseClient firebase, ILogger<SlotClaimer> log) : ISlotClaimer
{
    public async Task<bool?> TryClaimAsync(string slotId, CancellationToken ct = default)
    {
        try
        {
            if (!auth.IsSignedIn || await auth.GetTokensAsync(ct) is not { } tokens) return null;
            var won = await firebase.ClaimSlotAsync(tokens, slotId, auth.Current?.SessionId ?? "", ct);
            log.LogInformation(won ? "This device takes the {Slot} change" : "Another device already took the {Slot} change", slotId);
            return won;
        }
        catch (FirebaseException ex)
        {
            log.LogInformation("Couldn't claim the {Slot} change ({Reason}); changing anyway", slotId, ex.Message);
            return null;
        }
    }
}
