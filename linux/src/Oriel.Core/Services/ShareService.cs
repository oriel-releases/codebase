using Oriel.Core.Auth;
using Oriel.Core.Models;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

/// <summary>A friendly reason why a share link couldn't be made, ready to show.</summary>
public sealed class ShareException(string message) : Exception(message);

/// <summary>
/// Share links for wallpapers: each is a small <c>shares/{id}</c> document (the photo, and who sent it) and a short
/// link to it, <c>oriel.akbarjon.uz/share/AB3K9QXZ</c>. One per photo for as long as the app runs.
/// </summary>
public sealed class ShareService(AuthService auth, FirebaseClient firebase, ILogger<ShareService> log)
{
    readonly Dictionary<string, string> _links = [];
    readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>True for photos that can be shared (Unsplash photos).</summary>
    public static bool CanShare(HistoryEntry entry) => ShareLink.Photo(entry) is not null;

    /// <summary>The link for this photo, made the first time it's shared.</summary>
    public async Task<string> LinkAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        var photo = ShareLink.Photo(entry) ?? throw new ShareException("Only photos from Unsplash can be shared.");
        await _gate.WaitAsync(ct);
        try
        {
            if (_links.TryGetValue(photo.Id, out var known)) return known;
            var tokens = await auth.GetTokensAsync(ct) ?? throw new ShareException("Sign in to Oriel first.");
            var from = ShareLink.CleanName(auth.Current?.DisplayName);
            var picture = ShareLink.Picture(auth.Current?.PhotoUrl);
            for (var attempt = 0; ; attempt++)
            {
                var id = FollowService.NewCode();
                try
                {
                    await firebase.CreateShareAsync(tokens, id, from, picture, photo, ct);
                    return _links[photo.Id] = ShareLink.ForShare(id);
                }
                catch (FirebaseException ex) when (ex.Failure != FirebaseFailure.Network && attempt < 3)
                {
                    log.LogInformation("Share id taken or refused, trying another: {Reason}", ex.Message);
                }
                catch (FirebaseException ex)
                {
                    throw new ShareException(ex.Failure == FirebaseFailure.Network ? "You seem to be offline. Try again." : "Couldn't make a link. Try again.");
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
