using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core;
using Oriel.Core.Auth;
using Oriel.Core.Models;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Wallpaper;
using Oriel.UI.Services;
using Microsoft.UI.Xaml.Controls;

namespace Oriel.UI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    readonly ISettingsStore _settings;
    readonly IWallpaperSetter _setter;
    readonly AppPaths _paths;
    readonly WallpaperFolderMover _folderMover;
    readonly UpdateService _updates;
    readonly HotkeyService _hotkeys;
    readonly AuthService _auth;
    readonly WallpaperService _wallpapers;
    bool _loading;

    public SettingsViewModel(ISettingsStore settings, IWallpaperSetter setter, AppPaths paths,
        WallpaperFolderMover folderMover, UpdateService updates, HotkeyService hotkeys, AuthService auth,
        WallpaperService wallpapers)
    {
        _settings = settings;
        _setter = setter;
        _paths = paths;
        _folderMover = folderMover;
        _updates = updates;
        _hotkeys = hotkeys;
        _auth = auth;
        _wallpapers = wallpapers;
        Load();
        RefreshUpdate();
    }

    public string[] IntervalOptions { get; } = ["Once a day", "Every hour", "Every 3 hours", "Every 6 hours", "Every 12 hours"];
    static readonly int[] IntervalMinutes = [0, 60, 180, 360, 720];
    public string[] FitModes { get; } = ["Fill", "Fit", "Stretch", "Tile", "Center", "Span"];
    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    /// <summary>"Beta" while the csproj says so (AssemblyMetadata Stage), otherwise empty.</summary>
    public string Stage => Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "Stage")?.Value ?? "";

    // Account
    public string AccountEmail => _auth.Current?.Email ?? "";
    public string AccountName => _auth.Current is { DisplayName.Length: > 0 } a ? a.DisplayName : "Signed in";

    /// <summary>The Google profile photo, decoded small. Null (so initials show) if the account has none.</summary>
    public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? AccountPhoto =>
        Uri.TryCreate(_auth.Current?.PhotoUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri) { DecodePixelWidth = 80 }
            : null;

    [RelayCommand]
    Task SignOut() => _auth.SignOutAsync();

    [RelayCommand]
    async Task OpenAccount() => await Windows.System.Launcher.LaunchUriAsync(new Uri(OrielLinks.Account));

    [RelayCommand]
    async Task OpenSupport() => await Windows.System.Launcher.LaunchUriAsync(new Uri(OrielLinks.Support));

    // Schedule
    [ObservableProperty] public partial TimeSpan DailyTime { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDaily))]
    public partial int IntervalIndex { get; set; }
    /// <summary>The daily time and sign-in options only make sense when not running a slideshow.</summary>
    public bool IsDaily => IntervalIndex <= 0;
    [ObservableProperty] public partial int SourceIndex { get; set; }
    public string[] SourceOptions { get; } = ["New photos", "My favorites"];
    [ObservableProperty] public partial bool ApplyAtLogin { get; set; }

    // Display
    [ObservableProperty] public partial int FitIndex { get; set; }
    [ObservableProperty] public partial bool MatchTimeOfDay { get; set; }
    [ObservableProperty] public partial bool PhotoPerMonitor { get; set; }
    /// <summary>Only worth offering with more than one monitor.</summary>
    public bool HasMonitors => _setter.GetMonitors().Count > 1;



    // Quality
    [ObservableProperty] public partial bool SkipLowLikes { get; set; }
    [ObservableProperty] public partial double MinLikes { get; set; }

    // Storage & app
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial string PicturesFolder { get; set; } = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeFolderCommand))]
    public partial bool IsMovingFolder { get; set; }
    [ObservableProperty] public partial bool IsFolderInfoOpen { get; set; }
    [ObservableProperty] public partial string FolderInfoMessage { get; set; } = "";
    [ObservableProperty] public partial InfoBarSeverity FolderInfoSeverity { get; set; }

    bool _revertingStartup;
    /// <summary>Set by the page: asks before "Start with Windows" is turned off; true means go ahead.</summary>
    public Func<Task<bool>>? ConfirmTurnOffStartup { get; set; }

    /// <summary>Set by the page: shows a folder picker and returns the chosen path, or null.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }
    /// <summary>0 = desktop only, 1 = desktop and lock screen.</summary>
    [ObservableProperty] public partial int ApplyToIndex { get; set; }
    public string[] ApplyToOptions { get; } = ["Desktop only", "Desktop and lock screen"];

    [ObservableProperty] public partial bool MatchAccentColor { get; set; }

    // Keyboard shortcut for "next wallpaper"
    [ObservableProperty] public partial string Shortcut { get; set; } = "";
    [ObservableProperty] public partial string ShortcutNote { get; set; } = "";

    /// <summary>Saves the new shortcut and registers it right away. Null clears it.</summary>
    public async Task SetShortcutAsync(Hotkey? key)
    {
        Shortcut = key?.ToString() ?? "";
        _settings.Update(s => s.Hotkey = Shortcut);
        ShortcutNote = await _hotkeys.ApplyAsync(key) ?? "";
    }

    [RelayCommand] Task ClearShortcut() => SetShortcutAsync(null);

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
        ApplyAtLogin = s.ApplyAtLogin;
        FitIndex = (int)s.Fit;
        SkipLowLikes = s.SkipLowLikes;
        MinLikes = s.MinLikes;
        StartWithWindows = s.StartWithWindows;
        PicturesFolder = _paths.WallpaperFolder(s);
        ApplyToIndex = s.ChangeLockScreen ? 1 : 0;
        MatchAccentColor = s.MatchAccentColor;
        MatchTimeOfDay = s.MatchTimeOfDay;
        PhotoPerMonitor = s.PhotoPerMonitor;
        _loading = false;
    }

    [RelayCommand] void OpenPicturesFolder() => ImageLoader.OpenFolder(PicturesFolder);

    [RelayCommand(CanExecute = nameof(CanChangeFolder))]
    async Task ChangeFolder()
    {
        if (PickFolder is null || await PickFolder() is not { } folder) return;

        IsMovingFolder = true;
        try
        {
            var count = await Task.Run(() => _folderMover.MoveTo(folder));
            PicturesFolder = _paths.WallpaperFolder(_settings.Load());
            ShowFolderInfo(InfoBarSeverity.Success, count == 1 ? "Moved 1 photo." : $"Moved {count} photos.");
        }
        catch (WallpaperException ex)
        {
            ShowFolderInfo(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            IsMovingFolder = false;
        }
    }

    bool CanChangeFolder() => !IsMovingFolder;

    void ShowFolderInfo(InfoBarSeverity severity, string message)
    {
        FolderInfoSeverity = severity;
        FolderInfoMessage = message;
        IsFolderInfoOpen = true;
    }

    /// <summary>The page calls these on Loaded/Unloaded so the singleton service doesn't keep this VM alive.</summary>
    public void AttachUpdates() => _updates.StateChanged += OnUpdateStateChanged;
    public void DetachUpdates() => _updates.StateChanged -= OnUpdateStateChanged;

    void OnUpdateStateChanged(object? sender, EventArgs e) => App.Dispatcher.TryEnqueue(RefreshUpdate);

    void RefreshUpdate()
    {
        var version = Stage is { Length: > 0 } stage ? $"Version {Version} {stage.ToLowerInvariant()}" : $"Version {Version}";
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
    partial void OnSourceIndexChanged(int value) => Save(s => s.FavoritesOnly = value == 1);
    partial void OnApplyAtLoginChanged(bool value) => Save(s => s.ApplyAtLogin = value);
    partial void OnFitIndexChanged(int value) { if (value >= 0) Save(s => s.Fit = (WallpaperFit)value); }
    partial void OnSkipLowLikesChanged(bool value) => Save(s => s.SkipLowLikes = value);
    partial void OnMinLikesChanged(double value) => Save(s => s.MinLikes = (int)value);
    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading || _revertingStartup) return;
        if (value || ConfirmTurnOffStartup is null) Save(s => s.StartWithWindows = value);
        else _ = ConfirmStartupOffAsync();
    }

    /// <summary>Turning it off is only saved after the user confirms; otherwise the switch goes back on.</summary>
    async Task ConfirmStartupOffAsync()
    {
        if (await ConfirmTurnOffStartup!())
        {
            Save(s => s.StartWithWindows = false);
            return;
        }
        _revertingStartup = true;
        StartWithWindows = true;
        _revertingStartup = false;
    }
    partial void OnApplyToIndexChanged(int value) { if (value >= 0) Save(s => s.ChangeLockScreen = value == 1); }
    partial void OnMatchTimeOfDayChanged(bool value) => Save(s => s.MatchTimeOfDay = value);
    partial void OnPhotoPerMonitorChanged(bool value) => Save(s => s.PhotoPerMonitor = value);
    partial void OnMatchAccentColorChanged(bool value)
    {
        if (_loading) return;
        Save(s => s.MatchAccentColor = value);
        // Turning it on takes effect now, not only at the next wallpaper change.
        if (value) _ = Task.Run(_wallpapers.ApplyAccentToCurrentAsync);
    }
}
