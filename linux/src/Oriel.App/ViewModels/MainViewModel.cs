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

    public MainViewModel(IServiceProvider services, AuthService auth, SignInViewModel signIn)
    {
        _services = services;
        _auth = auth;
        SignIn = signIn;
        _auth.Changed += OnAuthChanged;
        UpdateSignInGate();
        Navigate("home");
    }

    public SignInViewModel SignIn { get; }

    [ObservableProperty] public partial object? CurrentPage { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedOut))]
    public partial bool IsSignedIn { get; set; }
    public bool IsSignedOut => !IsSignedIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome), nameof(IsSearch), nameof(IsHistory), nameof(IsFavorites), nameof(IsSettings))]
    public partial string Section { get; set; } = "home";
    public bool IsHome => Section == "home";
    public bool IsSearch => Section == "search";
    public bool IsHistory => Section == "history";
    public bool IsFavorites => Section == "favorites";
    public bool IsSettings => Section == "settings";

    void OnAuthChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(UpdateSignInGate);

    /// <summary>Signed out: only the sign-in screen is reachable. Signing in lands on Home.</summary>
    void UpdateSignInGate()
    {
        var wasSignedIn = IsSignedIn;
        IsSignedIn = _auth.IsSignedIn;
        if (IsSignedIn && !wasSignedIn) Navigate("home");
    }

    /// <summary>Clicking the current item again leaves a sub page (Daily mix) and returns to its main page.</summary>
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
            _ => _services.GetRequiredService<HomeViewModel>(),
        };
        if (previous is SettingsViewModel settings) settings.Dispose();
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
        if (CurrentPage is SettingsViewModel settings) settings.Dispose();
    }
}
