using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oriel.Core.Auth;

/// <summary>Why a Firebase call failed. <see cref="Rejected"/> means Google answered and said no (bad or revoked credentials, missing document).</summary>
public enum FirebaseFailure { Network, Rejected, NotFound, Other }

public sealed class FirebaseException(FirebaseFailure failure, string message) : Exception(message)
{
    public FirebaseFailure Failure { get; } = failure;
}

public sealed record FirebaseTokens(string IdToken, string RefreshToken, DateTimeOffset ExpiresAt, string Uid);

public sealed record FirebaseProfile(string Uid, string Email, string DisplayName, string PhotoUrl, FirebaseTokens Tokens);

/// <summary>Someone following this account: their account, first name and since when.</summary>
public sealed record Follower(string Uid, string Name, DateTimeOffset? Since);

public sealed record SessionInfo(string Platform, string DeviceName, string AppVersion);

/// <summary>The wallpaper a desktop app applied last, shared through <c>users/{uid}/sync/current</c> so the browser extension can show the same photo.</summary>
public sealed record CurrentWallpaper(string PhotoId, string ImageUrl, string PageUrl, string Photographer, string PhotographerUrl,
    string? Description, string? Topic, string Source, string? Color = null);

/// <summary>
/// Firebase Auth and Firestore over their REST APIs (there's no Firebase SDK for .NET desktop). The key is the
/// public web key: what protects the data is Firebase Auth plus firestore.rules, not secrecy of this value.
/// </summary>
public sealed class FirebaseClient(HttpClient http)
{
    const string ApiKey = "AIzaSyCtUNKnXo3UcKHsG_gtupyf1Tfpo-BzKc8";
    const string DocumentsPath = "projects/oriel-gcp/databases/(default)/documents";
    const string DocumentsUrl = "https://firestore.googleapis.com/v1/" + DocumentsPath;

    /// <summary>The Realtime Database: only the doorbell <c>pulse/{uid}</c> lives there (see database.rules.json).</summary>
    const string RealtimeUrl = "https://oriel-gcp-default-rtdb.firebaseio.com";

    /// <summary>The account's doorbell, readable and writable with its ID token (also as a live event stream).</summary>
    public static string PulseUrl(FirebaseTokens tokens) => $"{RealtimeUrl}/pulse/{tokens.Uid}.json?auth={Uri.EscapeDataString(tokens.IdToken)}";

    /// <summary>
    /// Rings the account's doorbell after <c>sync/current</c> changed: the other devices listening to it fetch the new
    /// photo within a second. <paramref name="device"/> lets this device ignore its own ring.
    /// </summary>
    public async Task RingAsync(FirebaseTokens tokens, string photoId, string source, string device, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object>
        {
            ["photoId"] = photoId,
            ["source"] = source,
            ["device"] = device,
            ["at"] = new Dictionary<string, string> { [".sv"] = "timestamp" },
        };
        // PATCH, not PUT: a photo ring leaves a settings ring's fields alone, and the other way round.
        using var request = new HttpRequestMessage(HttpMethod.Patch, PulseUrl(tokens)) { Content = JsonContent.Create(body) };
        using var response = await SendAsync(() => http.SendAsync(request, ct));
        await EnsureOkAsync(response, ct);
    }

