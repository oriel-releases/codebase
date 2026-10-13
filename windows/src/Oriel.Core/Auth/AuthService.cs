using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Oriel.Core.Auth;

/// <summary>
/// The app's sign-in. Signing in happens on the website; it hands over a Google ID token (or, for an account
/// without Google, the Firebase session the website made with the email and password) that this turns into
/// the app's own Firebase session (a refresh token kept encrypted on this PC) plus a session document in
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

    public Task SignInAsync(string googleIdToken, CancellationToken ct = default) =>
        SignInAsync(c => firebase.SignInWithGoogleAsync(googleIdToken, c), ct);

    /// <summary>Signs in with the Firebase session the website made for an email and password account.</summary>
    public Task SignInWithSessionAsync(string refreshToken, CancellationToken ct = default) =>
        SignInAsync(c => firebase.SignInWithRefreshTokenAsync(refreshToken, c), ct);

    async Task SignInAsync(Func<CancellationToken, Task<FirebaseProfile>> signIn, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var profile = await signIn(ct);
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
                new SessionInfo("windows", Environment.MachineName, AppVersion), account.SignedInAt, ct);
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

    /// <summary>Fresh Firebase credentials for the signed-in account, or null when signed out.</summary>
    public async Task<FirebaseTokens?> GetTokensAsync(CancellationToken ct = default)
    {
        var account = _account;
        if (account is null) return null;
        await _gate.WaitAsync(ct);
        try
        {
            return await EnsureTokensAsync(account, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>True while signing out (removing this session takes a moment): the app covers itself until it's done.</summary>
    public bool IsSigningOut { get; private set; }
    /// <summary>Raised when <see cref="IsSigningOut"/> changes, on whatever thread is signing out.</summary>
    public event EventHandler? SigningOutChanged;

    public async Task SignOutAsync()
    {
        var account = _account;
        if (account is null || IsSigningOut) return;
        IsSigningOut = true;
        SigningOutChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await _gate.WaitAsync();
            try
            {
                // Never longer than a few seconds: on a bad connection, signing out here is what matters.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try
                {
                    var tokens = await EnsureTokensAsync(account, timeout.Token);
                    await firebase.DeleteSessionAsync(tokens, account.SessionId, timeout.Token);
                }
                catch (Exception ex) when (ex is FirebaseException or OperationCanceledException)
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
        finally
        {
            IsSigningOut = false;
            SigningOutChanged?.Invoke(this, EventArgs.Empty);
        }
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
