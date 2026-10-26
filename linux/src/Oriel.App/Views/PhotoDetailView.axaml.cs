using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Oriel.UI.Services;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

/// <summary>
/// A photo from Explore, History or Favorites on its own page, over the page it came from (which keeps its place). The
/// card flies into place and grows into the photo, and flies back into its card on the way out, like on Windows.
/// </summary>
public partial class PhotoDetailView : UserControl
{
    /// <summary>The flight, both ways: a little under half a second, quick at first and settling slowly, like on Windows.</summary>
    static readonly TimeSpan FlyTime = TimeSpan.FromMilliseconds(460);
    Control? _from;
    Bitmap? _large;
    int _version;

    public PhotoDetailView() => InitializeComponent();

    public bool IsOpen => IsVisible;

    /// <summary>
    /// Makes a card open its photo here when clicked; clicks on its own buttons and links stay theirs. <paramref name="picture"/>
    /// is what flies (the card itself, or a list row's thumbnail). The pointer over it starts loading the sharp photo,
    /// so the page opens with it.
    /// </summary>
    public static void Wire(Control card, Func<Control>? picture = null)
    {
        card.Cursor = new Cursor(StandardCursorType.Hand);
        card.PointerEntered += (_, _) =>
        {
            if (card.DataContext is IPhotoDetail photo && TopLevel.GetTopLevel(card) is MainWindow window) window.PhotoDetail.Prefetch(photo);
        };
        card.Tapped += (_, e) =>
        {
            if (OnButton(e.Source as Visual, card) || card.DataContext is not IPhotoDetail photo) return;
            if (TopLevel.GetTopLevel(card) is MainWindow window) window.PhotoDetail.Open(photo, picture?.Invoke() ?? card);
        };
    }

    /// <summary>The photo whose sharp copy is loading or loaded, ahead of its page opening.</summary>
    (IPhotoDetail Photo, Task<Bitmap?> Image)? _early;

    /// <summary>The page's photo width in real pixels: the content area (less the menu and margins), at most 960 wide.</summary>
    int PagePixels()
    {
        var top = TopLevel.GetTopLevel(this);
        var points = Math.Min(960, (top?.ClientSize.Width ?? 1100) - 220 - 80);
        return (int)Math.Min(4096, Math.Ceiling(Math.Max(320, points) * (top?.RenderScaling ?? 1) / 256) * 256);
    }

    public void Prefetch(IPhotoDetail photo)
    {
        if (_early?.Photo == photo) return;
        // The last one hovered but never opened: its sharp photo isn't needed any more.
        if (_early is { Image.IsCompletedSuccessfully: true } old && old.Image.Result is { } unused && !ReferenceEquals(unused, _large))
            unused.Dispose();
        _early = (photo, photo.LoadLargeAsync(PagePixels()));
    }

    static bool OnButton(Visual? from, Visual card)
    {
        for (var node = from; node is not null && node != card; node = node.GetVisualParent())
            if (node is Button or ToggleButton) return true;
        return false;
    }

    public async void Open(IPhotoDetail photo, Control from)
    {
        var version = ++_version;
        Prefetch(photo);
        var early = _early!.Value.Image;
        // A moment for the sharp photo (it usually started on hover), so the page opens with it.
        await Task.WhenAny(early, Task.Delay(700));
        if (version != _version) return;
        _from = from;
        DataContext = photo;
        Message.IsVisible = false;
        ShareButton.IsVisible = Oriel.Core.Services.ShareService.CanShare(photo.ShareEntry);
        ReleaseLarge();
        var ready = early.IsCompletedSuccessfully ? early.Result : null;
        PreviewImage.Source = ready ?? photo.Preview;
        if (ready is not null) { _large = ready; _early = null; }
        Info.Opacity = 0;
        Info.RenderTransform = TransformOperations.Parse("translateY(14px)");
        PhotoFrame.RenderTransform = null;
        IsVisible = true;
        // Once laid out: the flight from the card, then the credit and buttons rise in under the photo.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        if (version != _version) return;
        _ = Fly(from, toCard: false);
        // As the photo settles: the credit and buttons rise in under it.
        await Task.Delay(260);
        if (version != _version) return;
        Info.Opacity = 1;
        Info.RenderTransform = TransformOperations.Parse("translateY(0px)");
        if (ready is not null) return;
        // Not here in time: the sharp photo fades in over the card's when it comes.
        var large = await early;
        if (version != _version || !IsVisible) return;
        _early = null;
        _large = large;
        LargeImage.Source = large;
        LargeImage.Opacity = 1;
    }

