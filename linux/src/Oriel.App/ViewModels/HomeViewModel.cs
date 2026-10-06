using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core;
using Oriel.Core.Providers;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Oriel.UI.Services;

namespace Oriel.UI.ViewModels;

public sealed partial class HomeViewModel : ObservableObject
{
    readonly WallpaperService _wallpapers;
    readonly IHistoryStore _history;
    readonly ISettingsStore _settings;
    readonly AppPaths _paths;
    long? _currentId;
    bool _previewWanted;
    string? _currentFile;
    bool _loading;
    DispatcherTimer? _limitTimer;

    public HomeViewModel(WallpaperService wallpapers, IHistoryStore history, ISettingsStore settings, AppPaths paths)
    {
        _wallpapers = wallpapers;
        _history = history;
        _settings = settings;
        _paths = paths;
        _settings.SettingsChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshAsync());
    }

    public InfoState Info { get; } = new();

    [ObservableProperty] public partial Bitmap? Preview { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoCurrent))]
    public partial bool HasCurrent { get; set; }
    public bool HasNoCurrent => !HasCurrent;
    [ObservableProperty] public partial string PhotographerName { get; set; } = "";
    [ObservableProperty] public partial Uri? PhotographerUri { get; set; }
    [ObservableProperty] public partial Uri? PhotoPageUri { get; set; }
    [ObservableProperty] public partial string Caption { get; set; } = "";
    [ObservableProperty] public partial bool IsFavorite { get; set; }
    [ObservableProperty] public partial bool IsPaused { get; set; }
    [ObservableProperty] public partial string NextChangeText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanChange), nameof(ShowChangeIcon), nameof(ShowLimitIcon))]
    [NotifyCanExecuteChangedFor(nameof(ChangeNowCommand))]
    public partial bool IsBusy { get; set; }
    public bool IsIdle => !IsBusy;

    // "Change now" is limited per hour: until it works again the button counts down, e.g. "Try after 20 min".
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChange), nameof(ShowChangeIcon), nameof(ShowLimitIcon), nameof(ChangeLabel))]
    [NotifyCanExecuteChangedFor(nameof(ChangeNowCommand))]
    public partial int LimitMinutes { get; set; }
    bool IsLimited => LimitMinutes > 0;
    public bool CanChange => IsIdle && !IsLimited;
    public bool ShowChangeIcon => IsIdle && !IsLimited;
    public bool ShowLimitIcon => IsIdle && IsLimited;
    public string ChangeLabel => IsLimited ? $"Try after {LimitMinutes} min" : "Change now";

    /// <summary>Shows the minutes left and re-checks every 15 s while limited, so the button comes back by itself.</summary>
    void UpdateLimit(OrielState state)
    {
        var now = DateTimeOffset.Now;
        var until = WallpaperService.ManualChangeAvailableAt(state, now);
        LimitMinutes = until is { } at ? WallpaperService.MinutesUntil(at, now) : 0;
        if (until is null)
        {
            _limitTimer?.Stop();
            return;
        }
        if (_limitTimer is null)
        {
            _limitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _limitTimer.Tick += (_, _) => UpdateLimit(_settings.LoadState());
        }
        if (!_limitTimer.IsEnabled) _limitTimer.Start();
    }

    public async Task RefreshAsync()
    {
        _loading = true;
        try
        {
            var s = _settings.Load();
            var state = _settings.LoadState();
            IsPaused = s.Paused;
            UpdateLimit(state);
            NextChangeText = DescribeNextChange(s, state);

            var latest = await Task.Run(() => _history.List().FirstOrDefault());
            HasCurrent = latest is not null;
            if (latest is not null)
            {
                IsFavorite = latest.IsFavorite;
                PhotographerName = latest.PhotographerName;
                PhotographerUri = new Uri(latest.PhotographerUrl);
                PhotoPageUri = new Uri(latest.PhotoPageUrl);
                Caption = $"{Cap(latest.Description ?? latest.Topic ?? "")}  ·  {latest.AppliedAt.LocalDateTime:ddd, d MMM HH:mm}";
                if (_previewWanted && (latest.Id != _currentId || latest.FilePath != _currentFile))
                {
                    _currentId = latest.Id;
                    _currentFile = latest.FilePath;
                    var old = Preview;
                    Preview = await ImageLoader.LoadAsync(latest.FilePath, latest.ThumbnailUrl, 1200);
                    old?.Dispose();
                }
            }

            if (state.LastError is { } error && !IsBusy)
                Info.Show(InfoSeverity.Warning, "Last change didn't work", RetryText(error, state));
            else if (Info.IsOpen && Info.Severity != InfoSeverity.Success)
                Info.IsOpen = false;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Called when the Home page is shown: the preview image is worth loading again.</summary>
    public Task ActivateAsync()
    {
        _previewWanted = true;
        return RefreshAsync();
    }

    /// <summary>Window closed: free the preview bitmap.</summary>
    public void ReleaseImages()
    {
        _previewWanted = false;
        var old = Preview;
        Preview = null;
        old?.Dispose();
        _currentId = null;
        _currentFile = null;
    }

    public async Task<ApplyResult> ChangeAsync()
    {
        if (IsBusy) return new ApplyResult(ApplyStatus.Busy, []);
        IsBusy = true;
        Info.IsOpen = false;
        try
        {
            var result = await Task.Run(() => _wallpapers.ApplyAsync(force: true));
            ShowResult(result);
            return result;
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    Task ChangeNow() => ChangeAsync();

    public void ShowResult(ApplyResult result)
    {
        switch (result.Status)
        {
            case ApplyStatus.Failed:
                var isKeyProblem = result.ErrorKind is ProviderErrorKind.MissingKey or ProviderErrorKind.Unauthorized;
                Info.Show(isKeyProblem ? InfoSeverity.Error : InfoSeverity.Warning,
                    "Couldn't change the wallpaper", $"{result.Error} Your current wallpaper was kept.");
                break;
            case ApplyStatus.Busy:
                Info.Show(InfoSeverity.Informational, "Hang on", result.Error ?? "Already changing.");
                break;
            case ApplyStatus.RateLimited:
                Info.Show(InfoSeverity.Informational, "Slow down a little", result.Error ?? "Try again later.");
                break;
        }
    }

    [RelayCommand]
    void OpenInFolder()
    {
        if (_currentFile is not null) Launcher.ShowInFileManager(_currentFile);
        else Launcher.OpenFolder(_paths.WallpaperFolder(_settings.Load()));
    }

    [RelayCommand]
    static void OpenUrl(Uri? uri)
    {
        if (uri is not null) Launcher.OpenUrl(uri);
    }

    partial void OnIsFavoriteChanged(bool value)
    {
        if (!_loading && _currentId is { } id) _history.SetFavorite(id, value);
    }

    partial void OnIsPausedChanged(bool value)
    {
        if (!_loading) _settings.Update(s => s.Paused = value);
    }

    static string RetryText(string error, OrielState state) =>
        state.NextRetryAt is { } at && at > DateTimeOffset.Now
            ? $"{error} Retrying automatically at {at.LocalDateTime:HH:mm}."
            : error;

    static string DescribeNextChange(OrielSettings s, OrielState state)
    {
        if (s.Paused) return "Paused. Your wallpaper stays as it is.";
        var time = s.GetDailyTime();
        var now = DateTime.Now;
        if (state.LastAppliedDate == DateOnly.FromDateTime(now)) return $"Tomorrow at {time:HH:mm}";
        if (state.NextRetryAt is { } retry && retry > DateTimeOffset.Now) return $"Retrying at {retry.LocalDateTime:HH:mm}";
        return TimeOnly.FromDateTime(now) < time ? $"Today at {time:HH:mm}" : "Any minute now";
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
