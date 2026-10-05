namespace Oriel.Core.Auth;

/// <summary>The signed-in Google account, as kept on this PC.</summary>
public sealed class Account
{
    public string Uid { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PhotoUrl { get; set; } = "";
    /// <summary>Firebase refresh token: lets the app get fresh ID tokens without asking again.</summary>
    public string RefreshToken { get; set; } = "";
    /// <summary>This install's document in <c>users/{uid}/sessions</c>.</summary>
    public string SessionId { get; set; } = "";
    public DateTimeOffset SignedInAt { get; set; }
}

public interface IAuthState
{
    bool IsSignedIn { get; }
    /// <summary>Raised after signing in or out.</summary>
    event EventHandler? Changed;
}
