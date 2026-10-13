using System.Text.Json;
using Oriel.Core.Auth;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

/// <summary>
/// Hears, within a second, that another device of the account set a new wallpaper. Every device writes a tiny
/// "doorbell" (<c>pulse/{uid}</c> in the Realtime Database) right after publishing <c>sync/current</c>; this keeps one
/// Server-Sent Events connection to it open and asks <see cref="FollowService"/> to fetch and show the new photo.
/// Plain HTTP, no SDK. When the connection can't be kept (offline, quota), the scheduler's poll every few minutes
/// still brings the photo, just later.
/// </summary>
public sealed class PulseListener(AuthService auth, FollowService follow, SettingsSync settings, FavoritesSync favorites,
    ILogger<PulseListener> log) : IDisposable
{
    readonly HttpClient _http = new(FastConnect.Handler()) { Timeout = Timeout.InfiniteTimeSpan };
    CancellationTokenSource? _cts;
    CancellationTokenSource? _connection;

    /// <summary>True while the live connection is open; the scheduler then polls only rarely.</summary>
    public bool IsConnected { get; private set; }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        // Signing in or out: drop the connection, so the next one is for the right account (or none).
        auth.Changed += OnAuthChanged;
        _ = RunAsync(_cts.Token);
    }

    void OnAuthChanged(object? sender, EventArgs e) => _connection?.Cancel();

    async Task RunAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _connection = connection;
            try
            {
                if (!auth.IsSignedIn || await auth.GetTokensAsync(connection.Token) is not { } tokens)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), connection.Token);
                    continue;
                }
                await ListenAsync(tokens, connection.Token);
                delay = TimeSpan.FromSeconds(2); // ended normally (the hourly token expired): reconnect right away
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // The account changed: start over at once.
                IsConnected = false;
                continue;
            }
            catch (Exception ex)
            {
                log.LogInformation("Live sync connection: {Reason}", ex.Message);
            }
            IsConnected = false;
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { return; }
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 120));
        }
    }

    async Task ListenAsync(FirebaseTokens tokens, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, FirebaseClient.PulseUrl(tokens));
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        IsConnected = true;
        log.LogInformation("Listening for wallpapers from your other devices");

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        string? name = null;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("event:", StringComparison.Ordinal)) name = line[6..].Trim();
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                switch (name)
                {
                    case "put" or "patch":
                        OnPulse(line[5..].Trim(), initial: name == "put");
                        break;
                    case "auth_revoked" or "cancel":
                        return; // the ID token expired (hourly) or access ended: reconnect with a fresh one
                }
            }
        }
    }

    /// <summary>
    /// The first event ("put") is the doorbell as it is now, catching up after a reconnect: both the photo and the
    /// settings are checked. Later rings are PATCHes carrying either photo fields or settings fields; this device's own
    /// rings are skipped.
    /// </summary>
    void OnPulse(string json, bool initial)
    {
        bool photo, prefs, stars;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return;
            var me = auth.Current?.SessionId;
            string? Field(string name) => data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            photo = data.TryGetProperty("photoId", out _) && (initial || Field("device") != me);
            prefs = data.TryGetProperty("settingsAt", out _) && (initial || Field("settingsDevice") != me);
            stars = data.TryGetProperty("favoritesAt", out _) && (initial || Field("favoritesDevice") != me);
        }
        catch (JsonException)
        {
            return;
        }
        if (stars) _ = Task.Run(() => favorites.PullAsync());
        // Settings first: a new schedule decides whether the photo check should change anything.
        if (prefs) _ = Task.Run(async () => { await settings.PullAsync(); if (photo) await follow.CheckAsync(); });
        else if (photo) _ = Task.Run(() => follow.CheckAsync());
    }

    public void Dispose()
    {
        auth.Changed -= OnAuthChanged;
        _cts?.Cancel();
        _cts?.Dispose();
        _http.Dispose();
    }
}
