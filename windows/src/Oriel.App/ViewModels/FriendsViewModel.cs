using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core.Services;
using Oriel.Core.Settings;

namespace Oriel.UI.ViewModels;

/// <summary>
/// The Friends part of Settings: share your wallpaper with a code, and follow a friend's. Lives as long as the
/// Settings page (Detach when it closes).
/// </summary>
public sealed partial class FriendsViewModel : ObservableObject, IDisposable
{
    readonly FollowService _follow;
    readonly ISettingsStore _settings;

    public FriendsViewModel(FollowService follow, ISettingsStore settings)
    {
        _follow = follow;
        _settings = settings;
        _follow.Changed += OnChanged;
        Load();
        _ = CountFollowersAsync();
    }

    public void Detach() => _follow.Changed -= OnChanged;
    public void Dispose() => Detach();

    void OnChanged(object? sender, EventArgs e) => App.Dispatcher.TryEnqueue(() =>
    {
        Load();
        _ = CountFollowersAsync();
    });

    // ---------------------------------------------------------------- sharing

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSharing), nameof(SharingDescription))]
    public partial bool IsSharing { get; set; }
    public bool IsNotSharing => !IsSharing;
    /// <summary>"K7MQ-2XPA"</summary>
    [ObservableProperty] public partial string MyCode { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SharingDescription), nameof(FollowersText))]
    public partial int? Followers { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SharingDescription), nameof(HasSharingError))]
    public partial string SharingError { get; set; } = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSharingCommand), nameof(StopSharingCommand))]
    public partial bool IsSharingBusy { get; set; }
    [ObservableProperty] public partial bool IsCopied { get; set; }

    /// <summary>"3 people follow you", shown under the code.</summary>
    public string FollowersText => Followers switch
    {
        null => "",
        0 => "No one follows you yet",
        1 => "1 person follows you",
        var n => $"{n} people follow you",
    };
    public bool HasSharingError => SharingError.Length > 0;
    public bool HasFollowError => FollowError.Length > 0;
    public bool HasFollowProblem => FollowProblem.Length > 0;
    /// <summary>The friend's initial, in a circle.</summary>
    public string FollowingInitial => FollowingName.Length > 0 ? char.ToUpperInvariant(FollowingName[0]).ToString() : "?";

    public string SharingDescription =>
        SharingError.Length > 0 ? SharingError
        : !IsSharing ? "Give friends a code. They see the wallpaper you have, every time it changes."
        : Followers switch
        {
            null or 0 => "No one follows you yet. Send this code to a friend.",
            1 => "1 person follows you. Anyone with this code can follow.",
            var n => $"{n} people follow you. Anyone with this code can follow.",
        };

    bool CanChangeSharing() => !IsSharingBusy;

    [RelayCommand(CanExecute = nameof(CanChangeSharing))]
    async Task StartSharing()
    {
        IsSharingBusy = true;
        SharingError = "";
        try
        {
            MyCode = FollowService.Format(await _follow.StartSharingAsync());
            IsSharing = true;
            Followers = 0;
        }
        catch (FollowException ex)
        {
            SharingError = ex.Message;
        }
        finally
        {
            IsSharingBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeSharing))]
    async Task StopSharing()
    {
        IsSharingBusy = true;
        SharingError = "";
        try
        {
            await _follow.StopSharingAsync();
            IsSharing = false;
            MyCode = "";
        }
        catch (FollowException ex)
        {
            SharingError = ex.Message;
        }
        finally
        {
            IsSharingBusy = false;
        }
    }

    /// <summary>The view copied the code: a check mark for a moment.</summary>
    public async Task MarkCopiedAsync()
    {
        IsCopied = true;
        await Task.Delay(2000);
        IsCopied = false;
    }

    async Task CountFollowersAsync()
    {
        if (!IsSharing) return;
        Followers = await _follow.CountFollowersAsync();
    }

    // ---------------------------------------------------------------- following

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotFollowing), nameof(FollowDescription))]
    public partial bool IsFollowing { get; set; }
    public bool IsNotFollowing => !IsFollowing;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowDescription), nameof(FollowingInitial))]
    public partial string FollowingName { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowDescription), nameof(HasFollowProblem))]
    public partial string FollowProblem { get; set; } = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FollowCommand))]
    public partial string CodeInput { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowDescription), nameof(HasFollowError))]
    public partial string FollowError { get; set; } = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FollowCommand), nameof(UnfollowCommand))]
    public partial bool IsFollowBusy { get; set; }

    public string FollowDescription =>
        FollowError.Length > 0 ? FollowError
        : FollowProblem.Length > 0 ? FollowProblem
        : IsFollowing ? $"Your desktop shows {FollowingName}'s wallpaper. Your own schedule is paused."
        : "Enter a friend's code to show their wallpaper on your desktop.";

    bool CanFollow() => !IsFollowBusy && FollowService.Normalize(CodeInput) is not null;

    [RelayCommand(CanExecute = nameof(CanFollow))]
    async Task Follow()
    {
        IsFollowBusy = true;
        FollowError = "";
        try
        {
            FollowingName = await _follow.FollowAsync(CodeInput);
            IsFollowing = true;
            CodeInput = "";
        }
        catch (FollowException ex)
        {
            FollowError = ex.Message;
        }
        finally
        {
            IsFollowBusy = false;
        }
    }

    bool CanUnfollow() => !IsFollowBusy;

    [RelayCommand(CanExecute = nameof(CanUnfollow))]
    async Task Unfollow()
    {
        IsFollowBusy = true;
        FollowError = "";
        try
        {
            await _follow.UnfollowAsync();
            IsFollowing = false;
        }
        finally
        {
            IsFollowBusy = false;
        }
    }

    void Load()
    {
        var s = _settings.Load();
        IsSharing = s.MyFollowCode is not null;
        MyCode = s.MyFollowCode is { } code ? FollowService.Format(code) : "";
        IsFollowing = s.IsFollowing;
        FollowingName = s.FollowName ?? "";
        FollowProblem = _settings.LoadState().FollowProblem ?? "";
    }
}
