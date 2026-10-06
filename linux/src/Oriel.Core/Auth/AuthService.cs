using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Auth;

/// <summary>
/// The app's sign-in. Signing in happens on the website; it hands over a Google ID token that this turns into
/// the app's own Firebase session (a refresh token kept in the system keyring on this PC) plus a session document in
/// <c>users/{uid}/sessions</c>. After that the app stays signed in, even offline. It signs out only when
/// Google rejects the account or the session document was deleted (revoked from somewhere else).
/// </summary>
public sealed class AuthService(IAccountStore store, FirebaseClient firebase, ILogger<AuthService> log) : IAuthState, IDisposable
{
    static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    readonly SemaphoreSlim _gate = new(1, 1);
    Account? _account = store.Load();
    FirebaseTokens? _tokens;
    Timer? _timer;

    /// <summary>Raised on whatever thread changed the state, after signing in or out.</summary>
    public event EventHandler? Changed;

    public bool IsSignedIn => _account is not null;
    public Account? Current => _account;

    /// <summary>Checks the account now and then every few hours, without ever blocking the app.</summary>
    public void Start() => _timer ??= new Timer(_ => _ = ValidateAsync(), null, TimeSpan.FromSeconds(5), CheckInterval);

    public async Task SignInAsync(string googleIdToken, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var profile = await firebase.SignInWithGoogleAsync(googleIdToken, ct);
            var account = new Account
            {
                Uid = profile.Uid,
                Email = profile.Email,
                DisplayName = profile.DisplayName,
                PhotoUrl = profile.PhotoUrl,
                RefreshToken = profile.Tokens.RefreshToken,
                SessionId = Guid.NewGuid().ToString("N"),
                SignedInAt = DateTimeOffset.UtcNow,
            };
            // Record this install first: if that fails, nothing is saved and the person can just try again.
            await firebase.SetSessionAsync(profile.Tokens, account.SessionId,
                new SessionInfo("linux", Environment.MachineName, AppVersion), account.SignedInAt, ct);
            store.Save(account);
            _account = account;
            _tokens = profile.Tokens;
        }
        finally
        {
            _gate.Release();
        }
        log.LogInformation("Signed in");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignOutAsync()
    {
        var account = _account;
        if (account is null) return;
        await _gate.WaitAsync();
        try
        {
            try
            {
                var tokens = await EnsureTokensAsync(account, default);
                await firebase.DeleteSessionAsync(tokens, account.SessionId);
            }
            catch (FirebaseException ex)
            {
                // Offline or already revoked: signing out locally is what matters.
                log.LogInformation("Couldn't remove the session document: {Reason}", ex.Message);
            }
            Forget();
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Refreshes the token and records activity. Signs out if the account or this session no longer exists.</summary>
    public async Task ValidateAsync()
    {
        var account = _account;
        if (account is null || !await _gate.WaitAsync(0)) return;
        var signedOut = false;
        try
        {
            var tokens = await EnsureTokensAsync(account, default);
            if (!await firebase.TouchSessionAsync(tokens, account.SessionId, AppVersion))
            {
                log.LogInformation("This session was removed; signing out");
                Forget();
                signedOut = true;
            }
        }
        catch (FirebaseException ex) when (ex.Failure == FirebaseFailure.Rejected)
        {
            log.LogInformation("Google no longer accepts this account ({Reason}); signing out", ex.Message);
            Forget();
            signedOut = true;
        }
        catch (FirebaseException ex)
        {
            // Offline or Google is down: stay signed in and check again later.
            log.LogInformation("Couldn't check the account: {Reason}", ex.Message);
        }
        finally
        {
            _gate.Release();
        }
        if (signedOut) Changed?.Invoke(this, EventArgs.Empty);
    }

    async Task<FirebaseTokens> EnsureTokensAsync(Account account, CancellationToken ct)
    {
        if (_tokens is { } t && t.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return t;
        var fresh = await firebase.RefreshAsync(account.RefreshToken, ct);
        if (fresh.RefreshToken != account.RefreshToken)
        {
            account.RefreshToken = fresh.RefreshToken;
            store.Save(account);
        }
        return _tokens = fresh;
    }

    void Forget()
    {
        store.Clear();
        _account = null;
        _tokens = null;
    }

    static string AppVersion =>
        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version?.ToString(3) ?? "";

    public void Dispose()
    {
        _timer?.Dispose();
        _gate.Dispose();
    }
}
