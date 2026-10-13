using Avalonia.Threading;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core.Models;
using Oriel.Core.Providers;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Oriel.UI.Services;

namespace Oriel.UI.ViewModels;

public sealed partial class TopicViewModel(CollectionsViewModel owner, string query, bool enabled, bool isCustom) : ObservableObject
{
    public string Query { get; } = query;
    public bool IsCustom { get; } = isCustom;
    public string Label => char.ToUpper(Query[0]) + Query[1..];
    public string Emoji => IsCustom ? "🔎" : Emojis.GetValueOrDefault(Query, "✨");
    /// <summary>What the chip shows: the emoji, then the name.</summary>
    public string ChipText => $"{Emoji}  {Label}";

    static readonly Dictionary<string, string> Emojis = new()
    {
        ["nature"] = "🌿", ["mountains"] = "🏔️", ["ocean"] = "🌊", ["beach"] = "🏖️", ["lakes"] = "🛶",
        ["waterfalls"] = "💧", ["forest"] = "🌲", ["autumn"] = "🍂", ["flowers"] = "🌸", ["desert"] = "🏜️",
        ["snow"] = "❄️", ["northern lights"] = "🌌", ["night sky"] = "🌃", ["space"] = "🪐", ["sunset"] = "🌅",
        ["cities"] = "🏙️", ["city at night"] = "🌉", ["architecture"] = "🏛️", ["japan"] = "⛩️", ["islands"] = "🏝️",
        ["wildlife"] = "🦊", ["countryside"] = "🌾", ["aerial"] = "🚁", ["fog"] = "🌫️",
    };

    public static string EmojiFor(string query) => Emojis.GetValueOrDefault(query, "✨");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOff))]
    public partial bool IsEnabled { get; set; } = enabled;
    public bool IsOff => !IsEnabled;

    partial void OnIsEnabledChanged(bool value) => owner.Save();

    /// <summary>The tile was clicked: on becomes off and back.</summary>
    [RelayCommand]
    void Toggle() => IsEnabled = !IsEnabled;

    [RelayCommand]
    void Remove() => owner.RemoveTopic(this);

    /// <summary>
    /// The tile's photo: a fixed one for built-in interests, the last wallpaper found for one of your own (if any).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover), nameof(HasNoCover), nameof(CoverNote))]
    public partial string? CoverUrl { get; set; } = InterestCovers.Url(query);
    public string CoverCredit => InterestCovers.Credit(Query);

    /// <summary>Looking up a photo for one of your own interests (see <see cref="CollectionsViewModel.FindCoversAsync"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoverNote))]
    public partial bool IsFindingCover { get; set; }
    /// <summary>Under the logo while there's no photo yet.</summary>
    public string CoverNote => IsFindingCover ? "Finding a photo…" : "Photo comes soon";

    /// <summary>Loaded when the Interests page opens (see <see cref="CollectionsViewModel.LoadCoversAsync"/>).</summary>
    [ObservableProperty] public partial Bitmap? Cover { get; set; }
    public bool HasCover => CoverUrl is not null;
    public bool HasNoCover => CoverUrl is null;

    partial void OnCoverUrlChanged(string? value)
    {
        Cover = null;
        _ = LoadCoverAsync();
    }

    public async Task LoadCoverAsync()
    {
        if (Cover is null && CoverUrl is { } url) Cover = await ImageLoader.LoadUrlAsync(url, 320);
    }
}

/// <summary>One search result: a photo the user can set as the wallpaper.</summary>
public sealed partial class PhotoResultViewModel(CollectionsViewModel owner, PhotoCandidate photo) : ObservableObject
{
    const int ThumbnailWidth = 360;

    int _loadVersion;

    public PhotoCandidate Photo { get; } = photo;
    public string PhotographerName => Photo.PhotographerName;
    public Uri PhotographerUri { get; } = new(photo.PhotographerUrl);
    public string Description => Photo.Description ?? "";
    public string Size => $"{Photo.Width} × {Photo.Height}";

    [ObservableProperty] public partial Bitmap? Thumbnail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    public partial bool IsBusy { get; set; }
    public bool IsIdle => !IsBusy;

    /// <summary>This photo was just set as the wallpaper.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    public partial bool IsApplied { get; set; }

