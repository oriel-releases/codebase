using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core;
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
    bool _loading;

    public SettingsViewModel(ISettingsStore settings, IWallpaperSetter setter, AppPaths paths,
        WallpaperFolderMover folderMover, UpdateService updates)
    {
        _settings = settings;
        _setter = setter;
        _paths = paths;
        _folderMover = folderMover;
        _updates = updates;
        Load();
        RefreshUpdate();
    }

    public string[] MonitorModes { get; } = ["Same image on all monitors", "Different image per monitor"];
    public string[] FitModes { get; } = ["Fill", "Fit", "Stretch", "Tile", "Center", "Span"];
    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    /// <summary>"Beta" while the csproj says so (AssemblyMetadata Stage), otherwise empty.</summary>
    public string Stage => Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "Stage")?.Value ?? "";

    // Schedule
    [ObservableProperty] public partial TimeSpan DailyTime { get; set; }
    [ObservableProperty] public partial bool ApplyAtLogin { get; set; }
    [ObservableProperty] public partial bool UseTaskSchedulerFallback { get; set; }

    // Display
    [ObservableProperty] public partial int MonitorModeIndex { get; set; }
    [ObservableProperty] public partial int FitIndex { get; set; }
    [ObservableProperty] public partial string MonitorSummary { get; set; } = "";
    [ObservableProperty] public partial bool HasMultipleMonitors { get; set; }

    // Quality
    [ObservableProperty] public partial bool SkipSmallerThanScreen { get; set; }
    [ObservableProperty] public partial bool SkipLowLikes { get; set; }
    [ObservableProperty] public partial double MinLikes { get; set; }

    // Storage & app
    [ObservableProperty] public partial double RetentionDays { get; set; }
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial string PicturesFolder { get; set; } = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeFolderCommand))]
    public partial bool IsMovingFolder { get; set; }
    [ObservableProperty] public partial bool IsFolderInfoOpen { get; set; }
    [ObservableProperty] public partial string FolderInfoMessage { get; set; } = "";
    [ObservableProperty] public partial InfoBarSeverity FolderInfoSeverity { get; set; }

    /// <summary>Set by the page: shows a folder picker and returns the chosen path, or null.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }
    [ObservableProperty] public partial bool ShowNotifications { get; set; }

    // Updates
    public bool CanUpdate => _updates.IsInstalled;
    [ObservableProperty] public partial string UpdateStatus { get; set; } = "";
    [ObservableProperty] public partial string UpdateButtonText { get; set; } = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    public partial bool IsUpdateBusy { get; set; }

    void Load()
    {
        _loading = true;
        var s = _settings.Load();
        DailyTime = s.GetDailyTime().ToTimeSpan();
        ApplyAtLogin = s.ApplyAtLogin;
        UseTaskSchedulerFallback = s.UseTaskSchedulerFallback;
        MonitorModeIndex = (int)s.MonitorMode;
        FitIndex = (int)s.Fit;
        SkipSmallerThanScreen = s.SkipSmallerThanScreen;
        SkipLowLikes = s.SkipLowLikes;
        MinLikes = s.MinLikes;
        RetentionDays = s.RetentionDays;
        StartWithWindows = s.StartWithWindows;
        PicturesFolder = _paths.WallpaperFolder(s);
        ShowNotifications = s.ShowNotifications;
        RefreshMonitors();
        _loading = false;
    }

    void RefreshMonitors()
    {
        try
        {
            var monitors = _setter.GetMonitors();
            HasMultipleMonitors = monitors.Count > 1;
            MonitorSummary = string.Join(",  ", monitors.Select(m => $"{m.Width}×{m.Height}{(m.IsPrimary ? " (primary)" : "")}"));
        }
        catch (Exception)
        {
            MonitorSummary = "Couldn't read monitors";
        }
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
    [RelayCommand] void OpenLogsFolder() => ImageLoader.OpenFolder(_paths.LogsDir);

    /// <summary>The page calls these on Loaded/Unloaded so the singleton service doesn't keep this VM alive.</summary>
    public void AttachUpdates() => _updates.StateChanged += OnUpdateStateChanged;
    public void DetachUpdates() => _updates.StateChanged -= OnUpdateStateChanged;

    void OnUpdateStateChanged(object? sender, EventArgs e) => App.Dispatcher.TryEnqueue(RefreshUpdate);

    void RefreshUpdate()
    {
        if (!CanUpdate)
        {
            UpdateStatus = "Updates work in the installed app.";
            return;
        }
        var state = _updates.State;
        IsUpdateBusy = state is UpdateState.Checking or UpdateState.Downloading;
        UpdateButtonText = state == UpdateState.ReadyToRestart ? "Restart to update" : "Check for updates";
        UpdateStatus = state switch
        {
            UpdateState.Checking => "Checking for updates…",
            UpdateState.UpToDate => "You have the latest version.",
            UpdateState.Downloading => $"Downloading version {_updates.AvailableVersion}…",
            UpdateState.ReadyToRestart => $"Version {_updates.AvailableVersion} is ready. It installs when you restart or quit Oriel.",
            UpdateState.Failed => "Couldn't check for updates. Check your connection and try again.",
            _ => "Oriel checks for updates once a day and installs them in the background.",
        };
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
    partial void OnApplyAtLoginChanged(bool value) => Save(s => s.ApplyAtLogin = value);
    partial void OnUseTaskSchedulerFallbackChanged(bool value) => Save(s => s.UseTaskSchedulerFallback = value);
    partial void OnMonitorModeIndexChanged(int value) { if (value >= 0) Save(s => s.MonitorMode = (MultiMonitorMode)value); }
    partial void OnFitIndexChanged(int value) { if (value >= 0) Save(s => s.Fit = (WallpaperFit)value); }
    partial void OnSkipSmallerThanScreenChanged(bool value) => Save(s => s.SkipSmallerThanScreen = value);
    partial void OnSkipLowLikesChanged(bool value) => Save(s => s.SkipLowLikes = value);
    partial void OnMinLikesChanged(double value) => Save(s => s.MinLikes = (int)value);
    partial void OnRetentionDaysChanged(double value)
    {
        if (double.IsNaN(value)) return;
        Save(s => s.RetentionDays = Math.Clamp((int)value, 1, 3650));
    }
    partial void OnStartWithWindowsChanged(bool value) => Save(s => s.StartWithWindows = value);
    partial void OnShowNotificationsChanged(bool value) => Save(s => s.ShowNotifications = value);
}
