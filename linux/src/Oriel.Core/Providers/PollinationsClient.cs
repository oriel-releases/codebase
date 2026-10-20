using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Oriel.Core.Models;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Providers;

/// <summary>
/// AI wallpapers made by Pollinations (https://pollinations.ai) from a few words, with the key built into the app
/// (POLLINATIONS_KEY, see Oriel.Core.csproj). Pollinations keeps each picture at a public link (media.pollinations.ai),
/// which is what the account's devices sync, like an Unsplash photo: other devices download that link.
/// </summary>
public sealed class PollinationsClient(HttpClient http, AppPaths paths, ILogger<PollinationsClient> log)
{
    public const string Provider = "ai";
    const string ImagesUrl = "https://gen.pollinations.ai/v1/images/generations";
    const string MediaHost = "media.pollinations.ai";
    const string TextUrl = "https://gen.pollinations.ai/v1/chat/completions";
    /// <summary>Writes the full picture description from the user's few words: quick, and costs next to nothing.</summary>
    const string TextModel = "openai/gpt-5.4-nano";
    static readonly TimeSpan ExpandTimeout = TimeSpan.FromSeconds(20);

    const string ExpandInstructions =
        "You write prompts for an AI image model that makes desktop wallpapers. Turn the user's few words into one vivid " +
        "prompt of 60 to 90 words: the subject, the setting, the time of day and light, colors, mood, camera or art style, " +
        "and a composition that leaves calm space for desktop icons. The scene is full-bleed: it runs to every edge of the image. Describe only the scene itself; never mention paper, canvas, frames, borders, edges, margins, posters, prints or screens. Keep everything the user asked for, in their language's " +
        "meaning but written in English, and don't add people unless asked. No text, logos or watermarks in the picture. " +
        "Reply with the prompt only.";
    /// <summary>Fast, sharp and among the cheapest models (0.004 Pollen an image when this was written).</summary>
    const string Model = "zimage";
    /// <summary>The widest picture the model makes (2560 is refused).</summary>
    const int MaxSide = 2048;
    /// <summary>Pictures one device may make a day: the key is shared by everyone, so nobody uses it all up.</summary>
    public const int MaxPerDay = 20;
    /// <summary>Making an image takes a few seconds, sometimes much longer when the service is busy.</summary>
    static readonly TimeSpan CreateTimeout = TimeSpan.FromSeconds(90);

