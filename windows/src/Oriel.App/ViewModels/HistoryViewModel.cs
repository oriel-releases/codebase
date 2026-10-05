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
    public HistoryEntry Entry { get; } = entry;
    public string PhotographerName => Entry.PhotographerName;
    public Uri PhotographerUri { get; } = new(entry.PhotographerUrl);
    public Uri PhotoPageUri { get; } = new(entry.PhotoPageUrl);
    public string DateText => Entry.AppliedAt.LocalDateTime.ToString("ddd, d MMM yyyy");
    public string Topic => Entry.Topic ?? "";
    public bool FileExists => File.Exists(Entry.FilePath);

    [ObservableProperty] public partial ImageSource? Thumbnail { get; set; }
    [ObservableProperty] public partial bool IsFavorite { get; set; } = entry.IsFavorite;

    partial void OnIsFavoriteChanged(bool value) => owner.SetFavorite(this, value);

    [RelayCommand] Task Reapply() => owner.ReapplyAsync(this);
    [RelayCommand] Task Delete() => owner.DeleteAsync(this);
    [RelayCommand] void OpenInFolder() => ImageLoader.ShowInExplorer(Entry.FilePath);
}

public sealed partial class HistoryViewModel(IHistoryStore history, WallpaperService wallpapers, RetentionService retention)
    : ObservableObject
{
    List<HistoryItemViewModel> _all = [];

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    /// <summary>Set by the page: shows a ContentDialog and returns true if the user confirmed.</summary>
    public Func<string, Task<bool>>? ConfirmDelete { get; set; }

    [ObservableProperty] public partial bool IsGrid { get; set; } = true;
    [ObservableProperty] public partial bool IsList { get; set; }
    [ObservableProperty] public partial int FilterIndex { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial string? Message { get; set; }
    [ObservableProperty] public partial bool IsMessageOpen { get; set; }

    partial void OnIsGridChanged(bool value) => IsList = !value;
    partial void OnIsListChanged(bool value) => IsGrid = !value;
    partial void OnFilterIndexChanged(int value) => ApplyFilter();

    public async Task LoadAsync()
    {
        _all = history.List().Select(e => new HistoryItemViewModel(this, e)).ToList();
        ApplyFilter();
        // Thumbnails after the grid is populated, so the page shows up instantly.
        foreach (var item in _all.ToList())
            item.Thumbnail = await ImageLoader.LoadAsync(item.Entry.FilePath, item.Entry.ThumbnailUrl, 320);
    }

    /// <summary>Window closed: drop the items and their thumbnails.</summary>
    public void Release()
    {
        _all = [];
        Items.Clear();
    }

    void ApplyFilter()
    {
        Items.Clear();
        foreach (var i in _all.Where(i => FilterIndex == 0 || i.IsFavorite)) Items.Add(i);
        IsEmpty = Items.Count == 0;
    }

    internal void SetFavorite(HistoryItemViewModel item, bool value)
    {
        history.SetFavorite(item.Entry.Id, value);
        if (FilterIndex == 1 && !value) ApplyFilter();
    }

    internal async Task ReapplyAsync(HistoryItemViewModel item)
    {
        try
        {
            await wallpapers.ReapplyAsync(item.Entry);
            Show($"Wallpaper set back to the photo by {item.PhotographerName}.");
        }
        catch (Exception ex)
        {
            Show($"Couldn't re-apply: {ex.Message}");
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
