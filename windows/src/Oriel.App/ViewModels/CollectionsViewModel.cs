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

    [ObservableProperty] public partial bool IsEnabled { get; set; } = enabled;

    partial void OnIsEnabledChanged(bool value) => owner.Save();

    [RelayCommand]
    void Remove() => owner.RemoveTopic(this);
}

/// <summary>One search result: a photo the user can set as the wallpaper.</summary>
public sealed partial class PhotoResultViewModel(CollectionsViewModel owner, PhotoCandidate photo) : ObservableObject
{
    public PhotoCandidate Photo { get; } = photo;
    public string PhotographerName => Photo.PhotographerName;
    public Uri PhotographerUri { get; } = new(photo.PhotographerUrl);
    public string Description => Photo.Description ?? "";
    public string Size => $"{Photo.Width} × {Photo.Height}";

    public ImageSource Thumbnail { get; } = new BitmapImage(new Uri(photo.ThumbnailUrl))
    {
        DecodePixelWidth = 360,
        DecodePixelType = DecodePixelType.Logical,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }
    public bool IsIdle => !IsBusy;

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

    // Search
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial string ResultsTitle { get; set; } = "";
    [ObservableProperty] public partial bool HasSearched { get; set; }
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
    /// <summary>"5 of 14 topics on. Each day Oriel picks one at random."</summary>
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
        Results.Clear();
        HasSearched = false;
        CanLoadMore = false;
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
        NothingEnabled = !BuiltInTopics.Concat(CustomTopics).Any(t => t.IsEnabled);
        HasCustomTopics = CustomTopics.Count > 0;
        var all = BuiltInTopics.Concat(CustomTopics).ToList();
        MixSummary = $"{all.Count(t => t.IsEnabled)} of {all.Count} topics on. Each day Oriel picks one at random.";
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

    bool CanLoadMoreResults() => !IsSearching && CanLoadMore;

    [RelayCommand(CanExecute = nameof(CanLoadMoreResults))]
    Task LoadMore() => LoadPagesAsync();

    /// <summary>Loads the next page, and one or two more if too few photos were big enough for the screen.</summary>
    async Task LoadPagesAsync()
    {
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

    internal async Task SetAsync(PhotoResultViewModel item)
    {
        if (Results.Any(r => r.IsBusy)) return;
        item.IsBusy = true;
        IsInfoOpen = false;
        try
        {
            var result = await Task.Run(() => _wallpapers.ApplyPhotoAsync(item.Photo, _query));
            if (result.Status != ApplyStatus.Applied)
                ShowInfo(InfoBarSeverity.Warning, "Couldn't set the wallpaper", result.Error ?? "Try again.");
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
