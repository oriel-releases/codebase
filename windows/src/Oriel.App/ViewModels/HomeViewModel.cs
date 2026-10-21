using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core;
using Oriel.Core.Models;
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

    readonly Oriel.Core.Auth.AuthService _auth;
    readonly TodayService _today;
    TodayPhoto? _todayPhoto;
    DateOnly? _todayLoadedFor;

    public HomeViewModel(WallpaperService wallpapers, IHistoryStore history, ISettingsStore settings,
        AppPaths paths, Oriel.Core.Auth.AuthService auth, TodayService today, ShareService shares)
    {
        _shares = shares;
        _today = today;
        _auth = auth;
        _wallpapers = wallpapers;
        _history = history;
        _settings = settings;
        _paths = paths;
        _settings.SettingsChanged += (_, _) => App.Dispatcher.TryEnqueue(() => _ = RefreshAsync());
        // Whatever is changing the wallpaper (this button, the schedule, the tray, another device), the button says so.
        _wallpapers.PhaseChanged += (_, p) => App.Dispatcher.TryEnqueue(() => OnPhase(p));
        // An accent color chosen here or on another device: its swatch gets the ring.
        _wallpapers.AccentChosen += (_, _) => App.Dispatcher.TryEnqueue(MarkChosenAccent);
    }

    // ---- The accent color: the photo's top three, one of them chosen ----

    public System.Collections.ObjectModel.ObservableCollection<AccentSwatchViewModel> Accents { get; } = [];
    [ObservableProperty] public partial bool HasAccents { get; set; }
    HistoryEntry? _accentEntry;

    async Task LoadAccentsAsync(HistoryEntry entry)
    {
        _accentEntry = entry;
        var options = await _wallpapers.AccentOptionsAsync(entry);
        if (_accentEntry != entry) return; // a newer photo came meanwhile
        Accents.Clear();
        foreach (var hex in options) Accents.Add(new AccentSwatchViewModel(this, hex));
        // The chosen color may be one picked from another device's copy: shown first when it isn't among these.
        if (_wallpapers.ChosenAccent(entry.PhotoId) is { } chosen && !Accents.Any(a => a.Is(chosen)))
        {
            Accents.Insert(0, new AccentSwatchViewModel(this, chosen));
            if (Accents.Count > 3) Accents.RemoveAt(3);
        }
        HasAccents = Accents.Count > 0;
        MarkChosenAccent();
    }

    void MarkChosenAccent()
    {
        var chosen = _accentEntry is { } e ? _wallpapers.ChosenAccent(e.PhotoId) : null;
        for (var i = 0; i < Accents.Count; i++)
            Accents[i].IsChosen = chosen is null ? i == 0 : Accents[i].Is(chosen);
    }

    internal async Task ChooseAccentAsync(AccentSwatchViewModel swatch)
    {
        if (_accentEntry is not { } entry || swatch.IsChosen) return;
        foreach (var a in Accents) a.IsChosen = a == swatch;
        await Task.Run(() => _wallpapers.ChooseAccentAsync(entry, swatch.Hex));
    }

    [ObservableProperty] public partial ImageSource? Preview { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoCurrent))]
    public partial bool HasCurrent { get; set; }
    public bool HasNoCurrent => !HasCurrent;
    [ObservableProperty] public partial string PhotographerName { get; set; } = "";
    /// <summary>An Unsplash photo ("Photo by … on Unsplash"), not one made with AI on the Create page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CreditLead))]
    public partial bool IsPhoto { get; set; } = true;
    public string CreditLead => IsPhoto ? "Photo by" : "Made with";
    [ObservableProperty] public partial Uri? PhotographerUri { get; set; }
    [ObservableProperty] public partial Uri? PhotoPageUri { get; set; }
    [ObservableProperty] public partial string Caption { get; set; } = "";

    readonly ShareService _shares;
    /// <summary>The photo on Home, when it can be shared (only Unsplash photos have a page to show).</summary>
    HistoryEntry? _shareEntry;
    [ObservableProperty] public partial bool HasShareLink { get; set; }

    /// <summary>
    /// The short link that opens Oriel on a friend's computer and sets this photo (made on the first share). Null when
    /// it couldn't be made: Home says why.
    /// </summary>
    public async Task<string?> CreateShareLinkAsync()
    {
        if (_shareEntry is not { } entry) return null;
        try
        {
            return await _shares.LinkAsync(entry);
        }
        catch (ShareException ex)
        {
            ShowInfo(InfoBarSeverity.Warning, "Couldn't share", ex.Message);
            return null;
        }
    }
    [ObservableProperty] public partial bool IsLinkCopied { get; set; }


    /// <summary>The view copied the link: show a check mark for a moment.</summary>
    public async Task MarkLinkCopiedAsync()
    {
        IsLinkCopied = true;
        await Task.Delay(2000);
        IsLinkCopied = false;
    }
    [ObservableProperty] public partial bool IsFavorite { get; set; }
    [ObservableProperty] public partial bool IsPaused { get; set; }
    [ObservableProperty] public partial string NextChangeText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanChange), nameof(ShowChangeIcon), nameof(ShowLimitIcon), nameof(ChangeLabel))]
    public partial bool IsBusy { get; set; }

    /// <summary>What the change under way is doing: "Finding a photo…", "Downloading…", "Changing…", "Syncing…", "Receiving…".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangeLabel))]
    public partial string? StageText { get; set; }

    void OnPhase(ApplyPhase phase)
    {
        if (phase == ApplyPhase.Done)
        {
            // No refresh here: the Applied event already updates Home (one preview decode per change, not three).
            StageText = null;
            IsBusy = false;
            return;
        }
        StageText = PhaseText.For(phase);
        IsBusy = true;
    }
    public bool IsIdle => !IsBusy;

    // "Change now" is limited per hour: until it works again the button counts down, e.g. "Try after 20 min".
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChange), nameof(ShowChangeIcon), nameof(ShowLimitIcon), nameof(ChangeLabel))]
    public partial int LimitMinutes { get; set; }
    bool IsLimited => LimitMinutes > 0;
    public bool CanChange => IsIdle && !IsLimited;
    public bool ShowChangeIcon => IsIdle && !IsLimited;
    public bool ShowLimitIcon => IsIdle && IsLimited;
    public string ChangeLabel => IsBusy && StageText is { } stage ? stage : IsLimited ? $"Try after {LimitMinutes} min" : "Change now";
    Microsoft.UI.Dispatching.DispatcherQueueTimer? _limitTimer;

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
            _limitTimer = App.Dispatcher.CreateTimer();
            _limitTimer.Interval = TimeSpan.FromSeconds(15);
            _limitTimer.Tick += (_, _) => UpdateLimit(_settings.LoadState());
        }
        if (!_limitTimer.IsRunning) _limitTimer.Start();
    }

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
            UpdateLimit(state);
            NextChangeText = DescribeNextChange(s, state);
            if (_todayPhoto is not null) UpdateTodayCurrent();

            var latest = _history.List().FirstOrDefault();
            HasCurrent = latest is not null;
            if (latest is not null)
            {
                IsFavorite = latest.IsFavorite;
                IsPhoto = latest.Provider != PollinationsClient.Provider;
                PhotographerName = latest.PhotographerName;
                PhotographerUri = new Uri(latest.PhotographerUrl);
                PhotoPageUri = new Uri(latest.PhotoPageUrl);
                _shareEntry = ShareService.CanShare(latest) ? latest : null;
                HasShareLink = _shareEntry is not null;
                Caption = $"{Cap(latest.Description ?? latest.Topic ?? "")}  ·  {latest.AppliedAt.LocalDateTime:ddd, d MMM HH:mm}";
                if (_previewWanted && (latest.Id != _currentId || latest.FilePath != _currentFile))
                {
                    _currentId = latest.Id;
                    _currentFile = latest.FilePath;
                    Preview = await ImageLoader.LoadAsync(latest.FilePath, latest.ThumbnailUrl, 1200);
                    await LoadAccentsAsync(latest);
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
        _ = LoadTodayAsync();
        return RefreshAsync();
    }

    // ---------------------------------------------------------------- Wallpaper of the day

    [ObservableProperty] public partial bool HasToday { get; set; }
    /// <summary>Today's photo for its own page (the card opens it).</summary>
    [ObservableProperty] public partial TodayDetail? Today { get; set; }
    [ObservableProperty] public partial ImageSource? TodayThumb { get; set; }
    [ObservableProperty] public partial string TodayBy { get; set; } = "";
    [ObservableProperty] public partial Uri? TodayAuthorUri { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TodayButtonText), nameof(CanSetToday))]
    [NotifyCanExecuteChangedFor(nameof(SetTodayCommand))]
    public partial bool IsTodaySetting { get; set; }
    /// <summary>Today's photo is already the wallpaper.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TodayButtonText), nameof(CanSetToday))]
    [NotifyCanExecuteChangedFor(nameof(SetTodayCommand))]
    public partial bool IsTodayCurrent { get; set; }
    public bool CanSetToday => !IsTodaySetting && !IsTodayCurrent;
    /// <summary>What setting the Wallpaper of the day is doing now: "Downloading…", "Changing…", "Syncing…".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TodayButtonText))]
    public partial string? TodayStage { get; set; }

    /// <summary>Under "Wallpaper of the day" on the card: whose photo, or that it's already the wallpaper.</summary>
    public string TodayCaption => IsTodaySetting ? TodayStage ?? "Finding it…" : IsTodayCurrent ? "On your desktop ✓" : $"Photo by {TodayBy}";
    partial void OnTodayByChanged(string value) => OnPropertyChanged(nameof(TodayCaption));
    partial void OnIsTodaySettingChanged(bool value) => OnPropertyChanged(nameof(TodayCaption));
    partial void OnIsTodayCurrentChanged(bool value) => OnPropertyChanged(nameof(TodayCaption));
    partial void OnTodayStageChanged(string? value) => OnPropertyChanged(nameof(TodayCaption));
    public string TodayButtonText => IsTodaySetting ? TodayStage ?? "Finding it…" : IsTodayCurrent ? "On your desktop ✓" : "Set as wallpaper";

    /// <summary>Today's photo from the website, once a day (the thumbnail is small: 240 px wide).</summary>
    async Task LoadTodayAsync()
    {
        var date = DateOnly.FromDateTime(DateTime.Now);
        if (_todayLoadedFor == date) return;
        _todayLoadedFor = date;
        var photo = await Task.Run(() => _today.GetAsync());
        if (photo is null)
        {
            _todayLoadedFor = null;
            return;
        }
        _todayPhoto = photo;
        TodayBy = photo.By;
        TodayAuthorUri = photo.AuthorUri;
        TodayThumb = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(photo.ThumbnailUrl(480))) { DecodePixelWidth = 240 };
        Today = new TodayDetail(this, photo, TodayThumb);
        HasToday = true;
        UpdateTodayCurrent();
    }

    void UpdateTodayCurrent() =>
        IsTodayCurrent = _todayPhoto is { } p && _history.List().FirstOrDefault()?.PhotoId == p.Id;

    [RelayCommand(CanExecute = nameof(CanSetToday))]
    async Task SetToday()
    {
        if (_todayPhoto is not { } today) return;
        IsTodaySetting = true;
        try
        {
            var result = await PhaseWatch.RunAsync(_wallpapers, () => Task.Run(async () =>
            {
                var photo = await _wallpapers.GetSharedPhotoAsync(today.Id);
                return await _wallpapers.ApplyPhotoAsync(photo, "wallpaper of the day");
            }), stage => App.Dispatcher.TryEnqueue(() => TodayStage = stage));
            ShowResult(result);
        }
        catch (Oriel.Core.Providers.ProviderException ex)
        {
            ShowResult(new ApplyResult(ApplyStatus.Failed, [], ex.Message, ex.Kind));
        }
        finally
        {
            IsTodaySetting = false;
            UpdateTodayCurrent();
        }
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
            // Still sharing it with the other devices: "Syncing…" stays until that's done (OnPhase).
            if (StageText is null) IsBusy = false;
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
            case ApplyStatus.RateLimited:
                ShowInfo(InfoBarSeverity.Informational, "Slow down a little", result.Error ?? "Try again later.");
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
        if (s.IsFollowing) return state.FollowProblem ?? $"Showing {s.FollowName}'s wallpaper, as it changes.";
        if (s.Paused) return "Paused. Your wallpaper stays as it is.";
        var time = s.GetDailyTime();
        var now = DateTime.Now;
        if (state.LastAppliedDate == DateOnly.FromDateTime(now)) return $"Tomorrow at {time:HH:mm}";
        if (state.NextRetryAt is { } retry && retry > DateTimeOffset.Now) return $"Retrying at {retry.LocalDateTime:HH:mm}";
        return TimeOnly.FromDateTime(now) < time ? $"Today at {time:HH:mm}" : "Any minute now";
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}

/// <summary>One of the photo's colors on Home; choosing it makes it the accent here and on the account's other devices.</summary>
public sealed partial class AccentSwatchViewModel(HomeViewModel owner, string hex) : ObservableObject
{
    public string Hex { get; } = hex.ToUpperInvariant();
    public Microsoft.UI.Xaml.Media.SolidColorBrush Brush { get; } = new(Windows.UI.Color.FromArgb(0xFF,
        Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16)));
    public string Tip => $"Accent color {Hex}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RingOpacity))]
    public partial bool IsChosen { get; set; }
    /// <summary>The ring around the chosen color.</summary>
    public double RingOpacity => IsChosen ? 1 : 0;

    public bool Is(string other) => string.Equals(Hex, other, StringComparison.OrdinalIgnoreCase);

    [RelayCommand] Task Choose() => owner.ChooseAccentAsync(this);
}
