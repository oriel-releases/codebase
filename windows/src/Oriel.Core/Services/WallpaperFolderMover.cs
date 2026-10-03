using Oriel.Core.Models;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Oriel.Core.Wallpaper;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

/// <summary>
/// Switches the wallpaper folder and moves every photo there. All or nothing: if a file can't be
/// moved, the ones already moved go back and the old folder stays in use.
/// </summary>
public sealed class WallpaperFolderMover(ISettingsStore settings, IHistoryStore history, IWallpaperSetter setter,
    AppPaths paths, ILogger<WallpaperFolderMover> log)
{
    /// <returns>How many files were moved.</returns>
    public int MoveTo(string newFolder)
    {
        var current = settings.Load();
        var oldFolder = Path.GetFullPath(paths.WallpaperFolder(current));
        newFolder = Path.GetFullPath(newFolder);
        if (string.Equals(oldFolder.TrimEnd('\\'), newFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return 0;
        if (Directory.Exists(newFolder) && Directory.EnumerateFileSystemEntries(newFolder).Any())
            throw new WallpaperException("Pick an empty folder. Oriel moves your photos into it.");

        // Don't race a wallpaper change that is saving into the old folder.
        using var gate = ApplyLock.TryAcquire(paths.LockFile)
            ?? throw new WallpaperException("A wallpaper change is in progress. Try again in a moment.");

        Directory.CreateDirectory(newFolder);
        var moved = MoveFiles(oldFolder, newFolder);

        history.UpdateFilePaths(moved);
        settings.Update(s => s.WallpaperFolder = newFolder);
        RepointCurrentWallpaper(moved, current.Fit);
        TryDeleteIfEmpty(oldFolder);

        log.LogInformation("Moved {Count} photo(s) to the new wallpaper folder", moved.Count);
        return moved.Count;
    }

    static Dictionary<string, string> MoveFiles(string from, string to)
    {
        var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(from)) return moved;
        try
        {
            foreach (var file in Directory.EnumerateFiles(from))
            {
                if (file.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue; // unfinished download
                var target = Path.Combine(to, Path.GetFileName(file));
                File.Move(file, target); // copies + deletes across drives
                moved[file] = target;
            }
            return moved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var (original, target) in moved)
                try { File.Move(target, original); } catch (IOException) { }
            throw new WallpaperException($"Couldn't move the photos: {ex.Message} Nothing was changed.", ex);
        }
    }

    /// <summary>Windows still points at the old paths; set the same images again from their new place.</summary>
    void RepointCurrentWallpaper(IReadOnlyDictionary<string, string> moved, WallpaperFit fit)
    {
        try
        {
            if (setter.GetCurrent() is { } all)
            {
                if (moved.TryGetValue(all, out var newAll))
                {
                    setter.SetForAll(newAll, fit);
                    setter.SetForAllDesktops(newAll);
                }
                return;
            }
            foreach (var m in setter.GetMonitors())
                if (setter.GetCurrent(m.DevicePath) is { } path && moved.TryGetValue(path, out var newPath))
                    setter.SetForMonitor(m.DevicePath, newPath, fit);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't re-point the current wallpaper after moving the folder");
        }
    }

    void TryDeleteIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogDebug(ex, "Old wallpaper folder left in place");
        }
    }
}
