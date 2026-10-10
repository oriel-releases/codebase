using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core;
using Oriel.Core.Auth;
using Windows.System;

namespace Oriel.UI.ViewModels;

/// <summary>The sign-in screen: opens the website's login page, then finishes when its oriel:// link arrives.</summary>
public sealed partial class SignInViewModel(AuthService auth) : ObservableObject
{
    const string LoginUrl = "https://oriel.akbarjon.uz/login";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    public partial bool IsWorking { get; set; }
    public bool IsIdle => !IsWorking;
    public string ButtonText => IsWorking ? "Continuing with Google…" : "Continue with Google";

    [ObservableProperty] public partial bool IsWaiting { get; set; }

    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial string ErrorMessage { get; set; } = "";

    [RelayCommand]
    async Task OpenSupport() => await Launcher.LaunchUriAsync(new Uri(OrielLinks.Support));

    [RelayCommand]
    async Task OpenBrowser()
    {
        HasError = false;
        IsWaiting = true;
        if (!await Launcher.LaunchUriAsync(new Uri(LoginUrl)))
        {
            IsWaiting = false;
            Fail("Couldn't open your browser. Go to oriel.akbarjon.uz/login yourself.");
        }
    }

    /// <summary>The website opened Oriel with a Google token: turn it into a signed-in session.</summary>
    public async Task CompleteAsync(string googleIdToken)
    {
        if (IsWorking || auth.IsSignedIn) return;
        IsWorking = true;
        HasError = false;
        try
        {
            await auth.SignInAsync(googleIdToken);
            IsWaiting = false;
        }
        catch (FirebaseException ex)
        {
            Fail(ex.Failure switch
            {
                FirebaseFailure.Network => ex.Message,
                FirebaseFailure.Rejected => "That sign-in expired or isn't valid. Sign in again from the website.",
                _ => "Google couldn't sign you in right now. Please try again.",
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
