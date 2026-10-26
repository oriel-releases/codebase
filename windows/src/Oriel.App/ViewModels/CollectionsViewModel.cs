using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core.Models;
using Oriel.Core.Providers;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
    ImageSource? _cover;
    /// <summary>Made when the tile first shows, sized for it.</summary>
    public ImageSource? Cover => _cover ??= CoverUrl is { } url
        ? new BitmapImage(new Uri(url)) { DecodePixelWidth = 320, DecodePixelType = DecodePixelType.Logical }
        : null;
    public bool HasCover => CoverUrl is not null;
    public bool HasNoCover => CoverUrl is null;

    partial void OnCoverUrlChanged(string? value)
    {
        _cover = null;
        OnPropertyChanged(nameof(Cover));
    }
}

/// <summary>One search result or Explore photo: a photo the user can set as the wallpaper.</summary>
public sealed partial class PhotoResultViewModel(CollectionsViewModel owner, PhotoCandidate photo, string? topic = null)
    : ObservableObject, IPhotoDetail
{
    public PhotoCandidate Photo { get; } = photo;
    /// <summary>The interest an Explore photo was found under (kept in History); a search's words for a result.</summary>
    public string? Topic { get; } = topic;
    public string PhotographerName => Photo.PhotographerName;
    public Uri PhotographerUri { get; } = new(photo.PhotographerUrl);
    public string Description => Photo.Description ?? "";
    public string Size => $"{Photo.Width} × {Photo.Height}";

    // Its own page (IPhotoDetail)
    public ImageSource? Preview => Thumbnail;
    public double AspectRatio => Photo.Height > 0 ? (double)Photo.Width / Photo.Height : 16 / 9.0;
    public string Headline => PhotoText.Headline(Photo.Description, Topic is "explore" or "search" ? null : Topic);
    public string Facts => Photo.Likes > 0 ? $"{Size} · {Photo.Likes:N0} likes" : Size;
    public Uri PhotoPageUri { get; } = new(photo.PhotoPageUrl);
    public bool CanFavorite => false;
    bool IPhotoDetail.IsFavorite { get => false; set { } }
    public bool CanDelete => false;
    public bool CanOpenFolder => false;
    public void OpenFolder() { }
    public bool IsFavorite => false;
    public string Subtitle => Topic is null or "explore" or "search" ? Size : PhotoText.Topic(Topic)!;
    public Task<bool> RemoveAsync(Func<string, Task<bool>> confirm) => Task.FromResult(false);
    public HistoryEntry ShareEntry => new()
    {
        Provider = Photo.Provider, PhotoId = Photo.Id, FilePath = "", ThumbnailUrl = Photo.RawUrl, PhotoPageUrl = Photo.PhotoPageUrl,
        PhotographerName = Photo.PhotographerName, PhotographerUrl = Photo.PhotographerUrl, AppliedAt = DateTimeOffset.Now,
    };

    public IReadOnlyList<PhotoFact> Chips => [.. new[]
    {
        new PhotoFact(PhotoFactKind.Size, Size),
        Photo.Likes > 0 ? new PhotoFact(PhotoFactKind.Likes, PhotoFact.Count(Photo.Likes)) : null,
        Topic is null or "explore" or "search" || PhotoText.Topic(Topic) is not { } topic ? null : new PhotoFact(PhotoFactKind.Topic, topic),
    }.OfType<PhotoFact>()];

    /// <summary>Unsplash makes it at exactly the size asked for (up to the photo's own), so it's sharp on any screen.</summary>
    public Task<ImageSource?> LoadLargeAsync(int pixelWidth)
    {
        var width = Math.Min(pixelWidth, Photo.Width > 0 ? Photo.Width : pixelWidth);
        var url = Photo.RawUrl + (Photo.RawUrl.Contains('?') ? '&' : '?') + $"w={width}&q=90&fm=jpg&fit=max";
        return ImageLoader.LoadUrlAsync(url, width, DecodePixelType.Physical);
    }

    public ImageSource Thumbnail { get; } = new BitmapImage(new Uri(photo.ThumbnailUrl))
    {
        DecodePixelWidth = 360,
        DecodePixelType = DecodePixelType.Logical,
    };

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

    [RelayCommand]
    Task Set() => owner.SetAsync(this);
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
    readonly ExploreService _explore;

    public CollectionsViewModel(ISettingsStore settings, WallpaperService wallpapers, Oriel.Core.Storage.IHistoryStore history,
        ExploreService explore)
    {
        _history = history;
        _explore = explore;
        _settings = settings;
        _wallpapers = wallpapers;
        Load();
    }

    public ObservableCollection<TopicViewModel> BuiltInTopics { get; } = [];
    public ObservableCollection<TopicViewModel> CustomTopics { get; } = [];
    public ObservableCollection<PhotoResultViewModel> Results { get; } = [];
    /// <summary>This hour's Explore photos shown so far; more are added as the page is scrolled (<see cref="ShowMoreExplore"/>).</summary>
    public ObservableCollection<PhotoResultViewModel> Explore { get; } = [];
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

    // Result of searching or setting a wallpaper
    [ObservableProperty] public partial bool IsInfoOpen { get; set; }
    [ObservableProperty] public partial string InfoTitle { get; set; } = "";
    [ObservableProperty] public partial string InfoMessage { get; set; } = "";
    [ObservableProperty] public partial InfoBarSeverity InfoSeverity { get; set; }

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
        Results.Clear();
        Explore.Clear();
        _exploreHour = DateTimeOffset.MinValue;
        HasSearched = false;
        CanLoadMore = false;
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
        NothingEnabled = !BuiltInTopics.Concat(CustomTopics).Any(t => t.IsEnabled);
        HasCustomTopics = CustomTopics.Count > 0;
        var all = BuiltInTopics.Concat(CustomTopics).ToList();
        MixSummary = $"{all.Count(t => t.IsEnabled)} of {all.Count} on. Each new wallpaper comes from one of them.";
        CanAddToMix = HasSearched && !BuiltInTopics.Concat(CustomTopics)
            .Any(t => t.Query.Equals(_query, StringComparison.OrdinalIgnoreCase));
    }

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
        IsInfoOpen = false;
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
        Results.Clear();
        HasSearched = false;
        HasNoResults = false;
        CanLoadMore = false;
        IsInfoOpen = false;
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
                var found = await _wallpapers.SearchAsync(_query, ++_page);
                if (version != _version) return;   // cleared while this was loading
                foreach (var photo in found.Photos) Results.Add(new PhotoResultViewModel(this, photo));
                added += found.Photos.Count;
                CanLoadMore = _page < found.TotalPages;
                if (!CanLoadMore) break;
            }
        }
        catch (ProviderException ex)
        {
            ShowInfo(ex.Kind is ProviderErrorKind.MissingKey or ProviderErrorKind.Unauthorized
                ? InfoBarSeverity.Error : InfoBarSeverity.Warning, "Couldn't search", ex.Message);
        }
        finally
        {
            IsSearching = false;
            HasNoResults = HasSearched && Results.Count == 0 && !IsInfoOpen;
            UpdateSummary();
        }
    }

    // ---- Explore: the same photos for everyone, picked once an hour (no Unsplash requests to scroll them) ----

    /// <summary>Photos added to <see cref="Explore"/> at a time, so the thumbnails load as the page is scrolled.</summary>
    const int ExploreBatch = 24;
    IReadOnlyList<ExplorePhoto> _explorePhotos = [];
    DateTimeOffset _exploreHour = DateTimeOffset.MinValue;

    [ObservableProperty] public partial bool IsExploreLoading { get; set; }
    /// <summary>No photos to show: offline the very first time.</summary>
    [ObservableProperty] public partial bool ExploreUnavailable { get; set; }

    /// <summary>On every visit: this hour's photos, kept as they are when the hour hasn't changed.</summary>
    public async Task LoadExploreAsync()
    {
        if (IsExploreLoading) return;
        IsExploreLoading = Explore.Count == 0;
        try
        {
            var feed = await _explore.GetAsync();
            ExploreUnavailable = feed.Photos.Count == 0;
            if (feed.Photos.Count == 0 || (feed.Hour == _exploreHour && Explore.Count > 0)) return;
            _exploreHour = feed.Hour;
            _explorePhotos = feed.Photos;
            Explore.Clear();
            ShowMoreExplore();
        }
        finally
        {
            IsExploreLoading = false;
        }
    }

    [ObservableProperty] public partial bool IsExploreRefreshing { get; set; }

    /// <summary>
    /// Refresh: a new hour's photos when they're out; else the same photos in a new order, so it still looks fresh.
    /// </summary>
    [RelayCommand]
    async Task RefreshExploreAsync()
    {
        if (IsExploreRefreshing || IsExploreLoading) return;
        IsExploreRefreshing = true;
        // It takes a moment even when nothing new is out: a refresh that answers at once looks like it did nothing.
        var moment = Task.Delay(1400);
        try
        {
            var feed = await _explore.GetAsync(force: true);
            await moment;
            if (feed.Photos.Count == 0) return;
            ExploreUnavailable = false;
            var photos = feed.Photos.ToArray();
            if (feed.Hour == _exploreHour) Random.Shared.Shuffle(photos);
            _exploreHour = feed.Hour;
            _explorePhotos = photos;
            Explore.Clear();
            ShowMoreExplore();
        }
        finally
        {
            IsExploreRefreshing = false;
        }
    }

    /// <summary>Scrolled near the end: the next few photos.</summary>
    public void ShowMoreExplore()
    {
        foreach (var item in _explorePhotos.Skip(Explore.Count).Take(ExploreBatch))
            Explore.Add(new PhotoResultViewModel(this, item.Photo, item.Topic ?? "explore"));
    }

    internal async Task SetAsync(PhotoResultViewModel item)
    {
        if (Results.Concat(Explore).Any(r => r.IsBusy)) return;
        item.IsBusy = true;
        IsInfoOpen = false;
        try
        {
            var topic = item.Topic ?? _query;
            var result = await PhaseWatch.RunAsync(_wallpapers, () => Task.Run(() => _wallpapers.ApplyPhotoAsync(item.Photo, topic)),
                stage => App.Dispatcher.TryEnqueue(() => item.Stage = stage));
            if (result.Status != ApplyStatus.Applied)
                ShowInfo(InfoBarSeverity.Warning, "Couldn't set the wallpaper", result.Error ?? "Try again.");
            else
            {
                foreach (var r in Results.Concat(Explore)) r.IsApplied = false;
                item.IsApplied = true;
            }
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    void ShowInfo(InfoBarSeverity severity, string title, string message)
    {
        InfoSeverity = severity;
        InfoTitle = title;
        InfoMessage = message;
        IsInfoOpen = true;
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
