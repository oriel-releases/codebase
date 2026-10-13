using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core.Models;
using Oriel.Core.Services;
using Oriel.Core.Storage;
using Oriel.UI.Services;
using Microsoft.UI.Xaml.Media;

namespace Oriel.UI.ViewModels;

public sealed partial class HistoryItemViewModel(HistoryViewModel owner, HistoryEntry entry) : ObservableObject
{
    public HistoryEntry Entry { get; private set; } = entry;
    public string PhotographerName => Entry.PhotographerName;
    public Uri PhotographerUri { get; } = new(entry.PhotographerUrl);
    public Uri PhotoPageUri { get; } = new(entry.PhotoPageUrl);
    public string DateText => Entry.AppliedAt.LocalDateTime.ToString("ddd, d MMM yyyy");
    public string Topic => Entry.Topic ?? "";
    public bool FileExists => File.Exists(Entry.FilePath);
    /// <summary>A favorite from another device has no file here yet: setting it downloads it.</summary>
    public bool CanReapply => FileExists || Entry.Provider == "unsplash";

    [ObservableProperty] public partial ImageSource? Thumbnail { get; set; }
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

    [RelayCommand] Task Reapply() => owner.ReapplyAsync(this);
    [RelayCommand] Task Delete() => owner.DeleteAsync(this);
    [RelayCommand] void OpenInFolder() => ImageLoader.ShowInExplorer(Entry.FilePath);
}

public sealed partial class HistoryViewModel(IHistoryStore history, WallpaperService wallpapers, RetentionService retention)
    : ObservableObject
{
    List<HistoryItemViewModel> _all = [];
    /// <summary>Items by history id, kept between visits: coming back shows the same cards and thumbnails at once.</summary>
    readonly Dictionary<long, HistoryItemViewModel> _known = [];

    /// <summary>
    /// The photos shown. Swapped as a whole (never cleared and refilled item by item): changing a list while the grid
    /// is still laying out the last change made WinUI fail with "Unspecified error" when History/Favorites switched fast.
    /// </summary>
    [ObservableProperty] public partial ObservableCollection<HistoryItemViewModel> Items { get; set; } = [];
    int _loadVersion;

    /// <summary>Set by the page: shows a ContentDialog and returns true if the user confirmed.</summary>
    public Func<string, Task<bool>>? ConfirmDelete { get; set; }

    [ObservableProperty] public partial bool IsGrid { get; set; } = true;
    [ObservableProperty] public partial bool IsList { get; set; }
    /// <summary>The same page serves History (everything) and Favorites (starred photos only).</summary>
    [ObservableProperty] public partial bool FavoritesOnly { get; set; }
    public string Title => FavoritesOnly ? "Favorites" : "History";
    public string EmptyText => FavoritesOnly ? "No favorites yet. Star a photo to keep it here." : "Nothing here yet.";
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial string? Message { get; set; }
    [ObservableProperty] public partial bool IsMessageOpen { get; set; }
    /// <summary>What "Set again" is doing now (Downloading… / Changing… / Syncing…), or null.</summary>
    [ObservableProperty] public partial string? Stage { get; set; }
    public bool IsChanging => Stage is not null;
    partial void OnStageChanged(string? value) => OnPropertyChanged(nameof(IsChanging));

    partial void OnIsGridChanged(bool value) => IsList = !value;
    partial void OnIsListChanged(bool value) => IsGrid = !value;

    public async Task LoadAsync(bool favoritesOnly = false)
    {
        FavoritesOnly = favoritesOnly;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(EmptyText));
        var version = ++_loadVersion;
        _all = history.List().Select(Reuse).ToList();
        foreach (var gone in _known.Keys.Except(_all.Select(i => i.Entry.Id)).ToList()) _known.Remove(gone);
        ApplyFilter();
        // Thumbnails after the grid is populated, so the page shows up instantly; only the ones not loaded yet. A newer
        // load (History <-> Favorites) takes over: this one stops.
        foreach (var item in _all.Where(i => i.Thumbnail is null).ToList())
        {
            if (version != _loadVersion) return;
            item.Thumbnail = await ImageLoader.LoadAsync(item.Entry.FilePath, item.Entry.ThumbnailUrl, 320);
        }
    }

    HistoryItemViewModel Reuse(HistoryEntry entry)
    {
        if (_known.TryGetValue(entry.Id, out var item) && item.Entry.FilePath == entry.FilePath && item.Entry.ThumbnailUrl == entry.ThumbnailUrl)
        {
            item.Sync(entry);
            return item;
        }
        return _known[entry.Id] = new HistoryItemViewModel(this, entry);
    }

    /// <summary>Window closed: drop the items and their thumbnails.</summary>
    public void Release()
    {
        _loadVersion++;
        _all = [];
        _known.Clear();
        Items = [];
    }

    void ApplyFilter()
    {
        var shown = _all.Where(i => !FavoritesOnly || i.IsFavorite).ToList();
        // Nothing changed: the grid stays exactly as it is (no cards rebuilt, nothing animates in again).
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
            }), stage => App.Dispatcher.TryEnqueue(() => { if (stage is not null) Stage = stage; }));
        }
        catch (Exception ex)
        {
            Show($"Couldn't re-apply: {ex.Message}");
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
        _all.Remove(item);
        Items.Remove(item);
        IsEmpty = Items.Count == 0;
    }

    void Show(string message)
    {
        Message = message;
        IsMessageOpen = true;
    }
}
