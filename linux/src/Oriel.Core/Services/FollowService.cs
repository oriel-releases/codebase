using System.Security.Cryptography;
using Oriel.Core.Auth;
using Oriel.Core.Models;
using Oriel.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Services;

/// <summary>A friendly reason why following didn't work, ready to show.</summary>
public sealed class FollowException(string message) : Exception(message);

/// <summary>
/// Follow a friend: your desktop shows whatever wallpaper they set. The friend shares a short code; following it
/// writes you into their <c>followers</c>, which is what lets you read their <c>sync/current</c> (firestore.rules).
/// While you follow, your own schedule is off. They can stop sharing at any time, which ends it for everyone.
/// </summary>
public sealed class FollowService(AuthService auth, FirebaseClient firebase, ISettingsStore store, WallpaperService wallpapers,
    ILogger<FollowService> log, Storage.IHistoryStore? history = null)
{
    /// <summary>No 0/O, 1/I: codes get read aloud and typed by hand.</summary>
    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int CodeLength = 8;

    readonly SemaphoreSlim _check = new(1, 1);

    /// <summary>Raised after following, sharing or the friend's photo changed, so the UI can refresh.</summary>
    public event EventHandler? Changed;

    public static string NewCode() =>
        string.Concat(Enumerable.Range(0, CodeLength).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]));

    /// <summary>"k7mq-2xpa " becomes "K7MQ2XPA"; null when it can't be a code.</summary>
    public static string? Normalize(string? input)
    {
        if (input is null) return null;
        var code = string.Concat(input.ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-'));
        return code.Length == CodeLength && code.All(Alphabet.Contains) ? code : null;
    }

    /// <summary>"K7MQ2XPA" shown as "K7MQ-2XPA".</summary>
    public static string Format(string code) => code.Length == CodeLength ? $"{code[..4]}-{code[4..]}" : code;

    /// <summary>
    /// What someone is typing, tidied as they type: capitals, only letters and numbers, at most a code long, and the
    /// dash after the fourth ("k7mq2x" becomes "K7MQ-2X").
    /// </summary>
    public static string FormatInput(string? input)
    {
        var chars = string.Concat((input ?? "").ToUpperInvariant().Where(char.IsAsciiLetterOrDigit));
        if (chars.Length > CodeLength) chars = chars[..CodeLength];
        return chars.Length > 4 ? $"{chars[..4]}-{chars[4..]}" : chars;
    }

    string MyName => ShareLink.CleanName(auth.Current?.DisplayName) is { Length: > 0 } n ? n : "A friend";

    async Task<FirebaseTokens> TokensAsync(CancellationToken ct) =>
        await auth.GetTokensAsync(ct) ?? throw new FollowException("Sign in to Oriel first.");

    // ---------------------------------------------------------------- sharing your wallpaper

    /// <summary>Your follow code, made the first time you share and the same one after that.</summary>
    public async Task<string> EnsureCodeAsync(CancellationToken ct = default) =>
        store.Load().MyFollowCode ?? await StartSharingAsync(ct);

    /// <summary>Starts sharing: a new code friends can follow. Returns it.</summary>
    public async Task<string> StartSharingAsync(CancellationToken ct = default)
    {
        var tokens = await TokensAsync(ct);
        for (var attempt = 0; ; attempt++)
        {
            var code = NewCode();
            try
            {
                await firebase.CreateFollowCodeAsync(tokens, code, MyName, ShareLink.Picture(auth.Current?.PhotoUrl), ct);
                Update(s => s.MyFollowCode = code);
                // The follow page shows the wallpaper you have now; after this it follows every change (SyncPublisher).
                if (history?.List().FirstOrDefault() is { } current) await ShowOnFollowPageAsync(tokens, code, current, ct);
                return code;
            }
            catch (FirebaseException ex) when (ex.Failure != FirebaseFailure.Network && attempt < 3)
            {
                log.LogInformation("Follow code taken or refused, trying another: {Reason}", ex.Message);
            }
            catch (FirebaseException ex)
            {
                throw new FollowException(ex.Failure == FirebaseFailure.Network ? "You seem to be offline. Try again." : "Couldn't create a code. Try again.");
            }
        }
    }

    /// <summary>Puts this wallpaper on your follow page. Best effort: the page just shows an older one if it fails.</summary>
    public async Task ShowOnFollowPageAsync(FirebaseTokens tokens, string code, HistoryEntry entry, CancellationToken ct = default)
    {
        if (ShareLink.Photo(entry) is not { } photo) return;
        try
        {
            await firebase.SetFollowCodePhotoAsync(tokens, code, photo, ct);
        }
        catch (FirebaseException ex)
        {
            log.LogInformation("Couldn't put the wallpaper on the follow page: {Reason}", ex.Message);
        }
    }

    /// <summary>Stops sharing: the code stops working and everyone following stops seeing your wallpaper.</summary>
    public async Task StopSharingAsync(CancellationToken ct = default)
    {
        var tokens = await TokensAsync(ct);
        try
        {
            if (store.Load().MyFollowCode is { } code) await firebase.DeleteFollowCodeAsync(tokens, code, ct);
            foreach (var follower in await firebase.ListFollowersAsync(tokens, ct))
                await firebase.RemoveFollowerAsync(tokens, tokens.Uid, follower.Uid, ct);
        }
        catch (FirebaseException ex)
        {
            throw new FollowException(ex.Failure == FirebaseFailure.Network ? "You seem to be offline. Try again." : "Couldn't stop sharing. Try again.");
        }
        Update(s => s.MyFollowCode = null);
    }

    /// <summary>The people following you, newest first; null when it can't be read right now.</summary>
    public async Task<IReadOnlyList<Follower>?> ListFollowersAsync(CancellationToken ct = default)
    {
        try
        {
            return await firebase.ListFollowersAsync(await TokensAsync(ct), ct);
        }
        catch (Exception ex) when (ex is FirebaseException or FollowException)
        {
            return null;
        }
    }

    /// <summary>Stops one person seeing your wallpaper.</summary>
    public async Task RemoveFollowerAsync(string followerUid, CancellationToken ct = default)
    {
        var tokens = await TokensAsync(ct);
        try
        {
            await firebase.RemoveFollowerAsync(tokens, tokens.Uid, followerUid, ct);
        }
        catch (FirebaseException ex)
        {
            throw new FollowException(ex.Failure == FirebaseFailure.Network ? "You seem to be offline. Try again." : "Couldn't remove them. Try again.");
        }
    }

    // ---------------------------------------------------------------- following a friend

    /// <summary>Follows the friend whose code this is. Returns their first name.</summary>
    public async Task<string> FollowAsync(string input, CancellationToken ct = default)
    {
        var code = Normalize(input) ?? throw new FollowException("A code has 8 letters and numbers, like K7MQ-2XPA.");
        var tokens = await TokensAsync(ct);
        try
        {
            var owner = await firebase.GetFollowCodeAsync(tokens, code, ct) ?? throw new FollowException("No one has that code. Check it and try again.");
            if (owner.Uid == tokens.Uid) throw new FollowException("That's your own code. Send it to a friend instead.");
            var previous = store.Load().FollowUid;
            if (previous is not null && previous != owner.Uid)
            {
                try
                {
                    await firebase.RemoveFollowerAsync(tokens, previous, tokens.Uid, ct);
                }
                catch (FirebaseException ex)
                {
                    log.LogInformation("Couldn't leave the previous friend: {Reason}", ex.Message);
                }
            }
            await firebase.AddFollowerAsync(tokens, owner.Uid, code, MyName, ct);
            var name = owner.Name.Length > 0 ? owner.Name : "your friend";
            Update(s =>
            {
                s.FollowUid = owner.Uid;
                s.FollowName = name;
                s.FollowPicture = owner.Picture.Length > 0 ? owner.Picture : null;
            });
            var state = store.LoadState();
            state.FollowedPhotoId = null;
            state.FollowProblem = null;
            store.SaveState(state);
            await CheckAsync(ct);
            return name;
        }
        catch (FirebaseException ex)
        {
            throw new FollowException(ex.Failure == FirebaseFailure.Network ? "You seem to be offline. Try again." : "Couldn't follow with that code. Try again.");
        }
    }

    /// <summary>A follow link that is being followed now (or waits for sign-in), for the page to show.</summary>
    public string? LinkCode { get; private set; }
    /// <summary>Why the last follow link didn't work, for the page to show; null when it did (or there was none).</summary>
    public string? LinkError { get; private set; }

    /// <summary>
    /// Someone clicked "Follow" on a friend's follow page. Follows now if signed in; otherwise keeps the code and
    /// follows as soon as the account signs in (see <see cref="FollowWaitingLinkAsync"/>).
    /// </summary>
    public async Task FollowFromLinkAsync(string code, CancellationToken ct = default)
    {
        LinkCode = code;
        LinkError = null;
        Changed?.Invoke(this, EventArgs.Empty);
        if (!auth.IsSignedIn) return;
        await FollowWaitingLinkAsync(ct);
    }

    /// <summary>Follows the link that was waiting for sign-in, if there is one.</summary>
    public async Task FollowWaitingLinkAsync(CancellationToken ct = default)
    {
        if (LinkCode is not { } code || !auth.IsSignedIn) return;
        try
        {
            await FollowAsync(code, ct);
            LinkError = null;
        }
        catch (FollowException ex)
        {
            LinkError = ex.Message;
        }
        finally
        {
            LinkCode = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The page showed the link's error: forget it.</summary>
    public void ClearLinkError() => LinkError = null;

    /// <summary>Stops following. Your own schedule takes over again.</summary>
    public async Task UnfollowAsync(CancellationToken ct = default)
    {
        var settings = store.Load();
        if (settings.FollowUid is { } friend)
        {
            try
            {
                if (await auth.GetTokensAsync(ct) is { } tokens) await firebase.RemoveFollowerAsync(tokens, friend, tokens.Uid, ct);
            }
            catch (FirebaseException ex)
            {
                // The friend may have stopped sharing already; leaving locally is what matters.
                log.LogInformation("Couldn't remove the follow: {Reason}", ex.Message);
            }
        }
        Update(s =>
        {
            s.FollowUid = null;
            s.FollowName = null;
            s.FollowPicture = null;
        });
        var state = store.LoadState();
        state.FollowedPhotoId = null;
        state.FollowProblem = null;
        store.SaveState(state);
    }

    /// <summary>
    /// Shows the wallpaper the account's other devices set last (their <c>sync/current</c>), or the followed friend's,
    /// when it's new here. Every device of an account shows the same photo: a change anywhere is published, and the
    /// others pick it up here. Called by the scheduler every few minutes and before a scheduled change. Locked (paused)
    /// only stops the schedule: a photo set by hand on another device still shows here.
    /// </summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        var settings = store.Load();
        var following = settings.FollowUid is not null;
        if (!await _check.WaitAsync(0, ct)) return;
        try
        {
            if (await auth.GetTokensAsync(ct) is not { } tokens) return;
            var owner = settings.FollowUid ?? tokens.Uid;
            string? photoId;
            try
            {
                photoId = await firebase.GetCurrentPhotoIdAsync(tokens, owner, ct);
            }
            catch (FirebaseException ex) when (ex.Failure == FirebaseFailure.Rejected && following)
            {
                SetProblem($"{settings.FollowName ?? "Your friend"} stopped sharing their wallpaper.");
                return;
            }
            var state = store.LoadState();
            if (photoId is null || photoId == state.FollowedPhotoId)
            {
                if (state.FollowProblem is not null) SetProblem(null);
                return;
            }
            // Already on this device (it published it, or showed it before): nothing to do.
            if (history?.List().FirstOrDefault()?.PhotoId == photoId)
            {
                state.FollowedPhotoId = photoId;
                store.SaveState(state);
                return;
            }
            wallpapers.ReportPhase(ApplyPhase.Receiving);
            PhotoCandidate photo;
            try
            {
                photo = await wallpapers.GetSharedPhotoAsync(photoId, ct);
            }
            catch
            {
                wallpapers.FinishPhase();
                throw;
            }
            // A friend's photo is passed on to this account's other devices; the account's own isn't sent back.
            var result = await wallpapers.ApplyPhotoAsync(photo, following ? $"from {settings.FollowName}" : "synced", ct, publish: following);
            if (result.Status != ApplyStatus.Applied) return;
            state = store.LoadState();
            state.FollowedPhotoId = photoId;
            state.FollowProblem = null;
            store.SaveState(state);
            log.LogInformation(following ? "Applied the followed friend's wallpaper {Photo}" : "Applied the wallpaper set on another device {Photo}", photoId);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is FirebaseException or Providers.ProviderException)
        {
            log.LogInformation("Couldn't check the followed wallpaper: {Reason}", ex.Message);
        }
        finally
        {
            _check.Release();
        }
    }

    void SetProblem(string? problem)
    {
        var state = store.LoadState();
        if (state.FollowProblem == problem) return;
        state.FollowProblem = problem;
        store.SaveState(state);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void Update(Action<OrielSettings> change)
    {
        var s = store.Load();
        change(s);
        store.Save(s);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
