using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core;
using Oriel.Core.Auth;
using Oriel.UI.Services;

namespace Oriel.UI.ViewModels;

/// <summary>The sign-in screen: opens the website's login page, then finishes when its oriel:// link arrives.</summary>
public sealed partial class SignInViewModel(AuthService auth) : ObservableObject
{
    const string LoginUrl = "https://oriel.akbarjon.uz/login";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(ButtonText))]
    public partial bool IsWorking { get; set; }
    public bool IsIdle => !IsWorking;
    public string ButtonText => IsWorking ? "Continuing with Google…" : "Continue with Google";

    [ObservableProperty] public partial bool IsWaiting { get; set; }

    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial string ErrorMessage { get; set; } = "";

    [RelayCommand]
    void OpenSupport() => Launcher.OpenUrl(new Uri(OrielLinks.Support));

    [RelayCommand]
    void OpenBrowser() => OpenLogin(LoginUrl);

    /// <summary>The website's sign-in with the email form ready, for accounts without Google.</summary>
    [RelayCommand]
    void OpenEmail() => OpenLogin(LoginUrl + "?method=email");

    void OpenLogin(string url)
    {
        HasError = false;
        IsWaiting = true;
        if (!Launcher.OpenUrl(new Uri(url)))
        {
            IsWaiting = false;
            Fail("Couldn't open your browser. Go to oriel.akbarjon.uz/login yourself.");
        }
    }

    /// <summary>The website opened Oriel with a Google token: turn it into a signed-in session.</summary>
    public Task CompleteAsync(string googleIdToken) => CompleteAsync(() => auth.SignInAsync(googleIdToken));

    /// <summary>The website opened Oriel with an email and password account's session: take it over.</summary>
    public Task CompleteWithSessionAsync(string refreshToken) => CompleteAsync(() => auth.SignInWithSessionAsync(refreshToken));

    async Task CompleteAsync(Func<Task> signIn)
    {
        if (IsWorking || auth.IsSignedIn) return;
        IsWorking = true;
        HasError = false;
        try
        {
            await signIn();
            IsWaiting = false;
        }
        catch (FirebaseException ex)
        {
            Fail(ex.Failure switch
            {
                FirebaseFailure.Network => ex.Message,
                FirebaseFailure.Rejected => "That sign-in expired or isn't valid. Sign in again from the website.",
                _ => "Couldn't sign you in right now. Please try again.",
            });
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>The link was opened but carried nothing usable.</summary>
    public void Invalid() => Fail("That sign-in link wasn't valid. Sign in again from the website.");

    void Fail(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}
