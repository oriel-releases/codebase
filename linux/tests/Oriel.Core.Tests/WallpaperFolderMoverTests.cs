using Oriel.Core.Models;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Oriel.Core.Tests;

public sealed class WallpaperFolderMoverTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly FakeSetter _setter = new();
    readonly JsonSettingsStore _store;
    readonly SqliteHistoryStore _history;
    readonly WallpaperFolderMover _mover;
    readonly string _newFolder;

    public WallpaperFolderMoverTests()
    {
        var paths = _dir.Paths;
        paths.EnsureCreated();
        _store = new JsonSettingsStore(paths);
        _history = new SqliteHistoryStore(paths);
        _mover = new WallpaperFolderMover(_store, _history, _setter, paths, NullLogger<WallpaperFolderMover>.Instance);
        _newFolder = Path.Combine(paths.DataRoot, "new place");
    }

    public void Dispose() => _dir.Dispose();

    long AddPhoto(string name)
    {
        Directory.CreateDirectory(_dir.Paths.PicturesRoot);
        var file = Path.Combine(_dir.Paths.PicturesRoot, name);
        File.WriteAllText(file, "jpg");
        return _history.Add(new HistoryEntry
        {
            Provider = "unsplash", PhotoId = name, FilePath = file, ThumbnailUrl = "t", PhotoPageUrl = "p",
            PhotographerName = "n", PhotographerUrl = "u", AppliedAt = DateTimeOffset.Now,
        });
    }

    [Fact]
    public void Moves_photos_updates_history_settings_and_current_wallpaper()
    {
        var a = AddPhoto("a.jpg");
        var b = AddPhoto("b.jpg");
        _setter.SetForAll(_history.Get(b)!.FilePath, WallpaperFit.Fill);

        Assert.Equal(2, _mover.MoveTo(_newFolder));

        Assert.Equal(Path.Combine(_newFolder, "a.jpg"), _history.Get(a)!.FilePath);
        Assert.True(File.Exists(_history.Get(b)!.FilePath));
        Assert.Equal(_newFolder, _store.Load().WallpaperFolder);
        Assert.Equal(Path.Combine(_newFolder, "b.jpg"), _setter.GetCurrent());
        Assert.False(Directory.Exists(_dir.Paths.PicturesRoot)); // empty old folder is cleaned up
    }

    [Fact]
    public void Refuses_a_folder_that_is_not_empty()
    {
        var a = AddPhoto("a.jpg");
        Directory.CreateDirectory(_newFolder);
        File.WriteAllText(Path.Combine(_newFolder, "mine.txt"), "x");

        Assert.Throws<WallpaperException>(() => _mover.MoveTo(_newFolder));

        Assert.True(File.Exists(_history.Get(a)!.FilePath));
        Assert.Null(_store.Load().WallpaperFolder);
    }

    [Fact]
    public void Failed_move_puts_everything_back()
    {
        // Needs a file that can't be moved. Windows gives that with an exclusive lock; Linux has no mandatory
        // locks (and a root test run ignores permissions), so the rollback is only exercised on Windows.
        if (!OperatingSystem.IsWindows()) return;

        var a = AddPhoto("a.jpg");
        AddPhoto("b.jpg");
        using var locked = File.Open(Path.Combine(_dir.Paths.PicturesRoot, "b.jpg"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<WallpaperException>(() => _mover.MoveTo(_newFolder));

        Assert.True(File.Exists(_history.Get(a)!.FilePath));
        Assert.Null(_store.Load().WallpaperFolder);
    }
}
