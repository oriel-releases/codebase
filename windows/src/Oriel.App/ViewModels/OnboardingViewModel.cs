using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.Core.Storage;

namespace Oriel.UI.ViewModels;

/// <summary>One interest on the "What do you like?" step.</summary>
public sealed partial class InterestChip(string query, bool selected, Action changed) : ObservableObject
{
    public string Query { get; } = query;
    public string Label => char.ToUpper(Query[0]) + Query[1..];
    public string ChipText => $"{TopicViewModel.EmojiFor(Query)}  {Label}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSelected))]
    public partial bool IsSelected { get; set; } = selected;
    public bool IsNotSelected => !IsSelected;

    partial void OnIsSelectedChanged(bool value) => changed();

    /// <summary>The tile was clicked: picked becomes unpicked and back.</summary>
    [RelayCommand]
    void Toggle() => IsSelected = !IsSelected;

    /// <summary>The tile's photo (the same as on the Interests page).</summary>
    public string? CoverUrl => InterestCovers.Url(Query, 400);

    Microsoft.UI.Xaml.Media.ImageSource? _cover;
    public Microsoft.UI.Xaml.Media.ImageSource? Cover => _cover ??= CoverUrl is { } url
        ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(url)) { DecodePixelWidth = 280, DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical }
        : null;
}

/// <summary>One "how often" choice. Behaves like a radio button made of chips: exactly one is always on.</summary>
public sealed partial class ScheduleChoice(string label, int minutes, Action<ScheduleChoice, bool> changed) : ObservableObject
{
    public string Label { get; } = label;
    public int Minutes { get; } = minutes;

    /// <summary>One line under the choice, so the difference is clear at a glance.</summary>
    public string Description => Minutes switch
    {
        0 => "A new photo every day, at the time you pick.",
        60 => "Something new all day long.",
        180 => "Morning, afternoon and evening.",
        360 => "A few times a day.",
        _ => "Day and night.",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSelected), nameof(ShowsTime))]
    public partial bool IsSelected { get; set; }
    public bool IsNotSelected => !IsSelected;
    /// <summary>"Once a day", chosen: its card opens a second row with the time.</summary>
    public bool ShowsTime => IsSelected && Minutes == 0;

    partial void OnIsSelectedChanged(bool value) => changed(this, value);

    /// <summary>The card was clicked: this becomes the choice.</summary>
    [RelayCommand]
    void Select() => IsSelected = true;
}