    /// <summary>What setting it is doing now: "Downloading…", "Changing…", "Syncing…".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    public partial string? Stage { get; set; }

    public string ButtonText => IsBusy ? Stage ?? "Downloading…" : IsApplied ? "Wallpaper set ✓" : "Set as wallpaper";

    /// <summary>Downloaded only while the card is on screen.</summary>
    public async Task LoadThumbnailAsync()
    {
        if (Thumbnail is not null) return;
        var version = ++_loadVersion;
        var bitmap = await ImageLoader.LoadUrlAsync(Photo.ThumbnailUrl, ThumbnailWidth);
        if (version != _loadVersion) { bitmap?.Dispose(); return; }
        Thumbnail = bitmap;
    }

    public void UnloadThumbnail()
    {
        _loadVersion++;
        var old = Thumbnail;
        Thumbnail = null;
        old?.Dispose();
    }

    [RelayCommand]
    Task Set() => owner.SetAsync(this);

    [RelayCommand]
    void OpenUrl(Uri? uri) { if (uri is not null) Launcher.OpenUrl(uri); }
}

public sealed partial class CollectionsViewModel : ObservableObject
{
    const int MinResultsPerSearch = 6;
    const int MaxPagesPerSearch = 3;
    /// <summary>Least time between two requests to the photo service, so mashing Enter or Show more can't burn the hourly limit.</summary>
    static readonly TimeSpan MinRequestGap = TimeSpan.FromSeconds(1.5);

    readonly ISettingsStore _settings;
    readonly WallpaperService _wallpapers;
    bool _loading;
    string _query = "";
    int _page;
    int _version;
    DateTime _lastRequestAt = DateTime.MinValue;

    readonly Oriel.Core.Storage.IHistoryStore _history;

    public CollectionsViewModel(ISettingsStore settings, WallpaperService wallpapers, Oriel.Core.Storage.IHistoryStore history)
    {
        _history = history;
        _settings = settings;
        _wallpapers = wallpapers;
        Load();
    }

    public ObservableCollection<TopicViewModel> BuiltInTopics { get; } = [];
    public ObservableCollection<TopicViewModel> CustomTopics { get; } = [];
    public ObservableCollection<PhotoResultViewModel> Results { get; } = [];
    /// <summary>Ideas shown before the first search, so the empty page isn't a blank wall. A new few each visit.</summary>
    public ObservableCollection<string> Suggestions { get; } = new(SearchIdeas.Pick());

    /// <summary>Other ideas: on every visit to Search, and the shuffle button.</summary>
    [RelayCommand]
    public void ShuffleSuggestions()
    {
        Suggestions.Clear();
        foreach (var idea in SearchIdeas.Pick()) Suggestions.Add(idea);
    }

    /// <summary>Result of searching or setting a wallpaper.</summary>
    public InfoState Info { get; } = new();
    /// <summary>"Nothing is on, so Oriel uses general landscape photos."</summary>
    public InfoState NothingOnInfo { get; } = new()
    {
        Severity = InfoSeverity.Warning,
        Message = "Nothing is on, so Oriel uses general landscape photos.",
        IsClosable = false,
    };

    /// <summary>The Interests sub page of Search is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchShown))]
    public partial bool IsMixOpen { get; set; }
    public bool IsSearchShown => !IsMixOpen;

