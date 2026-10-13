using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Oriel.Core.Auth;

namespace Oriel.UI.ViewModels;

/// <summary>The window: a side menu, the current page, and the sign-in screen while signed out.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    readonly IServiceProvider _services;
    readonly AuthService _auth;

    public MainViewModel(IServiceProvider services, AuthService auth, SignInViewModel signIn, OnboardingViewModel onboarding, SharedPhotoViewModel shared)
    {
        _services = services;
        _auth = auth;
        SignIn = signIn;
        Onboarding = onboarding;
        Onboarding.Finished += OnOnboardingFinished;
        Shared = shared;
        Shared.Changed += OnSharedChanged;
        _auth.Changed += OnAuthChanged;
        _auth.SigningOutChanged += OnSigningOut;
        UpdateSignInGate();
        Navigate("home");
    }

    public SignInViewModel SignIn { get; }
    public OnboardingViewModel Onboarding { get; }
    public SharedPhotoViewModel Shared { get; }

    [ObservableProperty] public partial object? CurrentPage { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedOut), nameof(ShowApp))]
    public partial bool IsSignedIn { get; set; }

    /// <summary>Signing out: the window waits, nothing can be clicked.</summary>
    [ObservableProperty] public partial bool IsSigningOut { get; set; }

    void OnSigningOut(object? sender, EventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => IsSigningOut = _auth.IsSigningOut);
    public bool IsSignedOut => !IsSignedIn;

    /// <summary>The first-run tour covers the app until it is finished or skipped.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowApp))]
    public partial bool ShowOnboarding { get; set; }
    public bool ShowApp => IsSignedIn && !ShowOnboarding;

    /// <summary>A photo a friend shared with a link waits behind sign-in and the tour, then shows over the app.</summary>
    [ObservableProperty] public partial bool ShowShared { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome), nameof(IsSearch), nameof(IsHistory), nameof(IsFavorites), nameof(IsFriends), nameof(IsSettings))]
    public partial string Section { get; set; } = "home";
    public bool IsHome => Section == "home";
    public bool IsSearch => Section == "search";
    public bool IsHistory => Section == "history";
    public bool IsFavorites => Section == "favorites";
    public bool IsFriends => Section == "friends";
    public bool IsSettings => Section == "settings";

    void OnAuthChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(UpdateSignInGate);

    /// <summary>Signed out: only the sign-in screen is reachable. Signing in lands on Home.</summary>
    void UpdateSignInGate()
    {
        var wasSignedIn = IsSignedIn;
        IsSignedIn = _auth.IsSignedIn;
        ShowOnboarding = IsSignedIn && Onboarding.ShouldShow();
        ShowShared = ShowApp && Shared.IsOpen;
        if (IsSignedIn && !wasSignedIn) Navigate("home");
    }

    void OnSharedChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(UpdateSignInGate);

    void OnOnboardingFinished(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        ShowOnboarding = false;
        ShowShared = Shared.IsOpen;
        Navigate("home");
    });

    /// <summary>Clicking the current item again leaves a sub page (Interests) and returns to its main page.</summary>
    [RelayCommand]
    public void Navigate(string section)
    {
        if (section == Section && CurrentPage is not null)
        {
            if (CurrentPage is CollectionsViewModel search) search.IsMixOpen = false;
            return;
        }

        var previous = CurrentPage;
        Section = section;
        CurrentPage = section switch
        {
            "search" => _services.GetRequiredService<CollectionsViewModel>(),
            "history" => LoadHistory(favorites: false),
            "favorites" => LoadHistory(favorites: true),
            "settings" => _services.GetRequiredService<SettingsViewModel>(), // new each time: it listens for updates
            "friends" => _services.GetRequiredService<FriendsViewModel>(), // new each time: it listens for follow changes
            _ => _services.GetRequiredService<HomeViewModel>(),
        };
        if (previous is IDisposable listening && previous is SettingsViewModel or FriendsViewModel) listening.Dispose();
    }

    HistoryViewModel LoadHistory(bool favorites)
    {
        var history = _services.GetRequiredService<HistoryViewModel>();
        _ = history.LoadAsync(favorites);
        return history;
    }

    public void Dispose()
    {
        _auth.Changed -= OnAuthChanged;
        _auth.SigningOutChanged -= OnSigningOut;
        Onboarding.Finished -= OnOnboardingFinished;
        Shared.Changed -= OnSharedChanged;
        if (CurrentPage is SettingsViewModel or FriendsViewModel) (CurrentPage as IDisposable)?.Dispose();
    }
}
