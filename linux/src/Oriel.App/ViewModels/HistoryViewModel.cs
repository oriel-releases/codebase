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

    public HistoryEntry Entry { get; } = entry;
    public string PhotographerName => Entry.PhotographerName;
    public Uri PhotographerUri { get; } = new(entry.PhotographerUrl);
    public string DateText => Entry.AppliedAt.LocalDateTime.ToString("ddd, d MMM yyyy");
    public string Topic => Entry.Topic ?? "";
    public string Details => Topic.Length == 0 ? DateText : $"{DateText} · {Topic}";
    public bool FileExists => File.Exists(Entry.FilePath);

    [ObservableProperty] public partial Bitmap? Thumbnail { get; set; }
    [ObservableProperty] public partial bool IsFavorite { get; set; } = entry.IsFavorite;

    partial void OnIsFavoriteChanged(bool value) => owner.SetFavorite(this, value);

    /// <summary>Thumbnails exist only while their card is on screen, so a long history stays cheap.</summary>
    public async Task LoadThumbnailAsync()
    {
        if (Thumbnail is not null) return;
        var version = ++_loadVersion;
        var bitmap = await ImageLoader.LoadAsync(Entry.FilePath, Entry.ThumbnailUrl, ThumbnailWidth);
        if (version != _loadVersion) { bitmap?.Dispose(); return; } // scrolled away while loading
        Thumbnail = bitmap;
    }

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

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];
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

    partial void OnIsGridChanged(bool value) => IsList = !value;
    partial void OnIsListChanged(bool value) => IsGrid = !value;

    public async Task LoadAsync(bool favoritesOnly = false)
    {
        FavoritesOnly = favoritesOnly;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(EmptyText));
        var entries = await Task.Run(() => history.List());
        Release();
        _all = entries.Select(e => new HistoryItemViewModel(this, e)).ToList();
        ApplyFilter();
    }

    /// <summary>Window closed: drop the items and their thumbnails.</summary>
    public void Release()
    {
        foreach (var item in _all) item.UnloadThumbnail();
        _all = [];
        Items.Clear();
    }

    void ApplyFilter()
    {
        Items.Clear();
        foreach (var i in _all.Where(i => !FavoritesOnly || i.IsFavorite)) Items.Add(i);
        IsEmpty = Items.Count == 0;
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
        try
        {
            await Task.Run(() => wallpapers.ReapplyAsync(item.Entry));
            Info.Show(InfoSeverity.Informational, "", $"Wallpaper set back to the photo by {item.PhotographerName}.");
        }
        catch (Exception ex)
        {
            Info.Show(InfoSeverity.Informational, "", $"Couldn't re-apply: {ex.Message}");
        }
    }

    internal async Task DeleteAsync(HistoryItemViewModel item)
    {
        var text = item.IsFavorite
            ? "This is a favorite. The file will be deleted, and Oriel won't pick this photo again."
            : "The file will be deleted, and Oriel won't pick this photo again.";
        if (ConfirmDelete is not null && !await ConfirmDelete(text)) return;

        retention.Delete(item.Entry.Id);
        item.UnloadThumbnail();
        _all.Remove(item);
        Items.Remove(item);
        IsEmpty = Items.Count == 0;
    }
}
