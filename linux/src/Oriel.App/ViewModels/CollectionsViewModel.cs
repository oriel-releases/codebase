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
        ["nature"] = "🌿", ["mountains"] = "🏔️", ["ocean"] = "🌊", ["space"] = "🪐",
        ["cities"] = "🏙️", ["architecture"] = "🏛️", ["minimal"] = "◽", ["dark"] = "🌑",
        ["abstract"] = "🎨", ["forest"] = "🌲", ["desert"] = "🏜️", ["aerial"] = "🚁",
        ["night sky"] = "🌃", ["snow"] = "❄️",
    };

    public static string EmojiFor(string query) => Emojis.GetValueOrDefault(query, "✨");

    [ObservableProperty] public partial bool IsEnabled { get; set; } = enabled;

    partial void OnIsEnabledChanged(bool value) => owner.Save();

    [RelayCommand]
    void Remove() => owner.RemoveTopic(this);
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

    public string ButtonText => IsBusy ? "Setting…" : IsApplied ? "Wallpaper set ✓" : "Set as wallpaper";

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
    static readonly string[] SuggestionList =
        ["northern lights", "tokyo night", "foggy forest", "desert dunes", "city skyline", "snowy mountains", "calm ocean", "milky way"];
    /// <summary>Least time between two requests to the photo service, so mashing Enter or Show more can't burn the hourly limit.</summary>
    static readonly TimeSpan MinRequestGap = TimeSpan.FromSeconds(1.5);

    readonly ISettingsStore _settings;
    readonly WallpaperService _wallpapers;
    bool _loading;
    string _query = "";
    int _page;
    int _version;
    DateTime _lastRequestAt = DateTime.MinValue;

    public CollectionsViewModel(ISettingsStore settings, WallpaperService wallpapers)
    {
        _settings = settings;
        _wallpapers = wallpapers;
        Load();
    }

    public ObservableCollection<TopicViewModel> BuiltInTopics { get; } = [];
    public ObservableCollection<TopicViewModel> CustomTopics { get; } = [];
    public ObservableCollection<PhotoResultViewModel> Results { get; } = [];
    /// <summary>Ideas shown before the first search, so the empty page isn't a blank wall.</summary>
    public IReadOnlyList<string> Suggestions => SuggestionList;

    /// <summary>Result of searching or setting a wallpaper.</summary>
    public InfoState Info { get; } = new();
    /// <summary>"Nothing is on, so Oriel will use generic landscape photos."</summary>
    public InfoState NothingOnInfo { get; } = new()
    {
        Severity = InfoSeverity.Warning,
        Message = "Nothing is on, so Oriel will use generic landscape photos.",
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
            (t.IsCustom ? CustomTopics : BuiltInTopics).Add(new TopicViewModel(this, t.Query, t.Enabled, t.IsCustom));
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
                .Select(t => new TopicSetting { Query = t.Query, Enabled = t.IsEnabled, IsCustom = t.IsCustom })
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
        MixSummary = $"{all.Count(t => t.IsEnabled)} of {all.Count} interests on. Each day Oriel picks one at random.";
        CanAddToMix = HasSearched && !all.Any(t => t.Query.Equals(_query, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand] void OpenMix() => IsMixOpen = true;
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
            var result = await Task.Run(() => _wallpapers.ApplyPhotoAsync(item.Photo, _query));
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
    void AddToMix()
    {
        var q = _query.Trim().ToLowerInvariant();
        if (q.Length == 0) return;
        var existing = BuiltInTopics.Concat(CustomTopics).FirstOrDefault(t => t.Query.Equals(q, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsEnabled = true;
            return;
        }
        CustomTopics.Add(new TopicViewModel(this, q, true, isCustom: true));
        Save();
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
