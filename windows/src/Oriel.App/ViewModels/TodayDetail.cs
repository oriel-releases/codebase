using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core.Models;
using Oriel.Core.Services;
using Oriel.UI.Services;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Oriel.UI.ViewModels;

/// <summary>
/// The Wallpaper of the day, opened on the photo page from Home's card: setting it is Home's own "set today", so the
/// card and the page show the same progress.
/// </summary>
public sealed partial class TodayDetail : ObservableObject, IPhotoDetail
{
    readonly HomeViewModel _home;
    readonly TodayPhoto _photo;

    public TodayDetail(HomeViewModel home, TodayPhoto photo, ImageSource? preview)
    {
        _home = home;
        _photo = photo;
        Preview = preview;
        home.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.TodayButtonText)) OnPropertyChanged(nameof(ButtonText));
            if (e.PropertyName == nameof(HomeViewModel.CanSetToday)) OnPropertyChanged(nameof(IsIdle));
        };
    }

    public ImageSource? Preview { get; }
    public double AspectRatio => 16 / 9.0;
    public string Headline => "Wallpaper of the day";
    public string Facts => DateTime.Now.ToString("dddd, d MMMM");
    public string Subtitle => $"Photo by {_photo.By}";
    public IReadOnlyList<string> Chips => [Facts];
    public string PhotographerName => _photo.By;
    public Uri PhotographerUri => _photo.AuthorUri;
    public Uri PhotoPageUri => new($"https://unsplash.com/photos/{_photo.Id}?utm_source=oriel&utm_medium=referral");
    public string ButtonText => _home.TodayButtonText;
    public bool IsIdle => _home.CanSetToday;
    public IAsyncRelayCommand SetCommand => _home.SetTodayCommand;
    public bool CanFavorite => false;
    bool IPhotoDetail.IsFavorite { get => false; set { } }
    public bool CanDelete => false;
    public Task<bool> RemoveAsync(Func<string, Task<bool>> confirm) => Task.FromResult(false);
    public bool CanOpenFolder => false;
    public void OpenFolder() { }

    public HistoryEntry ShareEntry => new()
    {
        Provider = "unsplash", PhotoId = _photo.Id, FilePath = "", ThumbnailUrl = $"https://images.unsplash.com/{_photo.Path}",
        PhotoPageUrl = PhotoPageUri.ToString(), PhotographerName = _photo.By, PhotographerUrl = _photo.AuthorUri.ToString(),
        AppliedAt = DateTimeOffset.Now,
    };

    /// <summary>Unsplash makes it at exactly the size asked for, so it's sharp on any screen.</summary>
    public Task<ImageSource?> LoadLargeAsync(int pixelWidth) => ImageLoader.LoadUrlAsync(
        $"https://images.unsplash.com/{_photo.Path}?w={pixelWidth}&q=90&fm=jpg&fit=max", pixelWidth, DecodePixelType.Physical);
}