/// <summary>
/// The first-run tour shown once after signing in: what Oriel does, then a few questions (interests, how often,
/// how it should look) and a first wallpaper. Nothing is saved until the end, so skipping leaves every default alone.
/// </summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    public const int LastStep = 4;

    readonly ISettingsStore _settings;
    readonly IHistoryStore _history;
    readonly HomeViewModel _home;
    readonly WallpaperService _wallpapers;
    bool _loading = true;
    bool _dismissed;
    readonly SharedPhotoViewModel _shared;

    public OnboardingViewModel(ISettingsStore settings, IHistoryStore history, HomeViewModel home, SharedPhotoViewModel shared
        , WallpaperService wallpapers
        )
    {
        _settings = settings;
        _history = history;
        _home = home;
        _shared = shared;
        _wallpapers = wallpapers;

        var s = settings.Load();
        foreach (var t in s.Topics.Where(t => !t.IsCustom && !string.IsNullOrWhiteSpace(t.Query)))
            Interests.Add(new InterestChip(t.Query, t.Enabled, OnInterestChanged));

        foreach (var (label, minutes) in new[]
                 { ("Once a day", 0), ("Every hour", 60), ("Every 3 hours", 180), ("Every 6 hours", 360), ("Every 12 hours", 720) })
            Schedules.Add(new ScheduleChoice(label, minutes, OnScheduleChanged));
        var current = Schedules.FirstOrDefault(c => c.Minutes == s.IntervalMinutes) ?? Schedules[0];
        _selected = current;
        current.IsSelected = true;

        DailyTime = s.GetDailyTime().ToTimeSpan();
        ChangeLockScreen = s.ChangeLockScreen;
        MatchAccentColor = s.MatchAccentColor;
        RunInBackground = s.StartWithWindows;
        Shortcut = s.Hotkey;
        _loading = false;
        OnInterestChanged();
    }

    public ObservableCollection<InterestChip> Interests { get; } = [];
    public ObservableCollection<ScheduleChoice> Schedules { get; } = [];
    ScheduleChoice _selected;

    /// <summary>Raised when the tour is over (finished or skipped), so the window can show the app.</summary>
    public event EventHandler? Finished;

    /// <summary>False once the tour was done or skipped. Installs that already have wallpapers are existing users: no tour.</summary>
    public bool ShouldShow()
    {
        if (_dismissed) return false;
        // Dev: ORIEL_ONBOARDING=1 replays the tour on an install that already has wallpapers.
        if (Environment.GetEnvironmentVariable("ORIEL_ONBOARDING") == "1") return true;
        if (_settings.Load().OnboardingDone) return false;
        if (_history.List().Count == 0) return true;
        _settings.Update(s => s.OnboardingDone = true);
        return false;
    }

    // ---- Steps: 0 welcome, 1 interests, 2 schedule, 3 preferences, 4 done ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsInterests), nameof(IsSchedule), nameof(IsPreferences), nameof(IsDone),
        nameof(ShowBack), nameof(ShowSkip), nameof(ShowNext), nameof(ShowProgress), nameof(NextText), nameof(StepText), nameof(StepNumber),
        nameof(Progress), nameof(StepShort))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    public partial int Step { get; set; }

    public bool IsWelcome => Step == 0;
    public bool IsInterests => Step == 1;
    public bool IsSchedule => Step == 2;
    public bool IsPreferences => Step == 3;
    public bool IsDone => Step == LastStep;
    public bool ShowBack => Step is >= 1 and <= 3;
    public bool ShowSkip => Step < LastStep;
    public bool ShowNext => Step < LastStep;
    public bool ShowProgress => Step is >= 1 and <= 3;
    public string NextText => Step == 0 ? "Get started" : "Next";
    public int StepNumber => Step;
    public string StepText => $"Step {Step} of 3";
    /// <summary>"1/3", beside the progress line.</summary>
    public string StepShort => $"{Step}/3";
    /// <summary>How full the progress line is: 1/3, 2/3, 1 on the question steps.</summary>
    public double Progress => Math.Clamp(Step / 3.0, 0, 1);

    /// <summary>The photos across the top of the welcome step.</summary>
    public static readonly string[] HeroInterests = ["mountains", "northern lights", "ocean"];
    public IReadOnlyList<Microsoft.UI.Xaml.Media.ImageSource> HeroPhotos { get; } = HeroInterests
        .Select(i => (Microsoft.UI.Xaml.Media.ImageSource)new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(InterestCovers.Url(i, 600)!))
        {
            DecodePixelWidth = 360,
            DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical,
        })
        .ToList();
    public Microsoft.UI.Xaml.Media.ImageSource Hero1 => HeroPhotos[0];
    public Microsoft.UI.Xaml.Media.ImageSource Hero2 => HeroPhotos[1];
    public Microsoft.UI.Xaml.Media.ImageSource Hero3 => HeroPhotos[2];

    // ---- Interests ----

    [ObservableProperty] public partial string InterestsHint { get; set; } = "";
    public bool HasInterests => Interests.Any(i => i.IsSelected);

    void OnInterestChanged()
    {
        if (_loading) return;
        var n = Interests.Count(i => i.IsSelected);
        InterestsHint = n == 0 ? "Pick at least one to continue." : n == 1 ? "1 picked. Pick a few more for variety." : $"{n} picked. Nice mix.";
        OnPropertyChanged(nameof(HasInterests));
        NextCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    void SelectAllInterests(bool all)
    {
        foreach (var i in Interests) i.IsSelected = all;
    }

    // ---- Schedule ----

    public bool IsDaily => _selected.Minutes == 0;

    [ObservableProperty] public partial TimeSpan DailyTime { get; set; }

    void OnScheduleChanged(ScheduleChoice choice, bool on)
    {
        if (on)
        {
            _selected = choice;
            foreach (var other in Schedules.Where(c => c != choice && c.IsSelected)) other.IsSelected = false;
            OnPropertyChanged(nameof(IsDaily));
        }
        else if (choice == _selected)
        {
            choice.IsSelected = true; // clicking the chosen one again keeps it: there is always an answer
        }
    }

    // ---- Preferences ----

    [ObservableProperty] public partial bool ChangeLockScreen { get; set; }
    [ObservableProperty] public partial bool MatchAccentColor { get; set; }
    [ObservableProperty] public partial bool RunInBackground { get; set; }
    public string Shortcut { get; }
    public string Tip => string.IsNullOrEmpty(Shortcut)
        ? "Oriel lives in your tray: open it from there any time."
        : $"Tip: press {Shortcut} to change your wallpaper from anywhere. Oriel lives in your tray.";

    // ---- Done: what was chosen ----

    [ObservableProperty] public partial string SummaryInterests { get; set; } = "";
    [ObservableProperty] public partial string SummarySchedule { get; set; } = "";
    [ObservableProperty] public partial string SummaryLook { get; set; } = "";
    /// <summary>The last button: the first wallpaper, or the one a friend shared when the person came from a share link.</summary>
    [ObservableProperty] public partial string FirstWallpaperText { get; set; } = "Set my first wallpaper";

    void BuildSummary()
    {
        FirstWallpaperText = _shared.IsOpen ? "See the shared wallpaper" : "Set my first wallpaper";
        var picked = Interests.Where(i => i.IsSelected).Select(i => i.Label).ToList();
        SummaryInterests = picked.Count <= 3 ? string.Join(", ", picked) : $"{string.Join(", ", picked.Take(3))} and {picked.Count - 3} more";
        SummarySchedule = _selected.Minutes == 0 ? $"Every day at {DailyTime.Hours:00}:{DailyTime.Minutes:00}" : _selected.Label;
        var look = new List<string> { ChangeLockScreen ? "Desktop and lock screen" : "Desktop only" };
        if (MatchAccentColor) look.Add("matching accent color");
        SummaryLook = string.Join(", ", look);
    }

    // ---- Navigation ----

    bool CanGoNext() => Step switch { 1 => HasInterests, < LastStep => true, _ => false };
    bool CanGoBack() => Step is >= 1 and <= 3;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    void Next()
    {
        if (Step == 3) BuildSummary();
        Step++;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    void Back() => Step--;

    /// <summary>"Skip setup": keep every default and go to the app.</summary>
    [RelayCommand]
    void Skip()
    {
        _settings.Update(s => s.OnboardingDone = true);
        _dismissed = true;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves the answers. <paramref name="changeNow"/> also sets the first wallpaper.</summary>
    [RelayCommand]
    void Finish(bool changeNow)
    {
        var picked = Interests.Where(i => i.IsSelected).Select(i => i.Query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _settings.Update(s =>
        {
            foreach (var t in s.Topics.Where(t => !t.IsCustom)) t.Enabled = picked.Contains(t.Query);
            s.IntervalMinutes = _selected.Minutes;
            s.DailyTime = $"{DailyTime.Hours:00}:{DailyTime.Minutes:00}";
            s.ChangeLockScreen = ChangeLockScreen;
            s.MatchAccentColor = MatchAccentColor;
            s.StartWithWindows = RunInBackground;
            s.OnboardingDone = true;
        });
        // Turning it on takes effect right away if there is already a wallpaper on screen.
        if (MatchAccentColor) _ = Task.Run(_wallpapers.ApplyAccentToCurrentAsync);
        _dismissed = true;
        Finished?.Invoke(this, EventArgs.Empty);
        // A shared photo waiting to be set is the first wallpaper: its card shows next, so no other change.
        if (changeNow && !_shared.IsOpen) _ = _home.ChangeAsync();
    }
}
