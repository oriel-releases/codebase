using Oriel.Core.Models;

namespace Oriel.Core.Settings;

public sealed class TopicSetting
{
    public string Query { get; set; } = "";
    public bool Enabled { get; set; }
    public bool IsCustom { get; set; }
}

public sealed class OrielSettings
{
    public static readonly string[] BuiltInTopics =
    [
        "nature", "mountains", "ocean", "space", "cities", "architecture",
        "minimal", "dark", "abstract", "forest", "desert", "aerial", "night sky", "snow",
    ];

    static readonly HashSet<string> DefaultEnabled = ["nature", "mountains", "ocean", "space", "architecture"];

    public List<TopicSetting> Topics { get; set; } = BuiltInTopics
        .Select(t => new TopicSetting { Query = t, Enabled = DefaultEnabled.Contains(t) })
        .ToList();

    /// <summary>Local time of day for the daily change, "HH:mm".</summary>
    public string DailyTime { get; set; } = "09:00";
    /// <summary>Slideshow: minutes between changes. 0 means once a day at <see cref="DailyTime"/>.</summary>
    public int IntervalMinutes { get; set; }
    /// <summary>Pick from the photos starred in History instead of fetching new ones.</summary>
    public bool FavoritesOnly { get; set; }
    public bool ApplyAtLogin { get; set; } = true;
    public bool Paused { get; set; }

    public WallpaperFit Fit { get; set; } = WallpaperFit.Fill;

    public bool SkipLowLikes { get; set; }
    public int MinLikes { get; set; } = 50;

    /// <summary>Where photos are saved; null means the default (Pictures/Oriel).</summary>
    public string? WallpaperFolder { get; set; }

    /// <summary>Open Oriel (minimized to the tray) when you log in.</summary>
    public bool StartAtLogin { get; set; } = true;
    /// <summary>Also use each new wallpaper as the lock screen picture, on desktops that allow it.</summary>
    public bool ChangeLockScreen { get; set; } = true;

    /// <summary>Prefer darker photos in the evening (and in dark mode), brighter ones in the morning.</summary>
    public bool MatchTimeOfDay { get; set; } = true;

    /// <summary>With more than one monitor, give each its own photo instead of the same one everywhere.</summary>
    public bool PhotoPerMonitor { get; set; }

    /// <summary>Following a friend: their account and first name. While set, the schedule is off and the friend's wallpaper is used.</summary>
    public string? FollowUid { get; set; }
    public string? FollowName { get; set; }

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

    /// <summary>The friend's photo already applied, so it isn't set again.</summary>
    public string? FollowedPhotoId { get; set; }
    /// <summary>Why following doesn't work right now (the friend stopped sharing), for the UI.</summary>
    public string? FollowProblem { get; set; }
}
