using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core.Models;
using Oriel.Core.Services;
using Oriel.Core.Storage;
using Oriel.UI.Services;

namespace Oriel.UI.ViewModels;

public sealed partial class HistoryItemViewModel(HistoryViewModel owner, HistoryEntry entry) : ObservableObject
{
    const int ThumbnailWidth = 320;

    int _loadVersion;

    public HistoryEntry Entry { get; private set; } = entry;
    public string PhotographerName => Entry.PhotographerName;
    public Uri PhotographerUri { get; } = new(entry.PhotographerUrl);
    public string DateText => Entry.AppliedAt.LocalDateTime.ToString("ddd, d MMM yyyy");
    public string Topic => Entry.Topic ?? "";
    public string Details => Topic.Length == 0 ? DateText : $"{DateText} · {Topic}";
    public bool FileExists => File.Exists(Entry.FilePath);
    /// <summary>A favorite from another device has no file here yet: setting it downloads it.</summary>
    public bool CanReapply => FileExists || Entry.Provider is "unsplash" or Oriel.Core.Providers.PollinationsClient.Provider;

    [ObservableProperty] public partial Bitmap? Thumbnail { get; set; }
    [ObservableProperty] public partial bool IsFavorite { get; set; } = entry.IsFavorite;

    bool _quiet;

    partial void OnIsFavoriteChanged(bool value)
    {
        if (!_quiet) owner.SetFavorite(this, value);
    }

    /// <summary>The same photo, read again (it may have been starred on another device): kept, with its thumbnail.</summary>
    internal void Sync(HistoryEntry entry)
    {
        Entry = entry;
        _quiet = true;
        IsFavorite = entry.IsFavorite;
        _quiet = false;
    }

    /// <summary>
    /// Thumbnails exist while their card is on screen, plus the last few seen (so going back to the page, or
    /// History and Favorites, shows them at once): a long history stays cheap.
    /// </summary>
    public async Task LoadThumbnailAsync()
    {
        owner.Showing(this);
        if (Thumbnail is not null) return;
        var version = ++_loadVersion;
        var bitmap = await ImageLoader.LoadAsync(Entry.FilePath, Entry.ThumbnailUrl, ThumbnailWidth);
        if (version != _loadVersion) { bitmap?.Dispose(); return; } // scrolled away while loading
        Thumbnail = bitmap;
    }

    /// <summary>Off screen: kept for a while, dropped once enough others went off screen after it.</summary>
    public void Hide() => owner.Hidden(this);

    public void UnloadThumbnail()
    {
        _loadVersion++;
        var old = Thumbnail;
        Thumbnail = null;
        old?.Dispose();
    }

    [RelayCommand] Task Reapply() => owner.ReapplyAsync(this);
    [RelayCommand] Task Delete() => owner.DeleteAsync(this);
    [RelayCommand] void OpenInFolder() => Launcher.ShowInFileManager(Entry.FilePath);
    [RelayCommand] void OpenUrl(Uri? uri) { if (uri is not null) Launcher.OpenUrl(uri); }
}

