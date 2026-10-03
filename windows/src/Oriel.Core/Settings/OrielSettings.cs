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

    /// <summary>Keywords; a candidate whose description mentions one is skipped.</summary>
    public List<string> ExcludedKeywords { get; set; } = [];

    /// <summary>Local time of day for the daily change, "HH:mm".</summary>
    public string DailyTime { get; set; } = "09:00";
    public bool ApplyAtLogin { get; set; } = true;
    public bool Paused { get; set; }

    public MultiMonitorMode MonitorMode { get; set; } = MultiMonitorMode.SameOnAll;
    public WallpaperFit Fit { get; set; } = WallpaperFit.Fill;

    public bool SkipSmallerThanScreen { get; set; } = true;
    public bool SkipLowLikes { get; set; }
    public int MinLikes { get; set; } = 50;

    public int RetentionDays { get; set; } = 30;
    /// <summary>Where photos are saved; null means the default (Pictures\Oriel).</summary>
    public string? WallpaperFolder { get; set; }

    public bool StartWithWindows { get; set; } = true;
    public bool UseTaskSchedulerFallback { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;

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
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
}
