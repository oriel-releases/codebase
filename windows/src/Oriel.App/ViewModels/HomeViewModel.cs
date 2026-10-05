using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core;
using Oriel.Core.Providers;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Storage;
using Oriel.UI.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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

    public HomeViewModel(WallpaperService wallpapers, IHistoryStore history, ISettingsStore settings,
        AppPaths paths)
    {
        _wallpapers = wallpapers;
        _history = history;
        _settings = settings;
        _paths = paths;
        _settings.SettingsChanged += (_, _) => App.Dispatcher.TryEnqueue(() => _ = RefreshAsync());
    }

    [ObservableProperty] public partial ImageSource? Preview { get; set; }
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
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }
    public bool IsIdle => !IsBusy;

    [ObservableProperty] public partial bool IsInfoOpen { get; set; }
    [ObservableProperty] public partial string InfoTitle { get; set; } = "";
    [ObservableProperty] public partial string InfoMessage { get; set; } = "";
    [ObservableProperty] public partial InfoBarSeverity InfoSeverity { get; set; }

    public async Task RefreshAsync()
    {
        _loading = true;
        try
        {
            var s = _settings.Load();
            var state = _settings.LoadState();
            IsPaused = s.Paused;
            NextChangeText = DescribeNextChange(s, state);

            var latest = _history.List().FirstOrDefault();
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
                    Preview = await ImageLoader.LoadAsync(latest.FilePath, latest.ThumbnailUrl, 1200);
                }
            }

            if (state.LastError is { } error && !IsBusy)
                ShowInfo(InfoBarSeverity.Warning, "Last change didn't work", RetryText(error, state));
            else if (IsInfoOpen && InfoSeverity != InfoBarSeverity.Success)
                IsInfoOpen = false;
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

    /// <summary>Window closed: let the preview bitmap be collected.</summary>
    public void ReleaseImages()
    {
        _previewWanted = false;
        Preview = null;
        _currentId = null;
        _currentFile = null;
    }

    public async Task<ApplyResult> ChangeAsync()
    {
        if (IsBusy) return new ApplyResult(ApplyStatus.Busy, []);
        IsBusy = true;
        IsInfoOpen = false;
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

    [RelayCommand]
    Task ChangeNow() => ChangeAsync();

    public void ShowResult(ApplyResult result)
    {
        switch (result.Status)
        {
            case ApplyStatus.Failed:
                var isKeyProblem = result.ErrorKind is ProviderErrorKind.MissingKey or ProviderErrorKind.Unauthorized;
                ShowInfo(isKeyProblem ? InfoBarSeverity.Error : InfoBarSeverity.Warning,
                    "Couldn't change the wallpaper", $"{result.Error} Your current wallpaper was kept.");
                break;
            case ApplyStatus.Busy:
                ShowInfo(InfoBarSeverity.Informational, "Hang on", result.Error ?? "Already changing.");
                break;
        }
    }

    [RelayCommand]
    void OpenInFolder()
    {
        if (_currentFile is not null) ImageLoader.ShowInExplorer(_currentFile);
        else ImageLoader.OpenFolder(_paths.WallpaperFolder(_settings.Load()));
    }

    partial void OnIsFavoriteChanged(bool value)
    {
        if (!_loading && _currentId is { } id) _history.SetFavorite(id, value);
    }

    partial void OnIsPausedChanged(bool value)
    {
        if (!_loading) _settings.Update(s => s.Paused = value);
    }

    void ShowInfo(InfoBarSeverity severity, string title, string message)
    {
        InfoSeverity = severity;
        InfoTitle = title;
        InfoMessage = message;
        IsInfoOpen = true;
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