    // Search
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyPropertyChangedFor(nameof(HasText))]
    public partial string SearchText { get; set; } = "";
    public bool HasText => !string.IsNullOrEmpty(SearchText);
    [ObservableProperty] public partial string ResultsTitle { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStart))]
    public partial bool HasSearched { get; set; }
    /// <summary>Before the first search: suggestions and the Interests row. After it, the photos come first.</summary>
    public bool ShowStart => !HasSearched;
    [ObservableProperty] public partial bool HasNoResults { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial bool CanLoadMore { get; set; }
    [ObservableProperty] public partial bool CanAddToMix { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial bool IsSearching { get; set; }

    [ObservableProperty] public partial bool NothingEnabled { get; set; }
    /// <summary>"5 of 14 interests on. Each day Oriel picks one at random."</summary>
    [ObservableProperty] public partial string MixSummary { get; set; } = "";
    [ObservableProperty] public partial bool HasCustomTopics { get; set; }

    void Load()
    {
        _loading = true;
        var s = _settings.Load();
        BuiltInTopics.Clear();
        CustomTopics.Clear();
        foreach (var t in s.Topics.Where(t => !string.IsNullOrWhiteSpace(t.Query)))
            (t.IsCustom ? CustomTopics : BuiltInTopics).Add(NewTopic(t.Query, t.Enabled, t.IsCustom, t.CoverUrl));
        _loading = false;
        UpdateSummary();
    }

    /// <summary>Window closed: drop the result thumbnails.</summary>
    public void Release()
    {
        foreach (var r in Results) r.UnloadThumbnail();
        Results.Clear();
        HasSearched = false;
        CanLoadMore = false;
        IsMixOpen = false;
    }

    internal void Save()
    {
        if (_loading) return;
        _settings.Update(s =>
        {
            s.Topics = BuiltInTopics.Concat(CustomTopics)
                .Select(t => new TopicSetting { Query = t.Query, Enabled = t.IsEnabled, IsCustom = t.IsCustom, CoverUrl = t.IsCustom ? t.CoverUrl : null })
                .ToList();
        });
        UpdateSummary();
    }

    void UpdateSummary()
    {
        var all = BuiltInTopics.Concat(CustomTopics).ToList();
        NothingEnabled = !all.Any(t => t.IsEnabled);
        NothingOnInfo.IsOpen = NothingEnabled;
        HasCustomTopics = CustomTopics.Count > 0;
        MixSummary = $"{all.Count(t => t.IsEnabled)} of {all.Count} on. Each new wallpaper comes from one of them.";
        CanAddToMix = HasSearched && !all.Any(t => t.Query.Equals(_query, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    void OpenMix()
    {
        IsMixOpen = true;
        _ = LoadCoversAsync();
        _ = FindCoversAsync();
    }

    /// <summary>The tiles' photos, loaded when the Interests page opens.</summary>
    public async Task LoadCoversAsync()
    {
        foreach (var t in BuiltInTopics.Concat(CustomTopics).ToList()) await t.LoadCoverAsync();
    }
    [RelayCommand] void CloseMix() => IsMixOpen = false;

    // ---- Search and set ----

    bool CanSearch() => !IsSearching && !string.IsNullOrWhiteSpace(SearchText);

    [RelayCommand(CanExecute = nameof(CanSearch))]
    async Task Search()
    {
        var query = SearchText.Trim();
        // Same words, results already on screen: nothing new to ask for.
        if (HasSearched && Results.Count > 0 && query.Equals(_query, StringComparison.OrdinalIgnoreCase)) return;
        _query = query;
        _page = 0;
        Info.IsOpen = false;
        foreach (var r in Results) r.UnloadThumbnail();
        Results.Clear();
        CanLoadMore = false;
        HasSearched = true;
        ResultsTitle = $"Results for “{_query}”";
        await LoadPagesAsync();
    }

    [RelayCommand]
    async Task SearchFor(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || IsSearching) return;
        SearchText = query;
        await Search();
    }

    /// <summary>The ✕ in the search bar: empties the field and goes back to the start page.</summary>
    [RelayCommand]
    void ClearSearch()
    {
        _version++;
        _query = "";
        SearchText = "";
        foreach (var r in Results) r.UnloadThumbnail();
        Results.Clear();
        HasSearched = false;
        HasNoResults = false;
        CanLoadMore = false;
        Info.IsOpen = false;
        UpdateSummary();
    }

    bool CanLoadMoreResults() => !IsSearching && CanLoadMore;

    [RelayCommand(CanExecute = nameof(CanLoadMoreResults))]
    Task LoadMore() => LoadPagesAsync();

    /// <summary>Loads the next page, and one or two more if too few photos were big enough for the screen.</summary>
    async Task LoadPagesAsync()
    {
        var version = _version;
        IsSearching = true;
        try
        {
            var added = 0;
            for (var i = 0; i < MaxPagesPerSearch && added < MinResultsPerSearch; i++)
            {
                var wait = MinRequestGap - (DateTime.UtcNow - _lastRequestAt);
                if (wait > TimeSpan.Zero) await Task.Delay(wait);
                _lastRequestAt = DateTime.UtcNow;
                // Finding the screen size runs xrandr or similar: keep that off the UI thread.
                var found = await Task.Run(() => _wallpapers.SearchAsync(_query, ++_page));
                if (version != _version) return;   // cleared while this was loading
                foreach (var photo in found.Photos) Results.Add(new PhotoResultViewModel(this, photo));
                added += found.Photos.Count;
                CanLoadMore = _page < found.TotalPages;
                if (!CanLoadMore) break;
            }
        }
        catch (ProviderException ex)
        {
            Info.Show(ex.Kind is ProviderErrorKind.MissingKey or ProviderErrorKind.Unauthorized
                ? InfoSeverity.Error : InfoSeverity.Warning, "Couldn't search", ex.Message);
        }
        finally
        {
            IsSearching = false;
            HasNoResults = HasSearched && Results.Count == 0 && !Info.IsOpen;
            UpdateSummary();
        }
    }

    internal async Task SetAsync(PhotoResultViewModel item)
    {
        if (Results.Any(r => r.IsBusy)) return;
        item.IsBusy = true;
        Info.IsOpen = false;
        try
        {
            var result = await PhaseWatch.RunAsync(_wallpapers, () => Task.Run(() => _wallpapers.ApplyPhotoAsync(item.Photo, _query)),
                stage => Dispatcher.UIThread.Post(() => item.Stage = stage));
            if (result.Status != ApplyStatus.Applied)
                Info.Show(InfoSeverity.Warning, "Couldn't set the wallpaper", result.Error ?? "Try again.");
            else
            {
                foreach (var r in Results) r.IsApplied = false;
                item.IsApplied = true;
            }
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    // ---- Topic mix ----

    [RelayCommand]
    void AddToMix() => AddTopic(_query);

    /// <summary>Turns an interest on, adding it as one of your own when it's new.</summary>
    void AddTopic(string query)
    {
        var q = query.Trim().ToLowerInvariant();
        if (q.Length == 0) return;
        var existing = BuiltInTopics.Concat(CustomTopics).FirstOrDefault(t => t.Query.Equals(q, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsEnabled = true;
            return;
        }
        CustomTopics.Add(NewTopic(q, true, isCustom: true));
        Save();
        _ = FindCoversAsync();
    }

    /// <summary>
    /// One of your own interests shows its kept photo, else the last wallpaper Oriel found for it; without either, one is
    /// looked up (<see cref="FindCoversAsync"/>).
    /// </summary>
    TopicViewModel NewTopic(string query, bool enabled, bool isCustom, string? keptCover = null) => isCustom
        ? new TopicViewModel(this, query, enabled, true)
        {
            CoverUrl = keptCover
                ?? _history.List().FirstOrDefault(e => string.Equals(e.Topic, query, StringComparison.OrdinalIgnoreCase))?.ThumbnailUrl,
        }
        : new TopicViewModel(this, query, enabled, false);

    /// <summary>Interests already looked up this session (found or not), so a failure isn't retried on every visit.</summary>
    readonly HashSet<string> _coverLookups = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Photos for your own interests that have none: one Unsplash search each, which counts toward the hourly limit,
    /// so it's done once per interest (the photo is kept with it) and skipped while requests are scarce. One at a time.
    /// </summary>
    public async Task FindCoversAsync()
    {
        foreach (var topic in CustomTopics.Where(t => t.CoverUrl is null && _coverLookups.Add(t.Query)).ToList())
        {
            topic.IsFindingCover = true;
            try
            {
                if (await _wallpapers.FindCoverAsync(topic.Query) is { } url)
                {
                    topic.CoverUrl = url;
                    Save();
                }
                else _coverLookups.Remove(topic.Query); // no requests to spare: try again next time
            }
            catch (ProviderException)
            {
                _coverLookups.Remove(topic.Query); // offline or limited: try again next time
            }
            finally
            {
                topic.IsFindingCover = false;
            }
        }
    }

    internal void RemoveTopic(TopicViewModel topic)
    {
        CustomTopics.Remove(topic);
        Save();
    }

    [RelayCommand]
    void SelectAll(bool enable)
    {
        _loading = true;
        foreach (var t in BuiltInTopics) t.IsEnabled = enable;
        _loading = false;
        Save();
    }
}