    static readonly string? Key = typeof(PollinationsClient).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "PollinationsKey")?.Value;

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Key);

    /// <summary>A new seed: the same words with another seed make another picture.</summary>
    public static int NewSeed() => Random.Shared.Next(1, int.MaxValue);

    /// <summary>Pictures this device can still make today.</summary>
    public int LeftToday => Math.Max(0, MaxPerDay - MadeToday().Count);

    /// <summary>
    /// Paints a picture for the words, in the screen's shape (at most 2048 px wide). Returns the photo record (its public
    /// link), ready to be set, and the JPEG or PNG bytes.
    /// </summary>
    /// <param name="progress">Told what is happening: "Imagining…" while the words are written out, then "Painting…".</param>
    public async Task<(PhotoCandidate Photo, byte[] Image)> CreateAsync(string prompt, string? style, int seed, PixelSize screen,
        Action<string>? progress = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new ProviderException(ProviderErrorKind.MissingKey, "This build of Oriel can't create AI wallpapers.");
        if (LeftToday == 0)
            throw new ProviderException(ProviderErrorKind.RateLimited,
                $"You've created {MaxPerDay} wallpapers today, the most for one day. Come back tomorrow for more.");

        var size = SizeFor(screen);
        var words = string.IsNullOrWhiteSpace(style) ? prompt.Trim() : $"{prompt.Trim()}, {style}";
        // People write a few words; the picture model draws far better from a full description.
        progress?.Invoke("Imagining…");
        var described = await ExpandAsync(words, ct) ?? words;
        progress?.Invoke("Painting…");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CreateTimeout);
        // Kept by Pollinations at a public link, so the account's other devices can show the very same picture.
        using var req = new HttpRequestMessage(HttpMethod.Post, ImagesUrl)
        {
            Content = JsonContent.Create(new
            {
                model = Model,
                prompt = Final(described, prompt.Trim()),
                size = $"{size.Width}x{size.Height}",
                seed,
                safe = true,
                response_format = "url",
            }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        string link;
        try
        {
            using var resp = await http.SendAsync(req, timeout.Token);
            if (!resp.IsSuccessStatusCode) throw await ErrorAsync(resp);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(timeout.Token));
            link = doc.RootElement.GetProperty("data")[0].GetProperty("url").GetString() ?? "";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ProviderException(ProviderErrorKind.Network, timeout.IsCancellationRequested
                ? "Pollinations is taking too long right now. Try again in a moment."
                : "Couldn't reach Pollinations. Check your connection and try again.", ex);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new ProviderException(ProviderErrorKind.BadResponse, "Pollinations didn't send back a picture. Try again.", ex);
        }
        var photo = FromLink(link, prompt.Trim(), size)
            ?? throw new ProviderException(ProviderErrorKind.BadResponse, "Pollinations didn't send back a picture. Try again.");
        var bytes = await DownloadAsync(photo, ct);
        CountOne();
        log.LogInformation("Created an AI wallpaper {Id} ({Width}x{Height}, {Bytes} bytes)", photo.Id, size.Width, size.Height, bytes.Length);
        return (photo, bytes);
    }

    /// <summary>
    /// What the picture model gets: a full-bleed wallpaper of great quality, the written-out scene, and the user's own words
    /// (so nothing they asked for is lost). Only what should be there: the model can't follow "no", and naming borders or
    /// frames (even as "no border") is what made it draw white edges and rounded corners.
    /// </summary>
    internal static string Final(string scene, string userWords) =>
        "Full-bleed widescreen wallpaper, the scene runs to every edge of the image, great quality, highly detailed. " +
        $"{scene.TrimEnd('.')}. User's idea: {userWords.TrimEnd('.')}.";

    /// <summary>True for an AI wallpaper's photo ID ("ai-" and its Pollinations media id).</summary>
    public static bool IsAiPhoto(string photoId) => photoId.StartsWith("ai-", StringComparison.Ordinal);

    /// <summary>True for a link to a picture Pollinations keeps (media.pollinations.ai/{id}).</summary>
    public static bool IsMediaLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == MediaHost;

    /// <summary>
    /// The photo record of an AI wallpaper, from its public link: one made here, or one another device of the account
    /// made (it arrives through <c>sync/current</c> or a favorite). Null when the link isn't one of Pollinations'.
    /// </summary>
    public static PhotoCandidate? FromLink(string link, string? prompt, PixelSize? size = null)
    {
        if (!IsMediaLink(link)) return null;
        var mediaId = new Uri(link).AbsolutePath.Trim('/');
        if (mediaId.Length is < 16 or > 128 || !mediaId.All(Uri.IsHexDigit)) return null;
        var clean = $"https://{MediaHost}/{mediaId}";
        return new PhotoCandidate
        {
            Provider = Provider,
            Id = "ai-" + mediaId,
            Width = size?.Width ?? 0,
            Height = size?.Height ?? 0,
            Description = prompt,
            RawUrl = clean,
            ThumbnailUrl = clean,
            PhotoPageUrl = clean,
            PhotographerName = "Pollinations AI",
            PhotographerUrl = "https://pollinations.ai",
        };
    }

    /// <summary>The picture behind an AI wallpaper's public link (no key needed). Pollinations keeps it 30 days after last use.</summary>
    public async Task<byte[]> DownloadAsync(PhotoCandidate photo, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        try
        {
            using var resp = await http.GetAsync(photo.RawUrl, timeout.Token);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                throw new ProviderException(ProviderErrorKind.NoResults, "This AI wallpaper isn't kept online any more.");
            if (!resp.IsSuccessStatusCode)
                throw new ProviderException(ProviderErrorKind.BadResponse, "Pollinations had a problem. Try again in a moment.");
            var bytes = await resp.Content.ReadAsByteArrayAsync(timeout.Token);
            return IsImage(bytes) ? bytes
                : throw new ProviderException(ProviderErrorKind.BadResponse, "Pollinations didn't send back a picture. Try again.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ProviderException(ProviderErrorKind.Network, "Couldn't download the AI wallpaper. Check your connection.", ex);
        }
    }

    /// <summary>The words written out as a full picture description (60 to 90 words), or null when that didn't work.</summary>
    async Task<string?> ExpandAsync(string words, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ExpandTimeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, TextUrl)
            {
                Content = JsonContent.Create(new
                {
                    model = TextModel,
                    max_tokens = 400,
                    messages = new object[]
                    {
                        new { role = "system", content = ExpandInstructions },
                        new { role = "user", content = words },
                    },
                }),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Key);
            using var resp = await http.SendAsync(req, timeout.Token);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogInformation("Couldn't write out the prompt ({Status}); using the words as they are", (int)resp.StatusCode);
                return null;
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(timeout.Token));
            var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim().Trim('"');
            // Long enough to be a description, short enough to fit in the picture link.
            if (text is not { Length: > 20 }) return null;
            return text.Length > 1200 ? text[..1200] : text;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException
            or InvalidOperationException or IndexOutOfRangeException && !ct.IsCancellationRequested)
        {
            log.LogInformation("Couldn't write out the prompt ({Reason}); using the words as they are", ex.Message);
            return null;
        }
    }

    /// <summary>The screen's shape, at most 2048 wide, in steps of 16 (what the models want).</summary>
    internal static PixelSize SizeFor(PixelSize screen)
    {
        double w = Math.Max(screen.Width, 640), h = Math.Max(screen.Height, 360);
        var scale = Math.Min(1, MaxSide / Math.Max(w, h));
        return new PixelSize(Round16(w * scale), Round16(h * scale));
    }

    static int Round16(double v) => Math.Max(16, (int)Math.Round(v / 16) * 16);

    public static bool IsImage(byte[] b) => b.Length > 1024 && (IsJpeg(b) || IsPng(b));
    public static bool IsJpeg(byte[] b) => b.Length > 2 && b[0] == 0xFF && b[1] == 0xD8;
    public static bool IsPng(byte[] b) => b.Length > 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

    // ---- How many were made today (this device, local day) ----

    string CountFile => Path.Combine(paths.DataRoot, "created.json");
    readonly Lock _countLock = new();

    List<DateTimeOffset> MadeToday()
    {
        lock (_countLock)
        {
            try
            {
                var all = File.Exists(CountFile) ? JsonSerializer.Deserialize<List<DateTimeOffset>>(File.ReadAllText(CountFile)) ?? [] : [];
                var today = DateTime.Now.Date;
                return all.Where(t => t.LocalDateTime.Date == today).ToList();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return [];
            }
        }
    }

    void CountOne()
    {
        var made = MadeToday();
        made.Add(DateTimeOffset.Now);
        lock (_countLock)
        {
            try { File.WriteAllText(CountFile, JsonSerializer.Serialize(made)); }
            catch (IOException) { }
        }
    }

    /// <summary>What Pollinations says went wrong, in words for the user.</summary>
    async Task<ProviderException> ErrorAsync(HttpResponseMessage resp)
    {
        string? reason = null;
        try
        {
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("error", out var e))
                reason = e.ValueKind == JsonValueKind.String ? e.GetString() : e.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException) { }
        log.LogWarning("Pollinations said {Status}: {Reason}", (int)resp.StatusCode, reason);
        return resp.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.PaymentRequired =>
                new ProviderException(ProviderErrorKind.RateLimited, "Creating is resting for now. Try again later."),
            HttpStatusCode.TooManyRequests => new ProviderException(ProviderErrorKind.RateLimited,
                "That was a lot of pictures in a short time. Wait a minute and try again."),
            HttpStatusCode.BadRequest => new ProviderException(ProviderErrorKind.BadResponse,
                "Pollinations couldn't make that. Try other words."),
            _ => new ProviderException(ProviderErrorKind.BadResponse, "Pollinations had a problem. Try again in a moment."),
        };
    }
}
