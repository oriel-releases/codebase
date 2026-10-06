using Oriel.Core.Models;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Oriel.Core.Tests;

public sealed class StorageTests : IDisposable
{
    readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    static HistoryEntry Entry(string id, DateTimeOffset at, string file) => new()
    {
        Provider = "unsplash", PhotoId = id, FilePath = file, ThumbnailUrl = "t", PhotoPageUrl = "p",
        PhotographerName = "n", PhotographerUrl = "u", AppliedAt = at,
    };

    [Fact]
    public void Settings_roundtrip_and_defaults()
    {
        var store = new JsonSettingsStore(_dir.Paths);
        var s = store.Load();
        Assert.Contains("nature", s.EnabledQueries());

        s.Fit = WallpaperFit.Span;
        s.Topics.Add(new TopicSetting { Query = "foggy forest", Enabled = true, IsCustom = true });
        store.Save(s);

        var again = store.Load();
        Assert.Equal(WallpaperFit.Span, again.Fit);
        Assert.Contains("foggy forest", again.EnabledQueries());
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults()
    {
        var paths = _dir.Paths;
        paths.EnsureCreated();
        File.WriteAllText(paths.SettingsFile, "{ not json");
        Assert.Equal("09:00", new JsonSettingsStore(paths).Load().DailyTime);
    }

    [Fact]
    public void History_tracks_favorites_and_soft_deletes()
    {
        var db = new SqliteHistoryStore(_dir.Paths);
        var now = DateTimeOffset.Now;
        var id = db.Add(Entry("a", now, "f"));

        db.SetFavorite(id, true);
        Assert.True(db.Get(id)!.IsFavorite);

        db.MarkDeleted(id);
        Assert.Empty(db.List());
        Assert.True(db.WasUsed("unsplash", "a")); // still blocks reuse
    }

    [Fact]
    public void Retention_deletes_old_non_favorites_only()
    {
        var paths = _dir.Paths;
        paths.EnsureCreated();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
        var db = new SqliteHistoryStore(paths);
        var store = new JsonSettingsStore(paths);
        var setter = new FakeSetter();

        Directory.CreateDirectory(paths.PicturesRoot);
        string File_(string n) { var f = Path.Combine(paths.PicturesRoot, n); File.WriteAllText(f, "x"); return f; }
        var old = db.Add(Entry("old", clock.Now.AddDays(-40), File_("old.jpg")));
        var fav = db.Add(Entry("fav", clock.Now.AddDays(-40), File_("fav.jpg")));
        var onScreen = db.Add(Entry("screen", clock.Now.AddDays(-40), File_("screen.jpg")));
        var fresh = db.Add(Entry("fresh", clock.Now.AddDays(-2), File_("fresh.jpg")));
        db.SetFavorite(fav, true);
        setter.SetForAll(db.Get(onScreen)!.FilePath, WallpaperFit.Fill);

        var removed = new RetentionService(db, store, setter, clock, NullLogger<RetentionService>.Instance).Cleanup();

        Assert.Equal(1, removed);
        Assert.False(File.Exists(db.Get(old)!.FilePath));
        Assert.True(db.Get(old)!.IsDeleted);
        Assert.True(File.Exists(db.Get(fav)!.FilePath));
        Assert.True(File.Exists(db.Get(onScreen)!.FilePath));
        Assert.True(File.Exists(db.Get(fresh)!.FilePath));
    }
}

public class ScheduleTests
{
    [Fact]
    public void Backoff_grows_and_caps()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), RetryBackoff.Delay(1));
        Assert.Equal(TimeSpan.FromMinutes(15), RetryBackoff.Delay(3));
        Assert.Equal(TimeSpan.FromHours(1), RetryBackoff.Delay(99));
    }

    [Fact]
    public void Topic_picker_avoids_used_topics_when_possible()
    {
        var p = new RandomTopicPicker(new Random(3));
        var avoid = new HashSet<string> { "a" };
        for (var i = 0; i < 20; i++) Assert.Equal("b", p.Pick(["a", "b"], avoid));
        Assert.Equal(RandomTopicPicker.Fallback, p.Pick([]));
    }
}