    IPhotoDetail? _watched;

    /// <summary>A new photo on the page: its button's words, and whether it's the wallpaper already.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_watched is not null) _watched.PropertyChanged -= Photo_PropertyChanged;
        _watched = DataContext as IPhotoDetail;
        if (_watched is null) return;
        _watched.PropertyChanged += Photo_PropertyChanged;
        _shownText = SetText.Text = _watched.ButtonText;
        ShowCurrent();
    }

    void Photo_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IPhotoDetail.ButtonText) && e.PropertyName != nameof(IPhotoDetail.IsIdle)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_watched is null) return;
            _ = SwapTextAsync(_watched.ButtonText);
            ShowCurrent();
        });
    }

    /// <summary>
    /// The photo already on the screen (the newest in History) has nothing to set: "Your wallpaper" shows instead of the
    /// button. Not while it's being set from here: the steps show on the button until it's done.
    /// </summary>
    void ShowCurrent()
    {
        var current = _watched is { IsIdle: true } photo
            && App.Get<Oriel.Core.Storage.IHistoryStore>().List().FirstOrDefault()?.PhotoId == photo.ShareEntry.PhotoId;
        SetButton.IsVisible = !current;
        CurrentPill.IsVisible = current;
    }

    string _shownText = "";

    /// <summary>The button's old words slide up and fade, the new ones rise in from below (Finding a photo…, Downloading…).</summary>
    async Task SwapTextAsync(string text)
    {
        if (text == _shownText) return;
        _shownText = text;
        await Slide(0, -8, 1, 0, new CubicEaseIn()).RunAsync(SetText);
        if (_shownText != text) return; // a newer word is on its way
        SetText.Text = text;
        await Slide(8, 0, 0, 1, new CubicEaseOut()).RunAsync(SetText);
    }

    static Animation Slide(double fromY, double toY, double fromOpacity, double toOpacity, Easing easing) => new()
    {
        Duration = TimeSpan.FromMilliseconds(130),
        Easing = easing,
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.YProperty, fromY), new Setter(OpacityProperty, fromOpacity) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.YProperty, toY), new Setter(OpacityProperty, toOpacity) } },
        },
    };

    /// <summary>Back: the photo flies into its card (gone: the page just goes), then the page underneath shows again.</summary>
    public async void Close()
    {
        if (!IsVisible) return;
        var version = ++_version;
        Info.Opacity = 0;
        if (_from is { } card && card.IsAttachedToVisualTree() && card.IsEffectivelyVisible) await Fly(card, toCard: true);
        if (version != _version) return;
        IsVisible = false;
        PhotoFrame.RenderTransform = null;
        ReleaseLarge();
        DataContext = null;
        _from = null;
    }

    /// <summary>Away at once (another page was picked in the menu).</summary>
    public void CloseNow()
    {
        _version++;
        IsVisible = false;
        ReleaseLarge();
        DataContext = null;
        _from = null;
    }

    /// <summary>Moves and scales the frame between the card's place and its own.</summary>
    Task Fly(Control card, bool toCard)
    {
        if (card.TranslatePoint(new Point(0, 0), PhotoFrame) is not { } at || PhotoFrame.Bounds.Width <= 0 || PhotoFrame.Bounds.Height <= 0)
            return Task.CompletedTask;
        PhotoFrame.RenderTransformOrigin = RelativePoint.TopLeft;
        var sx = card.Bounds.Width / PhotoFrame.Bounds.Width;
        var sy = card.Bounds.Height / PhotoFrame.Bounds.Height;
        // A scale, then a move, each animated by its own numbers (Avalonia has no animator for RenderTransform as a whole).
        if (PhotoFrame.RenderTransform is not TransformGroup { Children.Count: 2 })
            PhotoFrame.RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new TranslateTransform() } };
        KeyFrame Frame(double cue, bool onCard) => new()
        {
            Cue = new Cue(cue),
            Setters =
            {
                new Setter(ScaleTransform.ScaleXProperty, onCard ? sx : 1d),
                new Setter(ScaleTransform.ScaleYProperty, onCard ? sy : 1d),
                new Setter(TranslateTransform.XProperty, onCard ? at.X : 0d),
                new Setter(TranslateTransform.YProperty, onCard ? at.Y : 0d),
            },
        };
        var animation = new Animation
        {
            Duration = FlyTime,
            Easing = new SplineEasing(0.2, 0.9, 0.1, 1),
            FillMode = FillMode.Forward,
            Children = { Frame(0, !toCard), Frame(1, toCard) },
        };
        try
        {
            return animation.RunAsync(PhotoFrame);
        }
        catch (Exception ex)
        {
            // Decoration only: the photo still opens and closes without it.
            System.Diagnostics.Debug.WriteLine($"Photo animation skipped: {ex.Message}");
            return Task.CompletedTask;
        }
    }

    void ReleaseLarge()
    {
        LargeImage.Opacity = 0;
        LargeImage.Source = null;
        PreviewImage.Source = null; // it may show the sharp photo itself
        _large?.Dispose();
        _large = null;
    }

    void Back_Click(object? sender, RoutedEventArgs e) => Close();

    const string ShareText = "A wallpaper I like. Open this link to set it on your computer in one click:";

    async Task<string?> LinkAsync()
    {
        if (DataContext is not IPhotoDetail photo) return null;
        try
        {
            return await App.Get<Oriel.Core.Services.ShareService>().LinkAsync(photo.ShareEntry);
        }
        catch (Oriel.Core.Services.ShareException ex)
        {
            Show(ex.Message);
            return null;
        }
    }

    /// <summary>The picture itself on the clipboard (Telegram, WhatsApp and most apps paste it as a photo).</summary>
    async void CopyImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not IPhotoDetail photo || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if (await Oriel.UI.Services.ShareImage.FileForAsync(photo.ShareEntry) is not { } file)
        {
            Show("Couldn't get the picture. Check your connection.");
            return;
        }
        var link = await LinkAsync();
        await CopyImageAsync(clipboard, file, link is null ? null : $"{ShareText} {link}");
    }

    /// <summary>
    /// The picture and the words with the link on the clipboard together: an app that pastes pictures takes the photo,
    /// one that pastes text takes the words.
    /// </summary>
    static async Task CopyImageAsync(Avalonia.Input.Platform.IClipboard clipboard, string file, string? text)
    {
        using var bitmap = new Avalonia.Media.Imaging.Bitmap(file);
        var item = new Avalonia.Input.DataTransferItem();
        item.SetBitmap(bitmap);
        if (text is not null) item.SetText(text);
        var data = new Avalonia.Input.DataTransfer();
        data.Add(item);
        await clipboard.SetDataAsync(data);
    }

    async void CopyLink_Click(object? sender, RoutedEventArgs e)
    {
        if (await LinkAsync() is not { } link) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(link);
    }

    void ShareTelegram_Click(object? sender, RoutedEventArgs e) =>
        _ = OpenAsync(link => $"https://t.me/share/url?url={Uri.EscapeDataString(link)}&text={Uri.EscapeDataString(ShareText)}");

    void ShareWhatsApp_Click(object? sender, RoutedEventArgs e) =>
        _ = OpenAsync(link => "https://wa.me/?text=" + Uri.EscapeDataString(ShareText + " " + link));

    void ShareEmail_Click(object? sender, RoutedEventArgs e) =>
        _ = OpenAsync(link => "mailto:?subject=" + Uri.EscapeDataString("A wallpaper for you") + "&body=" + Uri.EscapeDataString(ShareText) + "%0A%0A" + Uri.EscapeDataString(link));

    async Task OpenAsync(Func<string, string> build)
    {
        if (await LinkAsync() is { } link) Launcher.OpenUrl(new Uri(build(link)));
    }

    void Folder_Click(object? sender, RoutedEventArgs e) => (DataContext as IPhotoDetail)?.OpenFolder();

    /// <summary>Deleted from History (after asking): the page goes, and its card is gone too.</summary>
    async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not IPhotoDetail photo) return;
        if (await photo.RemoveAsync(text => ConfirmDialog.AskAsync(this, "Delete this wallpaper?", text, "Delete", "Cancel"))) CloseNow();
    }

    void Show(string message)
    {
        Message.Text = message;
        Message.IsVisible = true;
    }

    void Photographer_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IPhotoDetail photo) Launcher.OpenUrl(photo.PhotographerUri);
    }

    void Page_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IPhotoDetail photo) Launcher.OpenUrl(photo.PhotoPageUri);
    }
}
