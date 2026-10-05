using System.Net;
using Oriel.Core;
using Oriel.Core.Models;
using Oriel.Core.Providers;
using Oriel.Core.Scheduling;
using Oriel.Core.Security;
using Oriel.Core.Services;
using Oriel.Core.Wallpaper;

namespace Oriel.Core.Tests;

sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oriel-tests", Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public AppPaths Paths => new(System.IO.Path.Combine(Path, "data"), System.IO.Path.Combine(Path, "pics"));
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; set; } = now;
    public DateOnly Today => DateOnly.FromDateTime(Now.LocalDateTime);
}

sealed class FakeKeyStore(string? key = "test-key") : IApiKeyStore
{
    public string? Get() => key;
    public bool HasKey => key is not null;
}

sealed class FakeStartup : IStartupRegistration
{
    public bool? Enabled { get; private set; }
    public void Set(bool enabled, string exePath) => Enabled = enabled;
}

sealed class FakeTasks : IScheduledTasks
{
    public List<string> Calls { get; } = [];
    public DateTimeOffset? RetryAt { get; private set; }

    public bool Register(string exePath, TimeOnly time, bool atSignIn, TimeSpan? repeatEvery = null)
    {
        Calls.Add($"register {time:HH:mm} signin={atSignIn}");
        return true;
    }

    public bool ScheduleRetry(string exePath, DateTimeOffset at)
    {
        RetryAt = at;
        Calls.Add("retry");
        return true;
    }

    public void Unregister() => Calls.Add("unregister");
}

sealed class FakeSetter : IWallpaperSetter
{
    public List<MonitorInfo> Monitors { get; } = [new("MON1", 0, 0, 2560, 1440)];
    public List<(string? Monitor, string Path, WallpaperFit Fit)> Calls { get; } = [];
    public bool Throw { get; set; }

    public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors;
    public void SetForAll(string imagePath, WallpaperFit fit)
    {
        if (Throw) throw new InvalidOperationException("boom");
        Calls.Add((null, imagePath, fit));
    }
    public List<string> AllDesktopCalls { get; } = [];
    public bool SetForAllDesktops(string imagePath)
    {
        AllDesktopCalls.Add(imagePath);
        return true;
    }
    public void SetForMonitor(string monitorDevicePath, string imagePath, WallpaperFit fit) =>
        Calls.Add((monitorDevicePath, imagePath, fit));
    public string? GetCurrent(string? monitorDevicePath = null) =>
        Calls.LastOrDefault(c => c.Monitor == monitorDevicePath).Path;
}

sealed class FakeProvider : IImageProvider
{
    public Queue<Func<IReadOnlyList<PhotoCandidate>>> Responses { get; } = new();
    public List<string> Queries { get; } = [];
    public List<string> Tracked { get; } = [];
    public byte[] Bytes { get; set; } = Jpeg();
    public bool Configured { get; set; } = true;

    public string Name => "unsplash";
    public bool IsConfigured => Configured;

    public Task<IReadOnlyList<PhotoCandidate>> GetRandomAsync(ImageQuery query, CancellationToken ct)
    {
        Queries.Add(query.Query);
        return Task.FromResult(Responses.Count > 0 ? Responses.Dequeue()() : []);
    }

    public Task<SearchPage> SearchAsync(string query, int page, CancellationToken ct) =>
        Task.FromResult(new SearchPage(Responses.Count > 0 ? Responses.Dequeue()() : [], 1));

    public Task<byte[]> DownloadAsync(PhotoCandidate photo, PixelSize size, CancellationToken ct) => Task.FromResult(Bytes);

    public Task TrackUsageAsync(PhotoCandidate photo, CancellationToken ct)
    {
        Tracked.Add(photo.Id);
        return Task.CompletedTask;
    }

    public static byte[] Jpeg()
    {
        var b = new byte[4096];
        b[0] = 0xFF; b[1] = 0xD8;
        return b;
    }

    public static PhotoCandidate Photo(string id, int w = 6000, int h = 4000, int likes = 100, string? desc = null) => new()
    {
        Provider = "unsplash", Id = id, Width = w, Height = h, Likes = likes, Description = desc,
        RawUrl = $"https://images.unsplash.com/{id}?ixid=x", ThumbnailUrl = $"https://t/{id}",
        PhotoPageUrl = $"https://unsplash.com/photos/{id}", PhotographerName = "Ann", PhotographerUrl = "https://unsplash.com/@ann",
        DownloadTrackingUrl = $"https://api.unsplash.com/photos/{id}/download",
    };
}

sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK, int? remaining = 49)
    {
        var r = new HttpResponseMessage(code) { Content = new StringContent(json) };
        if (remaining is not null) r.Headers.Add("X-Ratelimit-Remaining", remaining.ToString());
        return r;
    }
}
