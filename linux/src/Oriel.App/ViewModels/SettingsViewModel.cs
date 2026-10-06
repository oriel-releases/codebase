using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core;
using Oriel.Core.Auth;
using Oriel.Core.Models;
using Oriel.Core.Platform;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.UI.Services;

namespace Oriel.UI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    readonly ISettingsStore _settings;
    readonly AppPaths _paths;
    readonly WallpaperFolderMover _folderMover;
    readonly UpdateService _updates;
    readonly HotkeyService _hotkeys;
    readonly AuthService _auth;
    bool _loading;

    public SettingsViewModel(ISettingsStore settings, AppPaths paths, WallpaperFolderMover folderMover,
        UpdateService updates, HotkeyService hotkeys, AuthService auth)
    {
        _settings = settings;
        _paths = paths;
        _folderMover = folderMover;
        _updates = updates;
        _hotkeys = hotkeys;
        _auth = auth;
        Load();
        RefreshUpdate();
        _updates.StateChanged += OnUpdateStateChanged;
        _ = LoadAccountPhotoAsync();
    }

    public string[] IntervalOptions { get; } = ["Once a day", "Every hour", "Every 3 hours", "Every 6 hours", "Every 12 hours"];
    static readonly int[] IntervalMinutes = [0, 60, 180, 360, 720];
    public string[] FitModes { get; } = ["Fill", "Fit", "Stretch", "Tile", "Center", "Span"];
    public string Version => AppInfo.Version;

    // Account
    public string AccountEmail => _auth.Current?.Email ?? "";
    public string AccountName => _auth.Current is { DisplayName.Length: > 0 } a ? a.DisplayName : "Signed in";
    public string AccountInitial => AccountName.Length > 0 ? char.ToUpperInvariant(AccountName[0]).ToString() : "O";

    /// <summary>The Google profile photo, decoded small. Null (so the logo shows) if the account has none.</summary>
    [ObservableProperty] public partial Bitmap? AccountPhoto { get; set; }

    async Task LoadAccountPhotoAsync()
    {
        if (Uri.TryCreate(_auth.Current?.PhotoUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            AccountPhoto = await ImageLoader.LoadUrlAsync(uri.AbsoluteUri, 80);
    }

    [RelayCommand]
    Task SignOut() => _auth.SignOutAsync();

    // Schedule
    [ObservableProperty] public partial TimeSpan DailyTime { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDaily))]
    public partial int IntervalIndex { get; set; }
    /// <summary>The daily time and login options only make sense when not running a slideshow.</summary>
    public bool IsDaily => IntervalIndex <= 0;
    [ObservableProperty] public partial int SourceIndex { get; set; }
    public string[] SourceOptions { get; } = ["New photos", "My favorites"];
    [ObservableProperty] public partial bool ApplyAtLogin { get; set; }

    // Display
    [ObservableProperty] public partial int FitIndex { get; set; }

    // Quality
    [ObservableProperty] public partial bool SkipLowLikes { get; set; }
    [ObservableProperty] public partial double MinLikes { get; set; }

    // Storage & app
    [ObservableProperty] public partial bool StartAtLogin { get; set; }
    [ObservableProperty] public partial string PicturesFolder { get; set; } = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeFolderCommand))]
    public partial bool IsMovingFolder { get; set; }
    public InfoState FolderInfo { get; } = new();

    bool _revertingStartup;
    /// <summary>Set by the page: asks before "Start at login" is turned off; true means go ahead.</summary>
    public Func<Task<bool>>? ConfirmTurnOffStartup { get; set; }

    /// <summary>Set by the page: shows a folder picker and returns the chosen path, or null.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }
    /// <summary>0 = desktop only, 1 = desktop and lock screen.</summary>
    [ObservableProperty] public partial int ApplyToIndex { get; set; }
    public string[] ApplyToOptions { get; } = ["Desktop only", "Desktop and lock screen"];
    public string ApplyToNote => DesktopEnvironment.Current switch
    {
        Desktop.Kde => "Choose whether each new wallpaper also becomes your lock screen picture.",
        Desktop.Gnome or Desktop.Cinnamon => "Your lock screen already shows your wallpaper on this desktop.",
        _ => "Your desktop doesn't let apps change the lock screen picture, so this has no effect here.",
    };

    // Keyboard shortcut for "next wallpaper"
    [ObservableProperty] public partial string Shortcut { get; set; } = "";
    [ObservableProperty] public partial string ShortcutNote { get; set; } = "";

    /// <summary>Saves the new shortcut and registers it right away. Null clears it.</summary>
    public async Task SetShortcutAsync(Hotkey? key)
    {
        Shortcut = key?.ToString() ?? "";
        _settings.Update(s => s.Hotkey = Shortcut);
        ShortcutNote = await _hotkeys.ApplyAsync(key) ?? DefaultShortcutNote();
    }

    [RelayCommand] Task ClearShortcut() => SetShortcutAsync(null);

    static string DefaultShortcutNote() => DesktopEnvironment.IsWayland
        ? "Wayland doesn't let apps listen for keys. Add a shortcut in your system keyboard settings that runs: oriel --change-now"
        : "Click the box, then press the keys. Needs Ctrl, Alt or Super.";

    // Updates
    public bool CanUpdate => _updates.IsInstalled;
    [ObservableProperty] public partial string UpdateStatus { get; set; } = "";
    [ObservableProperty] public partial double UpdateProgress { get; set; }
    [ObservableProperty] public partial bool IsDownloading { get; set; }
    [ObservableProperty] public partial string UpdateButtonText { get; set; } = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    public partial bool IsUpdateBusy { get; set; }

    void Load()
    {
        _loading = true;
        var s = _settings.Load();
        DailyTime = s.GetDailyTime().ToTimeSpan();
        IntervalIndex = Math.Max(0, Array.IndexOf(IntervalMinutes, s.IntervalMinutes));
        SourceIndex = s.FavoritesOnly ? 1 : 0;
        Shortcut = s.Hotkey;
        ShortcutNote = DefaultShortcutNote();
        ApplyAtLogin = s.ApplyAtLogin;
        FitIndex = (int)s.Fit;
        SkipLowLikes = s.SkipLowLikes;
        MinLikes = s.MinLikes;
        StartAtLogin = s.StartAtLogin;
        PicturesFolder = _paths.WallpaperFolder(s);
        ApplyToIndex = s.ChangeLockScreen ? 1 : 0;
        _loading = false;
    }

    [RelayCommand] void OpenPicturesFolder() => Launcher.OpenFolder(PicturesFolder);

    [RelayCommand(CanExecute = nameof(CanChangeFolder))]
    async Task ChangeFolder()
    {
        if (PickFolder is null || await PickFolder() is not { } folder) return;

        IsMovingFolder = true;
        try
        {
            var count = await Task.Run(() => _folderMover.MoveTo(folder));
            PicturesFolder = _paths.WallpaperFolder(_settings.Load());
            FolderInfo.Show(InfoSeverity.Success, "", count == 1 ? "Moved 1 photo." : $"Moved {count} photos.");
        }
        catch (WallpaperException ex)
        {
            FolderInfo.Show(InfoSeverity.Error, "", ex.Message);
        }
        finally
        {
            IsMovingFolder = false;
        }
    }

    bool CanChangeFolder() => !IsMovingFolder;

    void OnUpdateStateChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshUpdate);

    void RefreshUpdate()
    {
        var stage = AppInfo.Stage;
        var version = stage.Length > 0 ? $"Version {Version} {stage.ToLowerInvariant()}" : $"Version {Version}";
        if (!CanUpdate)
        {
            UpdateStatus = $"{version} · Updates work in the installed app.";
            return;
        }
        var state = _updates.State;
        IsDownloading = state == UpdateState.Downloading;
        UpdateProgress = _updates.Progress;
        IsUpdateBusy = state is UpdateState.Checking or UpdateState.Downloading;
        UpdateButtonText = state == UpdateState.ReadyToRestart ? "Restart to update" : "Check for updates";
        var status = state switch
        {
            UpdateState.Checking => "Checking for updates…",
            UpdateState.UpToDate => "You have the latest version.",
            UpdateState.Downloading => $"Downloading version {_updates.AvailableVersion}… {_updates.Progress}%",
            UpdateState.ReadyToRestart => $"Version {_updates.AvailableVersion} is ready. It installs when you restart or quit Oriel.",
            UpdateState.Failed => "Couldn't check for updates. Check your connection and try again.",
            _ => "Oriel checks for updates once a day and installs them in the background.",
        };
        UpdateStatus = $"{version} · {status}";
    }

    [RelayCommand(CanExecute = nameof(CanRunUpdate))]
    async Task Update()
    {
        if (_updates.State == UpdateState.ReadyToRestart) _updates.RestartToUpdate();
        else await _updates.CheckAsync();
    }

    bool CanRunUpdate() => !IsUpdateBusy;

    void Save(Action<OrielSettings> change)
    {
        if (!_loading) _settings.Update(change);
    }

    partial void OnDailyTimeChanged(TimeSpan value) =>
        Save(s => s.DailyTime = $"{value.Hours:00}:{value.Minutes:00}");
    partial void OnIntervalIndexChanged(int value)
    {
        if (value >= 0) Save(s => s.IntervalMinutes = IntervalMinutes[value]);
    }
    partial void OnSourceIndexChanged(int value) { if (value >= 0) Save(s => s.FavoritesOnly = value == 1); }
    partial void OnApplyAtLoginChanged(bool value) => Save(s => s.ApplyAtLogin = value);
    partial void OnFitIndexChanged(int value) { if (value >= 0) Save(s => s.Fit = (WallpaperFit)value); }
    partial void OnSkipLowLikesChanged(bool value) => Save(s => s.SkipLowLikes = value);
    partial void OnMinLikesChanged(double value) => Save(s => s.MinLikes = (int)value);
    partial void OnStartAtLoginChanged(bool value)
    {
        if (_loading || _revertingStartup) return;
        if (value || ConfirmTurnOffStartup is null) Save(s => s.StartAtLogin = value);
        else _ = ConfirmStartupOffAsync();
    }

    /// <summary>Turning it off is only saved after the user confirms; otherwise the switch goes back on.</summary>
    async Task ConfirmStartupOffAsync()
    {
        if (await ConfirmTurnOffStartup!())
        {
            Save(s => s.StartAtLogin = false);
            return;
        }
        _revertingStartup = true;
        StartAtLogin = true;
        _revertingStartup = false;
    }

    partial void OnApplyToIndexChanged(int value) { if (value >= 0) Save(s => s.ChangeLockScreen = value == 1); }

    /// <summary>The singleton update service must not keep a closed page alive.</summary>
    public void Dispose()
    {
        _updates.StateChanged -= OnUpdateStateChanged;
        var photo = AccountPhoto;
        AccountPhoto = null;
        photo?.Dispose();
    }
}
