using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using Oriel.Core.Models;
using Oriel.Core.Providers;
using Oriel.Core.Services;
using Oriel.Core.Storage;
using Oriel.Core.Wallpaper;

namespace Oriel.UI.ViewModels;

/// <summary>
/// The position editor, like the phone's and Windows': the whole photo on screen with this screen's shape over it.
/// Smart (Unsplash's crop, where it really falls), Center, or Custom (dragged). Set downloads the photo cropped there
/// and sets it, and the account's other devices crop it around the same point. The view does the drawing and dragging.
/// </summary>
public sealed partial class PositionEditorViewModel : ObservableObject
{
    /// <summary>How wide the whole photo is shown (enough for a big window, small enough to load at once).</summary>
    const int WholeWidth = 1600;

    readonly WallpaperService _wallpapers;
    readonly IHistoryStore _history;
    readonly IPositionStore _positions;
    readonly HttpClient _http;
    HistoryEntry? _entry;
    PhotoPosition _saved = PhotoPosition.Smart;
    int _openVersion;

    public PositionEditorViewModel(WallpaperService wallpapers, IHistoryStore history, IPositionStore positions, HttpClient http)
    {
        _wallpapers = wallpapers;
        _history = history;
        _positions = positions;
        _http = http;
        _wallpapers.PhaseChanged += (_, phase) => Dispatcher.UIThread.Post(() =>
        {
            if (IsBusy && phase != ApplyPhase.Done) ButtonText = PhaseText.For(phase);
        });
    }

