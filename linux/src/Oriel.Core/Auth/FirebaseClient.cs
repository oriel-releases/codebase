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

    // ---------------------------------------------------------------- following a friend

    /// <summary>Who a follow code belongs to, or null when there is no such code.</summary>
    public async Task<(string Uid, string Name)?> GetFollowCodeAsync(FirebaseTokens tokens, string code, CancellationToken ct = default)
    {
        var fields = await GetDocumentAsync($"{DocumentsUrl}/codes/{code}", tokens, ct);
        if (fields is not { } f) return null;
        return (StringField(f, "uid") ?? "", StringField(f, "name") ?? "");
    }

    /// <summary>Claims a new follow code for this account. Fails if the code is taken (a new one is tried).</summary>
    public Task CreateFollowCodeAsync(FirebaseTokens tokens, string code, string name, CancellationToken ct = default) =>
        WriteDocumentAsync($"{DocumentsUrl}/codes/{code}?currentDocument.exists=false", tokens, new Dictionary<string, object>
        {
            ["uid"] = Str(tokens.Uid),
            ["name"] = Str(name),
            ["createdAt"] = Time(DateTimeOffset.UtcNow),
        }, ct);

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

    /// <summary>The accounts following this one.</summary>
    public async Task<IReadOnlyList<string>> ListFollowersAsync(FirebaseTokens tokens, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{DocumentsUrl}/users/{tokens.Uid}/followers?pageSize=300&mask.fieldPaths=code");
        request.Headers.Authorization = new("Bearer", tokens.IdToken);
        using var response = await SendAsync(() => http.SendAsync(request, ct));
        await EnsureOkAsync(response, ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("documents", out var docs)) return [];
        return docs.EnumerateArray().Select(d => d.GetProperty("name").GetString()!.Split('/')[^1]).ToList();
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

    sealed record RefreshResponse(
        [property: JsonPropertyName("id_token")] string IdToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")] string ExpiresIn,
        [property: JsonPropertyName("user_id")] string UserId);
}
