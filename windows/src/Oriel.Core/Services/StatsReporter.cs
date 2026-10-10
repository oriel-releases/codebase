using System.Text.Json;
using Oriel.Core.Auth;
using Oriel.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

/// <summary>Counts a moment worth knowing about (a share sent, opened or set). Best effort, never in the way.</summary>
public interface IStats
{
    void Count(string field);
}

/// <summary>The names of the counts. Each is a field in <c>stats/{day}</c>; firestore.rules lists the same names.</summary>
public static class StatFields
{
    public const string SharesSent = "sharesSent";
    public const string SharesOpened = "sharesOpened";
    public const string SharesSet = "sharesSet";
    public const string Retained7 = "retained7";
    public const string Retained30 = "retained30";
}

/// <summary>What this device has already counted, kept in its own small file.</summary>
public sealed class StatsMemory
{
    /// <summary>The first day this device was seen (for 7 and 30 day retention).</summary>
    public DateOnly? FirstDay { get; set; }
    /// <summary>The last day it was counted as active, so it is counted once a day at most.</summary>
    public DateOnly? ActiveDay { get; set; }
    public bool Retained7 { get; set; }
    public bool Retained30 { get; set; }

    /// <summary>
    /// The counts for being open today, and what to remember after they were sent. Pure, so it can be tested.
    /// <paramref name="installedOn"/> is set for an existing install seen for the first time (so it isn't a "new" one).
    /// </summary>
    public static (List<string> Fields, StatsMemory Next)? ForActiveDay(StatsMemory m, DateOnly today, string platform, DateOnly? installedOn)
    {
        if (m.ActiveDay == today) return null;
        var next = new StatsMemory { FirstDay = m.FirstDay, ActiveDay = today, Retained7 = m.Retained7, Retained30 = m.Retained30 };
        var fields = new List<string> { $"{platform}_active" };
        if (next.FirstDay is null)
        {
            if (installedOn is { } since && since < today) next.FirstDay = since;
            else
            {
                next.FirstDay = today;
                fields.Add($"{platform}_new");
            }
        }
        var age = today.DayNumber - next.FirstDay.Value.DayNumber;
        if (age >= 7 && !next.Retained7)
        {
            fields.Add(StatFields.Retained7);
            next.Retained7 = true;
        }
        if (age >= 30 && !next.Retained30)
        {
            fields.Add(StatFields.Retained30);
            next.Retained30 = true;
        }
        return (fields, next);
    }
}

/// <summary>
/// Anonymous daily counts, so it's possible to tell whether Oriel is growing and whether people keep it: new installs,
/// active devices, devices still around after 7 and 30 days, and shares sent, opened and set. One small document per
/// day (<c>stats/2026-10-10</c>) holding numbers only: no user IDs, no clicks, nothing about what anyone looks at.
/// </summary>
public sealed class StatsReporter(AuthService auth, FirebaseClient firebase, IHistoryStore history, AppPaths paths,
    ILogger<StatsReporter> log) : IStats
{
    const string Platform = "windows";
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    readonly SemaphoreSlim _gate = new(1, 1);

    public void Count(string field) => _ = SendAsync([field]);

    /// <summary>Counts this device as active today (once a day). Called by the scheduler, which runs while Oriel is open.</summary>
    public async Task MarkActiveAsync()
    {
        if (!auth.IsSignedIn || !await _gate.WaitAsync(0)) return;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var memory = Load();
            DateOnly? installedOn = null;
            if (memory.FirstDay is null && history.List(includeDeleted: true).MinBy(e => e.AppliedAt) is { } first)
                installedOn = DateOnly.FromDateTime(first.AppliedAt.UtcDateTime);
            if (StatsMemory.ForActiveDay(memory, today, Platform, installedOn) is not { } step) return;
            if (await SendAsync(step.Fields)) Save(step.Next);
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<bool> SendAsync(IReadOnlyCollection<string> fields)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            var tokens = await auth.GetTokensAsync(timeout.Token);
            if (tokens is null) return false;
            await firebase.IncrementStatsAsync(tokens, DateOnly.FromDateTime(DateTime.UtcNow), fields, timeout.Token);
            return true;
        }
        catch (Exception ex)
        {
            log.LogDebug("Couldn't send the daily counts: {Reason}", ex.Message);
            return false;
        }
    }

    StatsMemory Load()
    {
        try
        {
            return File.Exists(paths.StatsFile) ? JsonSerializer.Deserialize<StatsMemory>(File.ReadAllText(paths.StatsFile)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new();
        }
    }

    void Save(StatsMemory memory)
    {
        try { File.WriteAllText(paths.StatsFile, JsonSerializer.Serialize(memory)); }
        catch (IOException ex) { log.LogDebug(ex, "Couldn't remember the daily counts"); }
    }
}
