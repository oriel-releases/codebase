using Oriel.Core.Models;

namespace Oriel.Core.Settings;

public sealed class TopicSetting
{
    public string Query { get; set; } = "";
    public bool Enabled { get; set; }
    public bool IsCustom { get; set; }
    /// <summary>Your own interests: the photo its tile shows, found once and kept (Unsplash thumbnail link).</summary>
    public string? CoverUrl { get; set; }
}

public sealed class OrielSettings
{
    /// <summary>
    /// The interests every install offers (each has a photo for its tile in InterestCovers.cs). Ones that make good
    /// wallpapers; "minimal", "dark" and "abstract" were dropped (vague, and dark photos come with Match time of day).
    /// </summary>
    public static readonly string[] BuiltInTopics =
    [
        "nature", "mountains", "ocean", "beach", "lakes", "waterfalls", "forest", "autumn",
        "flowers", "desert", "snow", "northern lights", "night sky", "space", "sunset", "cities",
        "city at night", "architecture", "japan", "islands", "wildlife", "countryside", "aerial", "fog",
    ];

    static readonly HashSet<string> DefaultEnabled = ["nature", "mountains", "ocean", "space", "architecture"];

    public List<TopicSetting> Topics { get; set; } = BuiltInTopics
        .Select(t => new TopicSetting { Query = t, Enabled = DefaultEnabled.Contains(t) })
        .ToList();

    /// <summary>Time of day for the daily change, "HH:mm", in <see cref="TimeZone"/>.</summary>
    public string DailyTime { get; set; } = "09:00";
    /// <summary>
    /// The IANA time zone the schedule is anchored in (like "Asia/Tashkent"), shared by every device of the account so
    /// they all change at the same moment. Starts as this computer's own.
    /// </summary>
    public string TimeZone { get; set; } = LocalTimeZoneId();

    public static string LocalTimeZoneId() =>
        TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : TimeZoneInfo.Local.Id;

    public TimeZoneInfo Zone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return TimeZoneInfo.Local; }
    }
    /// <summary>Slideshow: minutes between changes. 0 means once a day at <see cref="DailyTime"/>.</summary>
    public int IntervalMinutes { get; set; }
    /// <summary>Pick from the photos starred in History instead of fetching new ones.</summary>
    public bool FavoritesOnly { get; set; }
    public bool ApplyAtLogin { get; set; } = true;
    public bool Paused { get; set; }

    public WallpaperFit Fit { get; set; } = WallpaperFit.Fill;

    public bool SkipLowLikes { get; set; }
    public int MinLikes { get; set; } = 50;

    /// <summary>Where photos are saved; null means the default (Pictures\Oriel).</summary>
    public string? WallpaperFolder { get; set; }

    public bool StartWithWindows { get; set; } = true;
    /// <summary>Also use each new wallpaper as the lock screen picture.</summary>
    public bool ChangeLockScreen { get; set; } = true;

    /// <summary>Make the Windows accent color follow each new wallpaper (Oriel picks the color from the photo).</summary>
    public bool MatchAccentColor { get; set; }

    /// <summary>Prefer darker photos in the evening (and in dark mode), brighter ones in the morning.</summary>
    public bool MatchTimeOfDay { get; set; } = true;

    /// <summary>With more than one monitor, give each its own photo instead of the same one everywhere.</summary>
    public bool PhotoPerMonitor { get; set; }

    /// <summary>Following a friend: their account and first name. While set, the schedule is off and the friend's wallpaper is used.</summary>
    public string? FollowUid { get; set; }
    public string? FollowName { get; set; }
    /// <summary>The followed friend's Google picture (shown instead of their initial).</summary>
    public string? FollowPicture { get; set; }

    /// <summary>This device's code for friends to follow (null while not sharing).</summary>
    public string? MyFollowCode { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFollowing => !string.IsNullOrEmpty(FollowUid);

    /// <summary>The first-run tour was finished or skipped.</summary>
    public bool OnboardingDone { get; set; }

    /// <summary>Global shortcut for "next wallpaper", like "Ctrl+Alt+W". Empty means none.</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+W";

    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan? Interval => IntervalMinutes > 0 ? TimeSpan.FromMinutes(IntervalMinutes) : null;

    public TimeOnly GetDailyTime() =>
        TimeOnly.TryParse(DailyTime, out var t) ? t : new TimeOnly(9, 0);

    /// <summary>
    /// Brings a saved list up to date with <see cref="BuiltInTopics"/>: new ones are added (off), ones no longer built in
    /// go, except that one you had on stays, as one of your own. Your own and your on/off choices are kept.
    /// </summary>
    public void UpdateBuiltInTopics()
    {
        var builtIn = new HashSet<string>(BuiltInTopics, StringComparer.OrdinalIgnoreCase);
        var saved = Topics.Where(t => !string.IsNullOrWhiteSpace(t.Query)).ToList();
        var kept = new List<TopicSetting>();
        foreach (var name in BuiltInTopics)
        {
            var mine = saved.FirstOrDefault(t => !t.IsCustom && t.Query.Equals(name, StringComparison.OrdinalIgnoreCase));
            kept.Add(new TopicSetting { Query = name, Enabled = mine?.Enabled ?? false });
        }
        foreach (var t in saved)
        {
            if (t.IsCustom) kept.Add(t);
            else if (!builtIn.Contains(t.Query) && t.Enabled) kept.Add(new TopicSetting { Query = t.Query, Enabled = true, IsCustom = true });
        }
        // A brand-new install (nothing saved yet) keeps its defaults.
        if (saved.Count > 0) Topics = kept;
    }

    public IReadOnlyList<string> EnabledQueries() =>
        Topics.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Query))
              .Select(t => t.Query.Trim())
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .ToList();
}

/// <summary>Runtime state that is not user-editable.</summary>
public sealed class OrielState
{
    public DateOnly? LastAppliedDate { get; set; }
    public DateTimeOffset? LastAppliedAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    /// <summary>When "Change now" last succeeded, for its per-hour limit. Only the last hour matters.</summary>
    public List<DateTimeOffset> ManualChanges { get; set; } = [];

    /// <summary>
    /// When the schedule last started fresh: the schedule was edited (here or on another device). Ticks before it
    /// aren't "missed", so an edit never changes the wallpaper by itself.
    /// </summary>
    public DateTimeOffset? ScheduleStartedAt { get; set; }
    /// <summary>The shared settings as last sent or received (to tell a real change from an echo), and when.</summary>
    public string? SyncedSettingsHash { get; set; }
    public DateTimeOffset? SettingsSyncedAt { get; set; }
    /// <summary>Changed here but not sent yet (offline).</summary>
    public bool SettingsDirty { get; set; }
    /// <summary>The newest favorite change taken from the account (the next sync asks only for later ones).</summary>
    public DateTimeOffset? FavoritesSyncedAt { get; set; }

    /// <summary>The friend's photo already applied, so it isn't set again.</summary>
    public string? FollowedPhotoId { get; set; }
    /// <summary>Why following doesn't work right now (the friend stopped sharing), for the UI.</summary>
    public string? FollowProblem { get; set; }

    /// <summary>
    /// The accent color chosen for a photo (on Home here, or on another device of the account), used instead of the one
    /// Oriel picks itself. Only the photo on screen matters, so one is kept.
    /// </summary>
    public string? AccentPhotoId { get; set; }
    public string? AccentColor { get; set; }
}
