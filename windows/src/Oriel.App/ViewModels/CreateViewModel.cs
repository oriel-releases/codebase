using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core.Models;
using Oriel.Core.Providers;
using Oriel.Core.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Oriel.UI.ViewModels;

/// <summary>One look on the Create page; the chosen one is filled in.</summary>
public sealed partial class CreateStyleViewModel(CreateViewModel owner, CreateStyle style) : ObservableObject
{
    public CreateStyle Style { get; } = style;
    public string Label => Style.Label;
    [ObservableProperty] public partial bool IsSelected { get; set; }

    /// <summary>Clicking a look picks it; clicking the chosen one again keeps it chosen.</summary>
    partial void OnIsSelectedChanged(bool value)
    {
        if (value) owner.PickStyle(this);
        else if (!owner.IsPicking) IsSelected = true;
    }
}

/// <summary>
/// Create: words in, an AI wallpaper out (Pollinations). The picture is shown first; it becomes the wallpaper only
/// when asked; then it syncs to the account's other devices like any photo.
/// </summary>
public sealed partial class CreateViewModel : ObservableObject
{
    readonly PollinationsClient _ai;
    readonly WallpaperService _wallpapers;
    (PhotoCandidate Photo, byte[] Image)? _created;
    string _createdPrompt = "";
    string? _createdStyle;

    public CreateViewModel(PollinationsClient ai, WallpaperService wallpapers)
    {
        _ai = ai;
        _wallpapers = wallpapers;
        foreach (var style in CreateIdeas.Styles) Styles.Add(new CreateStyleViewModel(this, style));
        PickStyle(Styles[0]);
        UpdateLeft();
    }

    public ObservableCollection<CreateStyleViewModel> Styles { get; } = [];
    public ObservableCollection<string> Ideas { get; } = new(CreateIdeas.Pick());

    /// <summary>Coming to Create: new ideas, and how many are left today.</summary>
    [RelayCommand]
    public void ShuffleIdeas()
    {
        Ideas.Clear();
        foreach (var idea in CreateIdeas.Pick()) Ideas.Add(idea);
        UpdateLeft();
    }

    /// <summary>"18 of 20 left today", on the Create button.</summary>
    [ObservableProperty] public partial string LeftText { get; set; } = "";

    void UpdateLeft() => LeftText = PollinationsClient.IsConfigured
        ? $"{_ai.LeftToday} of {PollinationsClient.MaxPerDay} left today"
        : "This build of Oriel can't create AI wallpapers.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string PromptText { get; set; } = "";