    [ObservableProperty] public partial Bitmap? Whole { get; set; }
    /// <summary>The photo's width over its height (known once its picture is in).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Window), nameof(ShownCenter))]
    public partial double PhotoAspect { get; set; }
    /// <summary>The picture is in and the smart crop found: the editor shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Window), nameof(ShownCenter), nameof(CanEditNow), nameof(IsLoading))]
    public partial bool IsReady { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownCenter), nameof(IsSmart), nameof(IsCenter), nameof(IsCustom))]
    public partial PositionMode Mode { get; set; }
    /// <summary>Custom: the window's middle, 0..1 along the side it moves on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownCenter))]
    public partial double CustomCenter { get; set; } = 0.5;
    /// <summary>Where Unsplash's smart crop falls on this photo (0..1 along); the middle until found.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownCenter))]
    public partial double SmartAt { get; set; } = 0.5;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditNow))]
    public partial bool IsBusy { get; set; }

    /// <summary>Loaded and not setting: the choices, the photo and Set take input (while Set works, nothing does).</summary>
    public bool CanEditNow => IsReady && !IsBusy;
    [ObservableProperty] public partial string ButtonText { get; set; } = "Set";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(HasError))]
    public partial string? Error { get; set; }

    public bool IsLoading => !IsReady && Error is null;
    public bool HasError => Error is not null;

    /// <summary>The screen's width over its height (the shape the wallpaper is cut to).</summary>
    public double ScreenAspect { get; private set; } = 16 / 9.0;

    /// <summary>The window over the photo, in a photo 1 tall and <see cref="PhotoAspect"/> wide.</summary>
    public CropWindow? Window => IsReady && PhotoAspect > 0 ? CropWindow.Fit(PhotoAspect, 1, ScreenAspect) : null;

    /// <summary>Where the window's middle is for the picked mode (0..1 along).</summary>
    public double ShownCenter => Window is { } w ? w.CenterOf(Position, SmartAt) : 0.5;

    public PhotoPosition Position => Mode switch
    {
        PositionMode.Focus => Window?.FocusAt(CustomCenter) ?? PhotoPosition.Center,
        PositionMode.Center => PhotoPosition.Center,
        _ => PhotoPosition.Smart,
    };

    // The three choices, for the switch.
    public bool IsSmart { get => Mode == PositionMode.Smart; set { if (value) Mode = PositionMode.Smart; } }
    public bool IsCenter { get => Mode == PositionMode.Center; set { if (value) { Mode = PositionMode.Center; CustomCenter = 0.5; } } }
    public bool IsCustom { get => Mode == PositionMode.Focus; set { if (value) Mode = PositionMode.Focus; } }

    /// <summary>The wallpaper on screen can be moved: an Unsplash photo (an AI one comes whole).</summary>
    public bool CanEdit => _history.Latest() is { Provider: "unsplash" };

    /// <summary>Opens on the wallpaper on screen: its picture, its position, where its smart crop falls.</summary>
    public async Task OpenAsync()
    {
        if (_history.Latest() is not { Provider: "unsplash" } entry) return;
        var version = ++_openVersion;
        _entry = entry;
        _saved = _positions.Get(entry.PhotoId) ?? PhotoPosition.Smart;
        var size = _wallpapers.ScreenSize();
        ScreenAspect = (double)size.Width / size.Height;
        IsReady = false;
        Error = null;
        ButtonText = "Set";
        Whole = null;
        Mode = _saved.Mode;
        try
        {
            // Usually ready already (Home prepared it as the photo showed): then the editor opens at once.
            var prepared = await PreparedFor(entry, ScreenAspect);
            if (version != _openVersion) return;
            var picture = await Task.Run(() => new Bitmap(new MemoryStream(prepared.Whole)));
            if (version != _openVersion) { picture.Dispose(); return; }
            Whole = picture;
            PhotoAspect = prepared.Aspect;
            SmartAt = prepared.SmartAt;
            CustomCenter = CropWindow.Fit(prepared.Aspect, 1, ScreenAspect).CenterOf(_saved, SmartAt);
            IsReady = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or ArgumentException or InvalidOperationException)
        {
            if (version == _openVersion) Error = "Couldn't load the photo. Check your connection and try again.";
        }
    }

    /// <summary>Closed (or closing): a picture still loading is no longer wanted, nor a crop downloaded ahead.</summary>
    public void Close()
    {
        _openVersion++;
        _settle?.Cancel();
        _wallpapers.DiscardPrefetchedCrop();
    }

    /// <summary>
    /// Loads what the editor needs for the wallpaper on screen ahead of time (Home calls it as each photo shows): the
    /// whole photo and where its smart crop falls, and the whole photo at the screen's size for Set to cut. Then
    /// Position opens at once and Set has nothing to download. Unsplash's CDN, not its API.
    /// </summary>
    public async Task PrepareAsync()
    {
        if (_history.Latest() is not { Provider: "unsplash" } entry) return;
        var size = _wallpapers.ScreenSize();
        try { await PreparedFor(entry, (double)size.Width / size.Height); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or ArgumentException or InvalidOperationException)
        {
            // Offline: opening the editor tries again.
        }
    }

    /// <summary>
    /// What the editor shows for one photo: its whole picture (still encoded), its shape, and its smart crop; and the
    /// whole photo at the screen's size (<see cref="Full"/>), which Set cuts here instead of waiting for Unsplash.
    /// </summary>
    sealed record Prepared(string PhotoId, double ScreenAspect, byte[] Whole, double Aspect, double SmartAt, Task<byte[]?> Full);

    Prepared? _prepared;
    Task<Prepared>? _preparing;
    string? _preparingId;

    Task<Prepared> PreparedFor(HistoryEntry entry, double screenAspect)
    {
        if (_prepared is { } done && done.PhotoId == entry.PhotoId && done.ScreenAspect == screenAspect) return Task.FromResult(done);
        if (_preparing is { IsFaulted: false, IsCanceled: false } running && _preparingId == entry.PhotoId) return running;
        _preparingId = entry.PhotoId;
        return _preparing = LoadPreparedAsync(entry, screenAspect);
    }

    async Task<Prepared> LoadPreparedAsync(HistoryEntry entry, double screenAspect)
    {
        var raw = UnsplashProvider.RawOf(entry.ThumbnailUrl);
        var bytes = await _http.GetByteArrayAsync($"{raw}{(raw.Contains('?') ? '&' : '?')}w={WholeWidth}&q=85&fm=jpg&fit=max");
        var aspect = await Task.Run(() =>
        {
            using var codec = SKCodec.Create(new MemoryStream(bytes)) ?? throw new InvalidOperationException("Not a picture");
            return (double)codec.Info.Width / codec.Info.Height;
        });
        var full = DownloadFullAsync(raw, aspect);
        var smartAt = await FindSmartAsync(raw, aspect, screenAspect) ?? 0.5;
        return _prepared = new Prepared(entry.PhotoId, screenAspect, bytes, aspect, smartAt, full);
    }

    /// <summary>The whole photo, big enough that any cut of it covers the screen; null when it can't be had.</summary>
    async Task<byte[]?> DownloadFullAsync(string raw, double aspect)
    {
        try
        {
            var width = LocalCrop.WholeWidthFor(aspect, _wallpapers.ScreenSize());
            return await _http.GetByteArrayAsync($"{raw}{(raw.Contains('?') ? '&' : '?')}w={width}&q=90&fm=jpg&fit=max");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    /// <summary>The screen's shape cut out of the whole photo where <paramref name="position"/> says, as a JPEG; null if it can't be.</summary>
    byte[]? Cut(byte[] full, PhotoPosition position, double smartAt)
    {
        try
        {
            using var whole = SKBitmap.Decode(full);
            if (whole is null) return null;
            var cut = LocalCrop.Plan(whole.Width, whole.Height, _wallpapers.ScreenSize(), position, smartAt);
            using var part = new SKBitmap();
            if (!whole.ExtractSubset(part, SKRectI.Create(cut.X, cut.Y, cut.Width, cut.Height))) return null;
            using var sized = cut.OutWidth == cut.Width ? part.Copy()
                : part.Resize(new SKImageInfo(cut.OutWidth, cut.OutHeight), new SKSamplingOptions(SKCubicResampler.Mitchell));
            if (sized is null) return null;
            using var image = SKImage.FromBitmap(sized);
            using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 95);
            return jpeg?.ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>How long the window rests before its crop starts downloading: past a click's settle, short of reaching for Set.</summary>
    const int SettleMs = 300;
    CancellationTokenSource? _settle;

    partial void OnModeChanged(PositionMode value) => PrefetchSoon();
    partial void OnCustomCenterChanged(double value) => PrefetchSoon();

    /// <summary>
    /// Once the window rests, its crop starts downloading, so Set has nothing left to wait for even before the whole
    /// photo is in (with it, Set cuts here and this one isn't used).
    /// </summary>
    void PrefetchSoon()
    {
        _settle?.Cancel();
        if (!IsReady || IsBusy || _entry is not { } entry) return;
        if (_prepared is { Full.IsCompletedSuccessfully: true } p && p.Full.Result is not null && p.PhotoId == entry.PhotoId) return;
        var settle = _settle = new CancellationTokenSource();
        _ = Task.Delay(SettleMs, settle.Token).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (settle.IsCancellationRequested || !IsReady || IsBusy || Position == _saved) return;
            _wallpapers.PrefetchCrop(entry, Position);
        }), TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    /// <summary>Dragged or clicked on the photo: the window's middle goes there (Custom).</summary>
    public void PlaceAt(double center)
    {
        Mode = PositionMode.Focus;
        CustomCenter = center;
    }

    /// <summary>Set: downloads the crop and sets it (nothing to do when it didn't move). True when the editor can go.</summary>
    public async Task<bool> SetAsync()
    {
        if (_entry is null || IsBusy) return false;
        var position = Position;
        if (position == _saved) return true;
        IsBusy = true;
        Error = null;
        // Usually the crop is here already (downloaded as the window settled): it says Downloading only if it isn't.
        ButtonText = PhaseText.For(ApplyPhase.Changing);
        try
        {
            // Cut here from the whole photo when it's in (at once); else Unsplash's crop (downloaded ahead, or now).
            var entry = _entry;
            var prepared = _prepared is { } p && p.PhotoId == entry.PhotoId && p.Full.IsCompletedSuccessfully ? p : null;
            var result = await Task.Run(() =>
            {
                var cut = prepared?.Full.Result is { } full ? Cut(full, position, prepared.SmartAt) : null;
                return _wallpapers.RepositionAsync(entry, position, image: cut);
            });
            if (result.Status == ApplyStatus.Applied)
            {
                _saved = position;
                return true;
            }
            Error = result.Error ?? "Couldn't move it. Try again.";
            return false;
        }
        finally
        {
            IsBusy = false;
            ButtonText = "Set";
        }
    }

    /// <summary>Where the smart crop falls (0..1 along), or null when the small pictures can't be had.</summary>
    async Task<double?> FindSmartAsync(string raw, double aspect, double screenAspect)
    {
        try
        {
            var (wholeUrl, cropUrl, across) = SmartCrop.SampleUrls(raw, aspect, screenAspect);
            var whole = LumaAsync(wholeUrl);
            var crop = LumaAsync(cropUrl);
            var (w, ww, wh) = await whole;
            var (c, cw, ch) = await crop;
            return await Task.Run(() => SmartCrop.Locate(w, ww, wh, c, cw, ch, across));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A small picture's brightness, one byte a pixel.</summary>
    async Task<(byte[] Luma, int Width, int Height)> LumaAsync(string url)
    {
        var bytes = await _http.GetByteArrayAsync(url);
        return await Task.Run(() =>
        {
            using var bitmap = new Bitmap(new MemoryStream(bytes));
            var size = bitmap.PixelSize;
            var stride = size.Width * 4;
            var pixels = new byte[stride * size.Height];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), pixels.Length, stride);
            }
            finally
            {
                handle.Free();
            }
            // Brightness weighs red and blue alike enough that B,G,R,A and R,G,B,A give nearly the same match.
            var luma = new byte[size.Width * size.Height];
            for (var i = 0; i < luma.Length; i++)
                luma[i] = (byte)((pixels[i * 4] * 2 + pixels[i * 4 + 1] * 6 + pixels[i * 4 + 2] * 2) / 10);
            return (luma, size.Width, size.Height);
        });
    }
}