public sealed partial class HistoryViewModel(IHistoryStore history, WallpaperService wallpapers, RetentionService retention)
    : ObservableObject
{
    List<HistoryItemViewModel> _all = [];
    /// <summary>Items by history id, kept between visits: coming back shows the same cards and thumbnails at once.</summary>
    readonly Dictionary<long, HistoryItemViewModel> _known = [];
    /// <summary>Off-screen items that still hold a thumbnail, oldest first.</summary>
    readonly LinkedList<HistoryItemViewModel> _hidden = [];
    const int KeepHidden = 60;

    /// <summary>
    /// The photos shown. Swapped as a whole (never cleared and refilled item by item): changing a list while the grid
    /// is still laying out the last change made WinUI fail with "Unspecified error" when History/Favorites switched fast.
    /// </summary>
    [ObservableProperty] public partial ObservableCollection<HistoryItemViewModel> Items { get; set; } = [];
    int _loadVersion;
    public InfoState Info { get; } = new() { Severity = InfoSeverity.Informational };

    /// <summary>Set by the page: shows a confirmation dialog and returns true if the user confirmed.</summary>
    public Func<string, Task<bool>>? ConfirmDelete { get; set; }

    [ObservableProperty] public partial bool IsGrid { get; set; } = true;
    [ObservableProperty] public partial bool IsList { get; set; }
    /// <summary>The same page serves History (everything) and Favorites (starred photos only).</summary>
    [ObservableProperty] public partial bool FavoritesOnly { get; set; }
    public string Title => FavoritesOnly ? "Favorites" : "History";
    public string EmptyText => FavoritesOnly ? "No favorites yet. Star a photo to keep it here." : "Nothing here yet.";
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    /// <summary>What "Set again" is doing now (Downloading… / Changing… / Syncing…), or null.</summary>
    [ObservableProperty] public partial string? Stage { get; set; }
    public bool IsChanging => Stage is not null;
    partial void OnStageChanged(string? value) => OnPropertyChanged(nameof(IsChanging));

    internal void Showing(HistoryItemViewModel item) => _hidden.Remove(item);

    internal void Hidden(HistoryItemViewModel item)
    {
        if (item.Thumbnail is null || _hidden.Contains(item)) return;
        _hidden.AddLast(item);
        while (_hidden.Count > KeepHidden)
        {
            var oldest = _hidden.First!.Value;
            _hidden.RemoveFirst();
            oldest.UnloadThumbnail();
        }
    }

    partial void OnIsGridChanged(bool value) => IsList = !value;
    partial void OnIsListChanged(bool value) => IsGrid = !value;

    public async Task LoadAsync(bool favoritesOnly = false)
    {
        FavoritesOnly = favoritesOnly;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(EmptyText));
        var version = ++_loadVersion;
        var entries = await Task.Run(() => history.List());
        if (version != _loadVersion) return; // a newer load (History <-> Favorites) takes over
        _all = entries.Select(Reuse).ToList();
        foreach (var gone in _known.Values.Except(_all).ToList()) Forget(gone);
        ApplyFilter();
    }

    HistoryItemViewModel Reuse(HistoryEntry entry)
    {
        if (_known.TryGetValue(entry.Id, out var item) && item.Entry.FilePath == entry.FilePath && item.Entry.ThumbnailUrl == entry.ThumbnailUrl)
        {
            item.Sync(entry);
            return item;
        }
        if (item is not null) Forget(item);
        return _known[entry.Id] = new HistoryItemViewModel(this, entry);
    }

    void Forget(HistoryItemViewModel item)
    {
        _known.Remove(item.Entry.Id);
        _hidden.Remove(item);
        item.UnloadThumbnail();
    }

    /// <summary>Window closed: drop the items and their thumbnails.</summary>
    public void Release()
    {
        foreach (var item in _known.Values) item.UnloadThumbnail();
        _known.Clear();
        _hidden.Clear();
        _all = [];
        Items = [];
    }

    void ApplyFilter()
    {
        var shown = _all.Where(i => !FavoritesOnly || i.IsFavorite).ToList();
        // Nothing changed: the grid stays exactly as it is (no cards rebuilt, nothing fades in again).
        if (!shown.SequenceEqual(Items)) Items = new ObservableCollection<HistoryItemViewModel>(shown);
        IsEmpty = shown.Count == 0;
    }

    internal void SetFavorite(HistoryItemViewModel item, bool value)
    {
        history.SetFavorite(item.Entry.Id, value);
        if (FavoritesOnly && !value)
        {
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
        }
    }

    internal async Task ReapplyAsync(HistoryItemViewModel item)
    {
        if (Stage is not null) return;
        Stage = PhaseText.For(ApplyPhase.Changing);
        try
        {
            // The same words as everywhere else while it downloads (a favorite from another device), sets and syncs.
            await PhaseWatch.RunAsync(wallpapers, () => Task.Run(async () =>
            {
                await wallpapers.ReapplyAsync(item.Entry);
                return new ApplyResult(ApplyStatus.Applied, []);
            }), stage => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (stage is not null) Stage = stage; }));
        }
        catch (Exception ex)
        {
            Info.Show(InfoSeverity.Informational, "", $"Couldn't re-apply: {ex.Message}");
        }
        finally
        {
            Stage = null;
        }
    }

    internal async Task DeleteAsync(HistoryItemViewModel item)
    {
        var text = item.IsFavorite
            ? "This is a favorite. The file will be deleted, and Oriel won't pick this photo again."
            : "The file will be deleted, and Oriel won't pick this photo again.";
        if (ConfirmDelete is not null && !await ConfirmDelete(text)) return;

        retention.Delete(item.Entry.Id);
        Forget(item);
        _all.Remove(item);
        Items.Remove(item);
        IsEmpty = Items.Count == 0;
    }
}