    /// <summary>Exchanges the Google ID token from the website for this app's own Firebase session.</summary>
    public async Task<FirebaseProfile> SignInWithGoogleAsync(string googleIdToken, CancellationToken ct = default)
    {
        var body = new
        {
            postBody = $"id_token={Uri.EscapeDataString(googleIdToken)}&providerId=google.com",
            requestUri = "http://localhost",
            returnSecureToken = true,
            returnIdpCredential = true,
        };
        using var response = await SendAsync(() => http.PostAsJsonAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key={ApiKey}", body, ct));
        var r = await ReadAsync<SignInResponse>(response, ct);
        return new FirebaseProfile(r.LocalId, r.Email ?? "", r.DisplayName ?? "", r.PhotoUrl ?? "",
            Tokens(r.IdToken, r.RefreshToken, r.ExpiresIn, r.LocalId));
    }

    /// <summary>
    /// Takes over the Firebase session the website made for an account without Google (email and password): fresh
    /// tokens from its refresh token, then the profile, which the token exchange doesn't return.
    /// </summary>
    public async Task<FirebaseProfile> SignInWithRefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var tokens = await RefreshAsync(refreshToken, ct);
        using var response = await SendAsync(() => http.PostAsJsonAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:lookup?key={ApiKey}", new { idToken = tokens.IdToken }, ct));
        var r = await ReadAsync<LookupResponse>(response, ct);
        var user = r.Users?.FirstOrDefault() ?? throw new FirebaseException(FirebaseFailure.Rejected, "Account not found.");
        return new FirebaseProfile(tokens.Uid, user.Email ?? "", user.DisplayName ?? "", user.PhotoUrl ?? "", tokens);
    }

    public async Task<FirebaseTokens> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });
        using var response = await SendAsync(() => http.PostAsync($"https://securetoken.googleapis.com/v1/token?key={ApiKey}", content, ct));
        var r = await ReadAsync<RefreshResponse>(response, ct);
        return Tokens(r.IdToken, r.RefreshToken, r.ExpiresIn, r.UserId);
    }

    /// <summary>Creates or replaces this install's session document.</summary>
    public Task SetSessionAsync(FirebaseTokens tokens, string sessionId, SessionInfo info, DateTimeOffset createdAt, CancellationToken ct = default)
    {
        var fields = new Dictionary<string, object>
        {
            ["platform"] = Str(info.Platform),
            ["deviceName"] = Str(info.DeviceName),
            ["appVersion"] = Str(info.AppVersion),
            ["createdAt"] = Time(createdAt),
            ["lastSeenAt"] = Time(DateTimeOffset.UtcNow),
        };
        return WriteDocumentAsync(SessionUrl(tokens.Uid, sessionId), tokens, fields, ct);
    }

    /// <summary>Bumps lastSeenAt and appVersion. Returns false if the session document is gone (signed out elsewhere).</summary>
    public async Task<bool> TouchSessionAsync(FirebaseTokens tokens, string sessionId, string appVersion, CancellationToken ct = default)
    {
        var fields = new Dictionary<string, object>
        {
            ["lastSeenAt"] = Time(DateTimeOffset.UtcNow),
            ["appVersion"] = Str(appVersion),
        };
        var url = SessionUrl(tokens.Uid, sessionId) + "?updateMask.fieldPaths=lastSeenAt&updateMask.fieldPaths=appVersion&currentDocument.exists=true";
        try
        {
            await WriteDocumentAsync(url, tokens, fields, ct);
            return true;
        }
        catch (FirebaseException ex) when (ex.Failure == FirebaseFailure.NotFound)
        {
            return false;
        }
    }

    public async Task DeleteSessionAsync(FirebaseTokens tokens, string sessionId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, SessionUrl(tokens.Uid, sessionId));
        request.Headers.Authorization = new("Bearer", tokens.IdToken);
        using var response = await SendAsync(() => http.SendAsync(request, ct));
        if (response.StatusCode != HttpStatusCode.NotFound) await EnsureOkAsync(response, ct);
    }

    /// <summary>Publishes the wallpaper just applied (creates or replaces <c>sync/current</c>; the latest write wins).</summary>
    public Task SetCurrentWallpaperAsync(FirebaseTokens tokens, CurrentWallpaper wallpaper, CancellationToken ct = default)
    {
        var fields = new Dictionary<string, object>
        {
            ["photoId"] = Str(wallpaper.PhotoId),
            ["imageUrl"] = Str(wallpaper.ImageUrl),
            ["pageUrl"] = Str(wallpaper.PageUrl),
            ["photographer"] = Str(wallpaper.Photographer),
            ["photographerUrl"] = Str(wallpaper.PhotographerUrl),
            ["source"] = Str(wallpaper.Source),
            ["updatedAt"] = Time(DateTimeOffset.UtcNow),
        };
        if (!string.IsNullOrWhiteSpace(wallpaper.Description)) fields["description"] = Str(wallpaper.Description);
        if (!string.IsNullOrWhiteSpace(wallpaper.Topic)) fields["topic"] = Str(wallpaper.Topic);
        // The photo's accent color ("#RRGGBB"): other devices (the Android app later) use it as their accent.
        if (!string.IsNullOrWhiteSpace(wallpaper.Color)) fields["color"] = Str(wallpaper.Color);
        return WriteDocumentAsync(CurrentUrl(tokens.Uid), tokens, fields, ct);
    }

    public async Task<bool> HasCurrentWallpaperAsync(FirebaseTokens tokens, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CurrentUrl(tokens.Uid) + "?mask.fieldPaths=photoId");
        request.Headers.Authorization = new("Bearer", tokens.IdToken);
        using var response = await SendAsync(() => http.SendAsync(request, ct));
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureOkAsync(response, ct);
        return true;
    }

    // ---------------------------------------------------------------- anonymous daily counts

    /// <summary>
    /// Adds 1 to each of <paramref name="fields"/> in <c>stats/{day}</c>, created on first use. Counts only: no user IDs,
    /// one small document per day (firestore.rules allows exactly this).
    /// </summary>
    public async Task IncrementStatsAsync(FirebaseTokens tokens, DateOnly day, IReadOnlyCollection<string> fields, CancellationToken ct = default)
    {
        var body = new
        {
            writes = new[]
            {
                new
                {
                    update = new { name = $"{DocumentsPath}/stats/{day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}", fields = new Dictionary<string, object>() },
                    updateMask = new { fieldPaths = Array.Empty<string>() },
                    updateTransforms = fields.Select(f => new { fieldPath = f, increment = new { integerValue = "1" } }).ToArray(),
                },
            },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{DocumentsUrl}:commit") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new("Bearer", tokens.IdToken);
        using var response = await SendAsync(() => http.SendAsync(request, ct));
        await EnsureOkAsync(response, ct);
    }

    // ---------------------------------------------------------------- shared settings and the schedule's slot

    static string SettingsUrl(string uid) => $"{DocumentsUrl}/users/{uid}/sync/settings";
    static string SlotUrl(string uid) => $"{DocumentsUrl}/users/{uid}/sync/slot";

    /// <summary>The account's shared settings and when they were last written, or null when no device has shared any yet.</summary>
    public async Task<(Services.SyncedSettings Settings, DateTimeOffset UpdatedAt)?> GetSyncedSettingsAsync(FirebaseTokens tokens, CancellationToken ct = default)
    {
        if (await GetDocumentAsync(SettingsUrl(tokens.Uid), tokens, ct) is not { } f) return null;
        static int Int(JsonElement f, string name, int fallback) =>
            f.TryGetProperty(name, out var v) && v.TryGetProperty("integerValue", out var i) && int.TryParse(i.GetString(), out var n) ? n : fallback;
        static bool Bool(JsonElement f, string name) =>
            f.TryGetProperty(name, out var v) && v.TryGetProperty("booleanValue", out var b) && b.GetBoolean();
        var interests = new List<Services.SyncedInterest>();
        if (f.TryGetProperty("interests", out var list) && list.TryGetProperty("arrayValue", out var array) && array.TryGetProperty("values", out var values))
        {
            foreach (var item in values.EnumerateArray())
            {
                if (!item.TryGetProperty("mapValue", out var map) || !map.TryGetProperty("fields", out var m)) continue;
                if (StringField(m, "query") is not { Length: > 0 } query) continue;
                interests.Add(new Services.SyncedInterest(query, Bool(m, "enabled"), Bool(m, "isCustom")));
            }
        }
        var settings = new Services.SyncedSettings(
            StringField(f, "dailyTime") ?? "09:00", StringField(f, "timeZone") ?? "UTC", Int(f, "intervalMinutes", 0),
            Bool(f, "paused"), Bool(f, "favoritesOnly"), Bool(f, "skipLowLikes"), Int(f, "minLikes", 50), interests,
            !f.TryGetProperty("matchTimeOfDay", out _) || Bool(f, "matchTimeOfDay"));
        return (settings, TimeField(f, "updatedAt") ?? DateTimeOffset.MinValue);
    }

    public Task SetSyncedSettingsAsync(FirebaseTokens tokens, Services.SyncedSettings s, string device, DateTimeOffset at, CancellationToken ct = default) =>
        WriteDocumentAsync(SettingsUrl(tokens.Uid), tokens, new Dictionary<string, object>
        {
            ["dailyTime"] = Str(s.DailyTime),
            ["timeZone"] = Str(s.TimeZone),
            ["intervalMinutes"] = new { integerValue = s.IntervalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ["paused"] = new { booleanValue = s.Paused },
            ["favoritesOnly"] = new { booleanValue = s.FavoritesOnly },
            ["skipLowLikes"] = new { booleanValue = s.SkipLowLikes },
            ["minLikes"] = new { integerValue = s.MinLikes.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ["matchTimeOfDay"] = new { booleanValue = s.MatchTimeOfDay },
            ["interests"] = new
            {
                arrayValue = new
                {
                    values = s.Interests.Select(i => new
                    {
                        mapValue = new
                        {
                            fields = new Dictionary<string, object>
                            {
                                ["query"] = Str(i.Query),
                                ["enabled"] = new { booleanValue = i.Enabled },
                                ["isCustom"] = new { booleanValue = i.IsCustom },
                            },
                        },
                    }).ToArray(),
                },
            },
            ["updatedAt"] = Time(at),
            ["device"] = Str(device),
        }, ct);

    /// <summary>
    /// Claims a scheduled moment for this device ("first device wins"): a compare-and-set on one document, so of two
    /// devices ticking together exactly one gets it. False when another device already holds this slot.
    /// </summary>
    public async Task<bool> ClaimSlotAsync(FirebaseTokens tokens, string slotId, string device, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            string precondition;
            using (var request = new HttpRequestMessage(HttpMethod.Get, SlotUrl(tokens.Uid)))
            {
                request.Headers.Authorization = new("Bearer", tokens.IdToken);
                using var response = await SendAsync(() => http.SendAsync(request, ct));
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    precondition = "currentDocument.exists=false";
                }
                else
                {
                    await EnsureOkAsync(response, ct);
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    var fields = doc.RootElement.TryGetProperty("fields", out var f) ? f : default;
                    if (fields.ValueKind == JsonValueKind.Object && StringField(fields, "slotId") == slotId) return false;
                    precondition = "currentDocument.updateTime=" + Uri.EscapeDataString(doc.RootElement.GetProperty("updateTime").GetString()!);
                }
            }
            try
            {
                await WriteDocumentAsync($"{SlotUrl(tokens.Uid)}?{precondition}", tokens, new Dictionary<string, object>
                {
                    ["slotId"] = Str(slotId),
                    ["device"] = Str(device),
                    ["at"] = Time(DateTimeOffset.UtcNow),
                }, ct);
                return true;
            }
            catch (FirebaseException ex) when (ex.Failure is FirebaseFailure.NotFound or FirebaseFailure.Rejected)
            {
                // Another device wrote it between the read and the write: look again.
            }
        }
        return false;
    }

    /// <summary>Rings the other devices: the shared settings changed (they read sync/settings again).</summary>
    public async Task RingSettingsAsync(FirebaseTokens tokens, string device, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object>
        {
            ["settingsAt"] = new Dictionary<string, string> { [".sv"] = "timestamp" },
            ["settingsDevice"] = device,
        };
        using var request = new HttpRequestMessage(HttpMethod.Patch, PulseUrl(tokens)) { Content = JsonContent.Create(body) };
        using var response = await SendAsync(() => http.SendAsync(request, ct));
        await EnsureOkAsync(response, ct);
    }

    // ---------------------------------------------------------------- favorites, the same on every device

    /// <summary>Writes one favorite (or un-favorite: the document stays, with favorite = false, so the change syncs).</summary>
    public Task SetFavoriteAsync(FirebaseTokens tokens, Services.SyncedFavorite f, string device, CancellationToken ct = default)
    {
        var fields = new Dictionary<string, object>
        {
            ["photoId"] = Str(f.PhotoId),
            ["imageUrl"] = Str(f.ImageUrl),
            ["pageUrl"] = Str(f.PageUrl),
            ["photographer"] = Str(f.Photographer),
            ["photographerUrl"] = Str(f.PhotographerUrl),
            ["favorite"] = new { booleanValue = f.Favorite },
            ["updatedAt"] = Time(f.UpdatedAt),
            ["device"] = Str(device),
        };
        if (!string.IsNullOrWhiteSpace(f.Description)) fields["description"] = Str(f.Description);
        return WriteDocumentAsync($"{DocumentsUrl}/users/{tokens.Uid}/favorites/{Uri.EscapeDataString(f.PhotoId)}", tokens, fields, ct);
    }

    /// <summary>The favorites changed since <paramref name="since"/> (all of them when null), oldest first.</summary>
    public async Task<IReadOnlyList<Services.SyncedFavorite>> GetFavoritesSinceAsync(FirebaseTokens tokens, DateTimeOffset? since, CancellationToken ct = default)
    {
        object query = since is { } s
            ? new
            {
                structuredQuery = new
                {
                    from = new[] { new { collectionId = "favorites" } },
                    where = new { fieldFilter = new { field = new { fieldPath = "updatedAt" }, op = "GREATER_THAN", value = new { timestampValue = s.UtcDateTime.ToString("o") } } },
                },
            }
            : new { structuredQuery = new { from = new[] { new { collectionId = "favorites" } } } };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{DocumentsUrl}/users/{tokens.Uid}:runQuery") { Content = JsonContent.Create(query) };
        request.Headers.Authorization = new("Bearer", tokens.IdToken);
        using var response = await SendAsync(() => http.SendAsync(request, ct));
        await EnsureOkAsync(response, ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var list = new List<Services.SyncedFavorite>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            if (!row.TryGetProperty("document", out var d) || !d.TryGetProperty("fields", out var f)) continue;
            if (StringField(f, "photoId") is not { Length: > 0 } id || StringField(f, "imageUrl") is not { } image) continue;
            var favorite = f.TryGetProperty("favorite", out var fv) && fv.TryGetProperty("booleanValue", out var b) && b.GetBoolean();
            list.Add(new Services.SyncedFavorite(id, image, StringField(f, "pageUrl") ?? "", StringField(f, "photographer") ?? "",
                StringField(f, "photographerUrl") ?? "", StringField(f, "description"), favorite, TimeField(f, "updatedAt") ?? DateTimeOffset.UtcNow));
        }
        return list;
    }

    /// <summary>Rings the other devices: favorites changed (they fetch the changes).</summary>
    public async Task RingFavoritesAsync(FirebaseTokens tokens, string device, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object>
        {
            ["favoritesAt"] = new Dictionary<string, string> { [".sv"] = "timestamp" },
            ["favoritesDevice"] = device,
        };
        using var request = new HttpRequestMessage(HttpMethod.Patch, PulseUrl(tokens)) { Content = JsonContent.Create(body) };
        using var response = await SendAsync(() => http.SendAsync(request, ct));
        await EnsureOkAsync(response, ct);
    }

    // ---------------------------------------------------------------- following a friend

    /// <summary>Who a follow code belongs to, or null when there is no such code.</summary>
    public async Task<(string Uid, string Name, string Picture)?> GetFollowCodeAsync(FirebaseTokens tokens, string code, CancellationToken ct = default)
    {
        var fields = await GetDocumentAsync($"{DocumentsUrl}/codes/{code}", tokens, ct);
        if (fields is not { } f) return null;
        return (StringField(f, "uid") ?? "", StringField(f, "name") ?? "", StringField(f, "picture") ?? "");
    }

    /// <summary>
    /// Claims a new follow code for this account, with the first name and picture its follow page shows. Fails if the
    /// code is taken (a new one is tried).
    /// </summary>
    public Task CreateFollowCodeAsync(FirebaseTokens tokens, string code, string name, string picture, CancellationToken ct = default) =>
        WriteDocumentAsync($"{DocumentsUrl}/codes/{code}?currentDocument.exists=false", tokens, new Dictionary<string, object>
        {
            ["uid"] = Str(tokens.Uid),
            ["name"] = Str(name),
            ["picture"] = Str(picture),
            ["createdAt"] = Time(DateTimeOffset.UtcNow),
        }, ct);

    /// <summary>The wallpaper the follow page shows: the owner's current one, updated as it changes.</summary>
    public Task SetFollowCodePhotoAsync(FirebaseTokens tokens, string code, Models.SharedPhoto photo, CancellationToken ct = default) =>
        WriteDocumentAsync($"{DocumentsUrl}/codes/{code}?currentDocument.exists=true" + string.Concat(PhotoFieldNames.Select(f => $"&updateMask.fieldPaths={f}")),
            tokens, PhotoFields(photo), ct);

    /// <summary>A shared wallpaper's <c>shares/{id}</c> document: the photo, and the sender's first name and picture.</summary>
    public Task CreateShareAsync(FirebaseTokens tokens, string id, string from, string picture, Models.SharedPhoto photo, CancellationToken ct = default)
    {
        var fields = PhotoFields(photo);
        fields["uid"] = Str(tokens.Uid);
        fields["from"] = Str(from);
        fields["picture"] = Str(picture);
        fields["createdAt"] = Time(DateTimeOffset.UtcNow);
        return WriteDocumentAsync($"{DocumentsUrl}/shares/{id}?currentDocument.exists=false", tokens, fields, ct);
    }

    static readonly string[] PhotoFieldNames = ["photoId", "photoPath", "photographer", "photographerHandle"];

    static Dictionary<string, object> PhotoFields(Models.SharedPhoto photo) => new()
    {
        ["photoId"] = Str(photo.Id),
        ["photoPath"] = Str(photo.Path),
        ["photographer"] = Str(photo.Photographer),
        ["photographerHandle"] = Str(photo.Handle),
    };

    public Task DeleteFollowCodeAsync(FirebaseTokens tokens, string code, CancellationToken ct = default) =>
        DeleteDocumentAsync($"{DocumentsUrl}/codes/{code}", tokens, ct);

    /// <summary>Registers this account as a follower of <paramref name="friendUid"/>, proven by their code.</summary>
    public Task AddFollowerAsync(FirebaseTokens tokens, string friendUid, string code, string name, CancellationToken ct = default) =>
        WriteDocumentAsync($"{DocumentsUrl}/users/{friendUid}/followers/{tokens.Uid}", tokens, new Dictionary<string, object>
        {
            ["code"] = Str(code),
            ["name"] = Str(name),
            ["createdAt"] = Time(DateTimeOffset.UtcNow),
        }, ct);

    public Task RemoveFollowerAsync(FirebaseTokens tokens, string ownerUid, string followerUid, CancellationToken ct = default) =>
        DeleteDocumentAsync($"{DocumentsUrl}/users/{ownerUid}/followers/{followerUid}", tokens, ct);

    /// <summary>The accounts following this one, newest first.</summary>
    public async Task<IReadOnlyList<Follower>> ListFollowersAsync(FirebaseTokens tokens, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{DocumentsUrl}/users/{tokens.Uid}/followers?pageSize=300&mask.fieldPaths=name&mask.fieldPaths=createdAt");
        request.Headers.Authorization = new("Bearer", tokens.IdToken);
        using var response = await SendAsync(() => http.SendAsync(request, ct));
        await EnsureOkAsync(response, ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("documents", out var docs)) return [];
        return docs.EnumerateArray()
            .Select(d =>
            {
                var fields = d.TryGetProperty("fields", out var f) ? f : default;
                var uid = d.GetProperty("name").GetString()!.Split('/')[^1];
                var name = fields.ValueKind == JsonValueKind.Object ? StringField(fields, "name") : null;
                return new Follower(uid, string.IsNullOrWhiteSpace(name) ? "A friend" : name, TimeField(fields, "createdAt"));
            })
            .OrderByDescending(f => f.Since)
            .ToList();
    }

    /// <summary>
    /// The photo a friend has on their desktop now (their <c>sync/current</c>), or null if they have none yet.
    /// Throws <see cref="FirebaseFailure.Rejected"/> once they stop sharing (the rules no longer let us read it).
    /// </summary>
    public async Task<string?> GetCurrentPhotoIdAsync(FirebaseTokens tokens, string ownerUid, CancellationToken ct = default)
    {
        var fields = await GetDocumentAsync(CurrentUrl(ownerUid) + "?mask.fieldPaths=photoId", tokens, ct);
        return fields is { } f ? StringField(f, "photoId") : null;
    }

    /// <summary>A document's fields, or null when it doesn't exist.</summary>
    async Task<JsonElement?> GetDocumentAsync(string url, FirebaseTokens tokens, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", tokens.IdToken);
        using var response = await SendAsync(() => http.SendAsync(request, ct));
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureOkAsync(response, ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("fields", out var fields) ? fields.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
    }

    async Task DeleteDocumentAsync(string url, FirebaseTokens tokens, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new("Bearer", tokens.IdToken);
        using var response = await SendAsync(() => http.SendAsync(request, ct));
        if (response.StatusCode != HttpStatusCode.NotFound) await EnsureOkAsync(response, ct);
    }

    static string? StringField(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var f) && f.TryGetProperty("stringValue", out var v) ? v.GetString() : null;

    static DateTimeOffset? TimeField(JsonElement fields, string name) =>
        fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty(name, out var f) && f.TryGetProperty("timestampValue", out var v)
            && DateTimeOffset.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    static string CurrentUrl(string uid) => $"{DocumentsUrl}/users/{uid}/sync/current";

    static string SessionUrl(string uid, string sessionId) => $"{DocumentsUrl}/users/{uid}/sessions/{sessionId}";

    async Task WriteDocumentAsync(string url, FirebaseTokens tokens, Dictionary<string, object> fields, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonContent.Create(new { fields }) };
        request.Headers.Authorization = new("Bearer", tokens.IdToken);
        using var response = await SendAsync(() => http.SendAsync(request, ct));
        await EnsureOkAsync(response, ct);
    }

    static object Str(string value) => new { stringValue = value };
    static object Time(DateTimeOffset value) => new { timestampValue = value.UtcDateTime.ToString("o") };

    static FirebaseTokens Tokens(string idToken, string refreshToken, string expiresIn, string uid) =>
        new(idToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(int.TryParse(expiresIn, out var s) ? s : 3600), uid);

    static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        try { return await send(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new FirebaseException(FirebaseFailure.Network, "Couldn't reach Google. Check your connection.");
        }
    }

    static async Task EnsureOkAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var message = ErrorMessage(await response.Content.ReadAsStringAsync(ct));
        var failure = response.StatusCode switch
        {
            HttpStatusCode.NotFound => FirebaseFailure.NotFound,
            HttpStatusCode.TooManyRequests => FirebaseFailure.Other,
            >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError => FirebaseFailure.Rejected,
            _ => FirebaseFailure.Other,
        };
        // A conditional write on a missing document is reported as 400 FAILED_PRECONDITION or 404 depending on the API.
        if (message.Contains("NOT_FOUND") || message.Contains("FAILED_PRECONDITION")) failure = FirebaseFailure.NotFound;
        throw new FirebaseException(failure, message);
    }

    static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await EnsureOkAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new FirebaseException(FirebaseFailure.Other, "Empty response.");
    }

    static string ErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var error = doc.RootElement.GetProperty("error");
            var message = error.TryGetProperty("message", out var m) ? m.ToString() : "";
            var status = error.TryGetProperty("status", out var s) ? s.ToString() : "";
            return $"{status} {message}".Trim();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "Unexpected response from Google.";
        }
    }

    sealed record SignInResponse(
        [property: JsonPropertyName("localId")] string LocalId,
        [property: JsonPropertyName("idToken")] string IdToken,
        [property: JsonPropertyName("refreshToken")] string RefreshToken,
        [property: JsonPropertyName("expiresIn")] string ExpiresIn,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("photoUrl")] string? PhotoUrl);

    sealed record LookupResponse([property: JsonPropertyName("users")] List<LookupUser>? Users);

    sealed record LookupUser(
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("photoUrl")] string? PhotoUrl);

    sealed record RefreshResponse(
        [property: JsonPropertyName("id_token")] string IdToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")] string ExpiresIn,
        [property: JsonPropertyName("user_id")] string UserId);
}
