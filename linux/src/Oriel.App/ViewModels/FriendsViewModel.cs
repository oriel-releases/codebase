using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Oriel.Core.Auth;
using Oriel.Core.Models;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Oriel.UI.Services;

namespace Oriel.UI.ViewModels;

/// <summary>Someone following you, in the followers window. Remove takes two clicks: the first asks "Sure?".</summary>
public sealed partial class FollowerRow(Follower follower) : ObservableObject
{
    public string Uid => follower.Uid;
    public string Name => follower.Name;
    public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";
    public string Since => follower.Since is { } at ? $"Since {at.LocalDateTime:MMM d}" : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoveText))]
    public partial bool IsConfirming { get; set; }
    public string RemoveText => IsConfirming ? "Remove?" : "Remove";
}

/// <summary>
/// The Friends page: share your wallpaper (one link, always the same), follow a friend with their code, and who
/// follows you. Lives as long as the page (Detach when it closes).
/// </summary>
public sealed partial class FriendsViewModel : ObservableObject, IDisposable
{
    readonly FollowService _follow;
    readonly ISettingsStore _settings;
    readonly IHistoryStore _history;
    readonly AuthService _auth;

    public FriendsViewModel(FollowService follow, ISettingsStore settings, IHistoryStore history, AuthService auth)
    {
        _follow = follow;
        _settings = settings;
        _history = history;
        _auth = auth;
        _follow.Changed += OnChanged;
        Load();
        _ = LoadFriendWallpaperAsync();
        _ = CountFollowersAsync();
    }

    public void Detach() => _follow.Changed -= OnChanged;
    public void Dispose() => Detach();

