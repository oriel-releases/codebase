using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Oriel.Core.Wallpaper;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

/// <summary>Deletes old non-favorite images. Rows stay in the DB so photos are never reused.</summary>
public sealed class RetentionService(IHistoryStore history, ISettingsStore settings, IWallpaperSetter setter,
    IClock clock, ILogger<RetentionService> log)
{
    /// <summary>Old non-favorite photos are deleted after this many days (sooner for a slideshow, which makes many).</summary>
    const int KeepDays = 30;
    const int SlideshowKeepDays = 7;

    public int Cleanup()
    {
        var keepDays = settings.Load().IntervalMinutes > 0 ? SlideshowKeepDays : KeepDays;
        var inUse = CurrentWallpapers();
        var removed = 0;
        foreach (var e in history.ExpiredBefore(clock.Now.AddDays(-keepDays)))
        {
            if (inUse.Contains(e.FilePath)) continue; // never delete what's on screen
            if (TryDeleteFile(e.FilePath))
            {
                history.MarkDeleted(e.Id);
                removed++;
            }
        }
        if (removed > 0) log.LogInformation("Retention removed {Count} old image(s)", removed);
        return removed;
    }

    /// <summary>User-initiated delete from the History page.</summary>
    public void Delete(long id)
    {
        var e = history.Get(id);
        if (e is null) return;
        TryDeleteFile(e.FilePath);
        history.MarkDeleted(id);
    }

    HashSet<string> CurrentWallpapers()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (setter.GetCurrent() is { } all) set.Add(all);
            foreach (var m in setter.GetMonitors())
                if (setter.GetCurrent(m.DevicePath) is { } p) set.Add(p);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Couldn't read current wallpapers");
        }
        return set;
    }

    bool TryDeleteFile(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
            return true;
        }
        catch (IOException ex)
        {
            log.LogWarning("Couldn't delete {File}: {Message}", file, ex.Message);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            log.LogWarning("Couldn't delete {File}: {Message}", file, ex.Message);
            return false;
        }
    }
}
