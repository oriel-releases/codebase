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
    ILogger<FollowService> log)
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

    string MyName => ShareLink.CleanName(auth.Current?.DisplayName) is { Length: > 0 } n ? n : "A friend";

    async Task<FirebaseTokens> TokensAsync(CancellationToken ct) =>
        await auth.GetTokensAsync(ct) ?? throw new FollowException("Sign in to Oriel first.");

    // ---------------------------------------------------------------- sharing your wallpaper

    /// <summary>Starts sharing: a new code friends can follow. Returns it.</summary>
    public async Task<string> StartSharingAsync(CancellationToken ct = default)
    {
        var tokens = await TokensAsync(ct);
        for (var attempt = 0; ; attempt++)
        {
            var code = NewCode();
            try
            {
                await firebase.CreateFollowCodeAsync(tokens, code, MyName, ct);
                Update(s => s.MyFollowCode = code);
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

    /// <summary>Stops sharing: the code stops working and everyone following stops seeing your wallpaper.</summary>
    public async Task StopSharingAsync(CancellationToken ct = default)
    {
        var tokens = await TokensAsync(ct);
        try
        {
            if (store.Load().MyFollowCode is { } code) await firebase.DeleteFollowCodeAsync(tokens, code, ct);
            foreach (var follower in await firebase.ListFollowersAsync(tokens, ct))
                await firebase.RemoveFollowerAsync(tokens, tokens.Uid, follower, ct);
        }
        catch (FirebaseException ex)
        {
            throw new FollowException(ex.Failure == FirebaseFailure.Network ? "You seem to be offline. Try again." : "Couldn't stop sharing. Try again.");
        }
        Update(s => s.MyFollowCode = null);
    }

    /// <summary>How many people follow you; null when it can't be read right now.</summary>
    public async Task<int?> CountFollowersAsync(CancellationToken ct = default)
    {
        try
        {
            return (await firebase.ListFollowersAsync(await TokensAsync(ct), ct)).Count;
        }
        catch (Exception ex) when (ex is FirebaseException or FollowException)
        {
            return null;
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
            await firebase.AddFollowerAsync(tokens, owner.Uid, code, MyName, ct);
            var name = owner.Name.Length > 0 ? owner.Name : "your friend";
            Update(s =>
            {
                s.FollowUid = owner.Uid;
                s.FollowName = name;
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
        });
        var state = store.LoadState();
        state.FollowedPhotoId = null;
        state.FollowProblem = null;
        store.SaveState(state);
    }

    /// <summary>Applies the friend's wallpaper when it changed. Called by the scheduler every few minutes while following.</summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        var settings = store.Load();
        if (settings.FollowUid is not { } friend || !await _check.WaitAsync(0, ct)) return;
        try
        {
            if (await auth.GetTokensAsync(ct) is not { } tokens) return;
            string? photoId;
            try
            {
                photoId = await firebase.GetCurrentPhotoIdAsync(tokens, friend, ct);
            }
            catch (FirebaseException ex) when (ex.Failure == FirebaseFailure.Rejected)
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
            var photo = await wallpapers.GetSharedPhotoAsync(photoId, ct);
            var result = await wallpapers.ApplyPhotoAsync(photo, $"from {settings.FollowName}", ct);
            if (result.Status != ApplyStatus.Applied) return;
            state = store.LoadState();
            state.FollowedPhotoId = photoId;
            state.FollowProblem = null;
            store.SaveState(state);
            log.LogInformation("Applied the followed friend's wallpaper {Photo}", photoId);
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