    /// <summary>New words: the picture on screen is no longer "the one just set".</summary>
    partial void OnPromptTextChanged(string value) => IsApplied = false;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand), nameof(TryAgainCommand), nameof(SetCommand), nameof(LuckyCommand))]
    [NotifyPropertyChangedFor(nameof(ShowIdeas), nameof(ShowPicture))]
    public partial bool IsCreating { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIdeas), nameof(ShowPicture))]
    [NotifyCanExecuteChangedFor(nameof(TryAgainCommand), nameof(SetCommand))]
    public partial bool HasPicture { get; set; }
    public bool ShowPicture => HasPicture && !IsCreating;
    /// <summary>Ideas until there's a picture (or one is being made).</summary>
    public bool ShowIdeas => !HasPicture && !IsCreating;

    /// <summary>"Imagining…" while the words are written out as a full description, then "Painting…".</summary>
    [ObservableProperty] public partial string CreatingText { get; set; } = "Painting…";

    [ObservableProperty] public partial ImageSource? Picture { get; set; }
    [ObservableProperty] public partial string PictureCaption { get; set; } = "";

    internal bool IsPicking { get; private set; }

    internal void PickStyle(CreateStyleViewModel style)
    {
        if (IsPicking) return;
        IsPicking = true;
        foreach (var s in Styles) s.IsSelected = s == style;
        IsPicking = false;
    }

    string? ChosenStyle => Styles.FirstOrDefault(s => s.IsSelected)?.Style.Words;

    bool CanCreate() => !IsCreating && !string.IsNullOrWhiteSpace(PromptText);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    Task Create() => CreateAsync(PromptText.Trim(), ChosenStyle);

    /// <summary>An idea fills in the words, to change or create as they are.</summary>
    [RelayCommand]
    void UseIdea(string? idea)
    {
        if (!string.IsNullOrWhiteSpace(idea)) PromptText = idea;
    }

    bool CanBeLucky() => !IsCreating;

    /// <summary>"I'm feeling lucky": an idea and a look at random, made right away.</summary>
    [RelayCommand(CanExecute = nameof(CanBeLucky))]
    Task Lucky()
    {
        var idea = CreateIdeas.Any();
        PromptText = idea;
        PickStyle(Styles[Random.Shared.Next(Styles.Count)]);
        return CreateAsync(idea, ChosenStyle);
    }

    bool CanTryAgain() => !IsCreating && HasPicture;

    /// <summary>The same words and look, another picture.</summary>
    [RelayCommand(CanExecute = nameof(CanTryAgain))]
    Task TryAgain() => CreateAsync(_createdPrompt, _createdStyle);

    async Task CreateAsync(string prompt, string? style)
    {
        IsInfoOpen = false;
        // The last picture goes now: it never shows again before the new one.
        HasPicture = false;
        Picture = null;
        IsCreating = true;
        IsApplied = false;
        try
        {
            var screen = _wallpapers.ScreenSize();
            CreatingText = "Imagining…";
            var made = await Task.Run(() => _ai.CreateAsync(prompt, style, PollinationsClient.NewSeed(), screen,
                step => App.Dispatcher.TryEnqueue(() => CreatingText = step)));
            var bitmap = new BitmapImage { DecodePixelWidth = 1200, DecodePixelType = DecodePixelType.Logical };
            using (var ms = new MemoryStream(made.Image))
                await bitmap.SetSourceAsync(ms.AsRandomAccessStream());
            _created = made;
            _createdPrompt = prompt;
            _createdStyle = style;
            PictureCaption = char.ToUpper(prompt[0]) + prompt[1..];
            // The picture area shows first, then the picture arrives in it: its reveal plays on screen, like every photo's.
            IsCreating = false;
            HasPicture = true;
            await Task.Delay(30);
            Picture = bitmap;
        }
        catch (ProviderException ex)
        {
            ShowInfo(ex.Kind is ProviderErrorKind.RateLimited or ProviderErrorKind.Network ? InfoBarSeverity.Warning : InfoBarSeverity.Error,
                "Couldn't create it", ex.Message);
        }
        catch (Exception ex)
        {
            ShowInfo(InfoBarSeverity.Error, "Couldn't create it", $"Something went wrong: {ex.Message}");
        }
        finally
        {
            IsCreating = false;
            UpdateLeft();
        }
    }

    // ---- Setting it ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetText))]
    [NotifyCanExecuteChangedFor(nameof(SetCommand))]
    public partial bool IsSetting { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetText))]
    public partial bool IsApplied { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetText))]
    public partial string? Stage { get; set; }
    public string SetText => IsSetting ? Stage ?? "Changing…" : IsApplied ? "Wallpaper set ✓" : "Set as wallpaper";

    bool CanSet() => HasPicture && !IsSetting && !IsCreating;

    [RelayCommand(CanExecute = nameof(CanSet))]
    async Task Set()
    {
        if (_created is not { } made) return;
        IsSetting = true;
        IsInfoOpen = false;
        try
        {
            var result = await PhaseWatch.RunAsync(_wallpapers, () => Task.Run(() => _wallpapers.ApplyCreatedAsync(made.Photo, made.Image)),
                stage => App.Dispatcher.TryEnqueue(() => Stage = stage));
            if (result.Status == ApplyStatus.Applied) IsApplied = true;
            else ShowInfo(InfoBarSeverity.Warning, "Couldn't set the wallpaper", result.Error ?? "Try again.");
        }
        finally
        {
            IsSetting = false;
        }
    }

    // ---- Messages ----

    [ObservableProperty] public partial bool IsInfoOpen { get; set; }
    [ObservableProperty] public partial string InfoTitle { get; set; } = "";
    [ObservableProperty] public partial string InfoMessage { get; set; } = "";
    [ObservableProperty] public partial InfoBarSeverity InfoSeverity { get; set; }

    void ShowInfo(InfoBarSeverity severity, string title, string message)
    {
        InfoSeverity = severity;
        InfoTitle = title;
        InfoMessage = message;
        IsInfoOpen = true;
    }
}
