using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core.Models;
using Oriel.Core.Providers;
using Oriel.Core.Services;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Oriel.UI.ViewModels;

/// <summary>
/// A photo someone shared with an oriel://wallpaper link: shows it and, with one button, sets it as the wallpaper.
/// The link carries only the photo's ID; the photo itself is looked up on Unsplash, like in Search.
/// </summary>
public sealed partial class SharedPhotoViewModel(WallpaperService wallpapers) : ObservableObject
{
    PhotoCandidate? _photo;
    int _version;

    /// <summary>Raised when the card opens or closes, so the window can show or hide it.</summary>
    public event EventHandler? Changed;

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPhoto))]
    public partial bool IsLoading { get; set; }
    /// <summary>The photo couldn't be looked up (bad or removed link, offline).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPhoto))]
    public partial bool LoadFailed { get; set; }
    [ObservableProperty] public partial string ErrorMessage { get; set; } = "";
    /// <summary>Setting it as the wallpaper failed; the photo stays on screen so the user can try again.</summary>
    [ObservableProperty] public partial bool HasSetError { get; set; }
    [ObservableProperty] public partial string SetError { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSet), nameof(SetText))]
    public partial bool IsSetting { get; set; }

    /// <summary>What setting it is doing now: "Downloading…", "Changing…", "Syncing…".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetText))]
    public partial string? Stage { get; set; }

    public string SetText => IsSetting ? Stage ?? "Downloading…" : "Set as wallpaper";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSet), nameof(ShowActions))]
    public partial bool IsDone { get; set; }
    /// <summary>"Akbar shared a wallpaper with you" (the sender's first name comes in the link).</summary>
    [ObservableProperty] public partial string Heading { get; set; } = "Someone shared a wallpaper with you";
    [ObservableProperty] public partial string PhotographerName { get; set; } = "";
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial ImageSource? Preview { get; set; }

    public bool ShowPhoto => !IsLoading && !LoadFailed;
    public bool CanSet => !IsSetting && !IsDone;
    public bool ShowActions => !IsDone;

    /// <summary>Looks the photo up and opens the card. A newer link replaces an older one.</summary>
    public async Task OpenAsync(string photoId, string from = "")
    {
        var version = ++_version;
        Heading = from.Length > 0 ? $"{from} shared a wallpaper with you" : "Someone shared a wallpaper with you";
        _photo = null;
        ClearPreview();
        IsDone = false;
        IsSetting = false;
        LoadFailed = false;
        HasSetError = false;
        IsLoading = true;
        IsOpen = true;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var photo = await wallpapers.GetSharedPhotoAsync(photoId);
            if (version != _version) return;
            _photo = photo;
            PhotographerName = photo.PhotographerName;
            Description = photo.Description ?? "";
            Preview = new BitmapImage(new Uri(photo.ThumbnailUrl)) { DecodePixelWidth = 720, DecodePixelType = DecodePixelType.Logical };
        }
        catch (ProviderException ex)
        {
            if (version != _version) return;
            Fail(ex.Kind == ProviderErrorKind.NoResults ? "That photo isn't available any more." : ex.Message);
        }
        finally
        {
            if (version == _version) IsLoading = false;
        }
    }

    void Fail(string message)
    {
        ErrorMessage = message;
        LoadFailed = true;
    }

    [RelayCommand]
    async Task Set()
    {
        if (_photo is null || IsSetting) return;
        IsSetting = true;
        HasSetError = false;
        try
        {
            var photo = _photo;
            var result = await PhaseWatch.RunAsync(wallpapers, () => Task.Run(() => wallpapers.ApplyPhotoAsync(photo, "shared")),
                stage => App.Dispatcher.TryEnqueue(() => Stage = stage));
            if (result.Status == ApplyStatus.Applied)
            {
                IsDone = true;
            }
            else
            {
                SetError = result.Error ?? "Couldn't set the wallpaper. Try again.";
                HasSetError = true;
            }
        }
        finally
        {
            IsSetting = false;
        }
    }

    [RelayCommand]
    void Dismiss()
    {
        _version++;
        _photo = null;
        ClearPreview();
        IsOpen = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void ClearPreview()
    {
        Preview = null;
    }
}