    void OnChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        Load();
        _ = LoadFriendWallpaperAsync();
        _ = CountFollowersAsync();
    });

    /// <summary>Set by the page: asks (title, text, confirm button) and returns true if the person confirmed.</summary>
    public Func<string, string, string, Task<bool>>? Confirm { get; set; }

    /// <summary>A problem to show under the list (sharing, a follow link, a friend who stopped sharing).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string Message { get; set; } = "";
    public bool HasMessage => Message.Length > 0;

    // ---------------------------------------------------------------- following a friend

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotFollowing))]
    public partial bool IsFollowing { get; set; }
    public bool IsNotFollowing => !IsFollowing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowingTitle), nameof(FollowingInitial))]
    public partial string FollowingName { get; set; } = "";
    public string FollowingTitle => $"Following {FollowingName}";
    /// <summary>The friend's Google picture, over their initial (when they have one).</summary>
    [ObservableProperty] public partial Avalonia.Media.Imaging.Bitmap? FollowingPicture { get; set; }

    async Task LoadFollowingPictureAsync(string? url) =>
        FollowingPicture = string.IsNullOrEmpty(url) ? null : await Oriel.UI.Services.ImageLoader.LoadAsync(null, url, 160);

    public string FollowingInitial => FollowingName.Length > 0 ? char.ToUpperInvariant(FollowingName[0]).ToString() : "?";

    /// <summary>The friend's wallpaper (on this desktop too). Null until there is one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFriendWallpaper), nameof(HasNoFriendWallpaper))]
    public partial Bitmap? FriendWallpaper { get; set; }
    public bool HasFriendWallpaper => FriendWallpaper is not null;
    public bool HasNoFriendWallpaper => FriendWallpaper is null;
    [ObservableProperty] public partial string FriendWallpaperDetail { get; set; } = "";

    /// <summary>A follow link is being followed right now (opened from a friend's follow page).</summary>
    [ObservableProperty] public partial bool IsLinkFollowing { get; set; }

    /// <summary>The code typed in the follow window, tidied by the page as it's typed ("K7MQ-2X").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFollowCode))]
    public partial string CodeInput { get; set; } = "";
    public bool CanFollowCode => !IsFollowBusy && FollowService.Normalize(CodeInput) is not null;

    /// <summary>Why the code didn't work, inside the follow window.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCodeError))]
    public partial string CodeError { get; set; } = "";
    public bool HasCodeError => CodeError.Length > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnfollowCommand))]
    [NotifyPropertyChangedFor(nameof(CanFollowCode))]
    public partial bool IsFollowBusy { get; set; }

    /// <summary>The follow window opens empty.</summary>
    public void StartEnteringCode()
    {
        CodeInput = "";
        CodeError = "";
        IsCodeOpen = true;
    }

    /// <summary>The follow window and the followers window, shown over the page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDialogOpen))]
    public partial bool IsCodeOpen { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDialogOpen))]
    public partial bool IsFollowersOpen { get; set; }
    public bool IsDialogOpen => IsCodeOpen || IsFollowersOpen;

    [RelayCommand]
    void CloseDialogs()
    {
        IsCodeOpen = false;
        IsFollowersOpen = false;
    }

    /// <summary>The follow window's Follow button (and Enter): closes it when it worked.</summary>
    [RelayCommand]
    async Task SubmitCode()
    {
        if (await FollowAsync()) IsCodeOpen = false;
    }

    [RelayCommand]
    async Task OpenFollowers()
    {
        IsFollowersOpen = true;
        await LoadFollowersAsync();
    }

    [RelayCommand]
    Task Remove(FollowerRow row) => RemoveAsync(row);

    /// <summary>Follows the typed code. True when it worked (the window closes); otherwise the window shows why.</summary>
    public async Task<bool> FollowAsync()
    {
        if (!CanFollowCode) return false;
        IsFollowBusy = true;
        CodeError = "";
        try
        {
            FollowingName = await _follow.FollowAsync(CodeInput);
            IsFollowing = true;
            await LoadFriendWallpaperAsync();
            return true;
        }
        catch (FollowException ex)
        {
            CodeError = ex.Message;
            return false;
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
        if (Confirm is not null && !await Confirm($"Stop following {FollowingName}?", "Oriel goes back to your own wallpapers.", "Unfollow"))
            return;
        IsFollowBusy = true;
        try
        {
            await _follow.UnfollowAsync();
            IsFollowing = false;
            FriendWallpaper = null;
        }
        finally
        {
            IsFollowBusy = false;
        }
    }

    /// <summary>The friend's latest wallpaper is the newest history entry that came from them (it is on disk already).</summary>
    async Task LoadFriendWallpaperAsync()
    {
        if (!IsFollowing)
        {
            FriendWallpaper = null;
            return;
        }
        var entry = _history.List().FirstOrDefault(e => e.Topic == $"from {FollowingName}");
        if (entry is null)
        {
            FriendWallpaper = null;
            FriendWallpaperDetail = "Their wallpaper arrives in a moment.";
            return;
        }
        FriendWallpaper = await ImageLoader.LoadAsync(entry.FilePath, entry.ThumbnailUrl, 900);
        FriendWallpaperDetail = string.IsNullOrWhiteSpace(entry.PhotographerName) ? "" : $"Photo by {entry.PhotographerName}";
    }

    // ---------------------------------------------------------------- sharing yours

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareText))]
    public partial bool IsShareBusy { get; set; }
    /// <summary>"Link copied" for a moment, when the share sheet couldn't open and the link went to the clipboard.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareText))]
    public partial bool IsLinkCopied { get; set; }
    public string ShareText => IsShareBusy ? "Getting your link…" : IsLinkCopied ? "Link copied" : "Share";

    /// <summary>
    /// Your follow link (made the first time), with your current wallpaper on it so the friend sees what they'll get.
    /// Null when it couldn't be made (Message says why).
    /// </summary>
    public async Task<string?> GetShareLinkAsync()
    {
        IsShareBusy = true;
        Message = "";
        try
        {
            var code = await _follow.EnsureCodeAsync();
            IsSharing = true;
            return ShareLink.ForFollow(code);
        }
        catch (FollowException ex)
        {
            Message = ex.Message;
            return null;
        }
        finally
        {
            IsShareBusy = false;
        }
    }

    public async Task MarkLinkCopiedAsync()
    {
        IsLinkCopied = true;
        await Task.Delay(2000);
        IsLinkCopied = false;
    }

    // ---------------------------------------------------------------- who follows you

    /// <summary>True while your link works (from the first Share until you turn it off).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareDetail))]
    [NotifyCanExecuteChangedFor(nameof(TurnOffSharingCommand))]
    public partial bool IsSharing { get; set; }

    /// <summary>Under "Share my wallpaper": whether it's on, and how many follow.</summary>
    public string ShareDetail => !IsSharing ? "Off. Friends get every wallpaper you set."
        : FollowerCount switch
        {
            null => "On.",
            0 => "On. No one follows yet.",
            1 => "On. 1 person follows you.",
            var n => $"On. {n} people follow you.",
        };

    public ObservableCollection<FollowerRow> FollowerRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowersDetail), nameof(HasFollowers), nameof(HasNoFollowers), nameof(ShareDetail))]
    public partial int? FollowerCount { get; set; }
    public string FollowersDetail => FollowerCount switch
    {
        null => "",
        0 => "No one yet",
        1 => "1 person",
        var n => $"{n} people",
    };
    public bool HasFollowers => FollowerCount > 0;
    public bool HasNoFollowers => !HasFollowers;

    /// <summary>The followers window is opening: the latest list.</summary>
    public async Task LoadFollowersAsync()
    {
        var followers = await _follow.ListFollowersAsync();
        FollowerRows.Clear();
        foreach (var f in followers ?? []) FollowerRows.Add(new FollowerRow(f));
        FollowerCount = followers?.Count;
    }

    async Task CountFollowersAsync()
    {
        if (IsSharing) FollowerCount = (await _follow.ListFollowersAsync())?.Count;
    }

    /// <summary>First click asks ("Remove?"), the second removes.</summary>
    public async Task RemoveAsync(FollowerRow row)
    {
        if (!row.IsConfirming)
        {
            foreach (var other in FollowerRows) other.IsConfirming = false;
            row.IsConfirming = true;
            return;
        }
        try
        {
            await _follow.RemoveFollowerAsync(row.Uid);
            FollowerRows.Remove(row);
            FollowerCount = FollowerRows.Count;
        }
        catch (FollowException ex)
        {
            Message = ex.Message;
        }
    }

    bool CanTurnOffSharing() => IsSharing;

    /// <summary>After asking: your link stops working and everyone following stops. Sharing again makes a new link.</summary>
    [RelayCommand(CanExecute = nameof(CanTurnOffSharing))]
    async Task TurnOffSharing()
    {
        var who = FollowerCount switch
        {
            1 => " The 1 person following you stops getting your wallpaper.",
            > 1 and var n => $" The {n} people following you stop getting your wallpaper.",
            _ => "",
        };
        if (Confirm is not null && !await Confirm("Turn off sharing?", $"Your link stops working.{who}", "Turn off")) return;
        try
        {
            await _follow.StopSharingAsync();
            IsSharing = false;
            FollowerRows.Clear();
            FollowerCount = null;
        }
        catch (FollowException ex)
        {
            Message = ex.Message;
        }
    }

    // ----------------------------------------------------------------

    void Load()
    {
        var s = _settings.Load();
        IsSharing = s.MyFollowCode is not null;
        IsFollowing = s.IsFollowing;
        FollowingName = s.FollowName ?? "";
        _ = LoadFollowingPictureAsync(s.FollowPicture);
        IsLinkFollowing = _follow.LinkCode is not null;
        // A follow link that didn't work is shown once; a friend who stopped sharing, for as long as it's true.
        if (_follow.LinkError is { } linkError)
        {
            Message = linkError;
            _follow.ClearLinkError();
        }
        else if (IsFollowing && _settings.LoadState().FollowProblem is { } problem) Message = problem;
    }
}
