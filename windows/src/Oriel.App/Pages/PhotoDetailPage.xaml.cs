using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace Oriel.UI.Pages;

/// <summary>A photo from Explore, History or Favorites on its own page: bigger, what's known about it, and its actions.</summary>
public sealed partial class PhotoDetailPage : Page
{
    public IPhotoDetail? Photo { get; private set; }
    int _version;
    int _loadedWidth;

    public PhotoDetailPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Photo = e.Parameter as IPhotoDetail;
        Message.IsOpen = false;
        ShareButton.Visibility = Photo is not null && Oriel.Core.Services.ShareService.CanShare(Photo.ShareEntry)
            ? Visibility.Visible : Visibility.Collapsed;
        Bindings.Update();
        var version = ++_version;
        _loadedWidth = 0;
        // The sharp photo, when it's ready (it starts loading on hover, and a click waits a moment for it): shown at once.
        // Else the card's picture, and the sharp one fades in over it.
        if (PhotoDetailNav.Ready(Photo) is { } ready)
        {
            PreviewImage.Source = ready.Image;
            LargeImage.Source = null;
            _loadedWidth = ready.Width;
        }
        else
        {
            PreviewImage.Source = Photo?.Preview;
            LargeImage.Opacity = 0;
            LargeImage.Source = null;
        }
        // The card flies into place and grows into the photo; the credit and buttons then rise in under it.
        PhotoDetailNav.Arrive(PhotoFrame);
        _ = ShowInfoAsync(version);
        _ = LoadLargeAsync(version);
    }

    async Task ShowInfoAsync(int version)
    {
        Info.Opacity = 0;
        Info.Translation = new System.Numerics.Vector3(0, 16, 0);
        // As the photo settles: the text rises in under it.
        await Task.Delay(260);
        if (version != _version) return;
        Info.Opacity = 1;
        Info.Translation = System.Numerics.Vector3.Zero;
    }

    /// <summary>
    /// The sharp photo, at the frame's width in real screen pixels (on a 150% screen a 900-point frame needs 1,350),
    /// so it never looks soft. A frame that grows a lot later (a bigger window) loads it again, bigger.
    /// </summary>
    async Task LoadLargeAsync(int version)
    {
        if (Photo is null || PhotoFrame.ActualWidth <= 0) return;
        var pixels = PhotoDetailNav.Pixels(PhotoFrame.ActualWidth, XamlRoot);
        if (pixels <= _loadedWidth * 1.15) return;
        _loadedWidth = pixels;
        var large = await Photo.LoadLargeAsync(pixels);
        if (version != _version || large is null) return;
        LargeImage.Source = large;
        // Decoded already (a file, or downloaded first): no ImageOpened comes for it, so it shows now.
        if (large is Microsoft.UI.Xaml.Media.Imaging.BitmapSource { PixelWidth: > 0 }) LargeImage.Opacity = 1;
    }

    void LargeImage_Opened(object sender, RoutedEventArgs e) => LargeImage.Opacity = 1;

    void PhotoFrame_SizeChanged(object sender, SizeChangedEventArgs e) => _ = LoadLargeAsync(_version);

    void Back_Click(object sender, RoutedEventArgs e) => GoBack();

    /// <summary>A link to this photo, by the system share sheet (copied when that can't open), like on Home.</summary>
    async void Share_Click(object sender, RoutedEventArgs e)
    {
        if (Photo is null) return;
        string link;
        try
        {
            link = await App.Get<Oriel.Core.Services.ShareService>().LinkAsync(Photo.ShareEntry);
        }
        catch (Oriel.Core.Services.ShareException ex)
        {
            Show(ex.Message);
            return;
        }
        if (Oriel.UI.Services.ShareSheet.Show(App.MainWindowHandle, "A wallpaper for you",
                "A wallpaper I like. Open this link to set it on your computer in one click:", new Uri(link))) return;
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(link);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        Show("Link copied.");
    }

    void Folder_Click(object sender, RoutedEventArgs e) => Photo?.OpenFolder();

    /// <summary>Deleted from History (after asking): back to the page it came from, where its card is gone too.</summary>
    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Photo is null || !await Photo.RemoveAsync(ConfirmDeleteAsync)) return;
        if (Frame.CanGoBack) Frame.GoBack(new SuppressNavigationTransitionInfo());
    }

    async Task<bool> ConfirmDeleteAsync(string text)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete this wallpaper?",
            Content = text,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    void Show(string message)
    {
        Message.Message = message;
        Message.IsOpen = true;
    }

    void Escape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        GoBack();
    }

    void GoBack()
    {
        if (!Frame.CanGoBack) return;
        PhotoDetailNav.Leave(PhotoFrame);
        Frame.GoBack(new SuppressNavigationTransitionInfo());
    }
}

/// <summary>
/// Opening a photo's own page from a card, and coming back to it: the card flies into place on the new page (a connected
/// animation, WinUI's view transition) and flies back into its card on the way out.
/// </summary>
public static class PhotoDetailNav
{
    const string Forward = "photo-open";
    const string Backward = "photo-back";

    /// <summary>
    /// The flight, both ways: a little under half a second, starting quick and settling slowly (an ease-out curve), so
    /// the photo glides into place instead of snapping.
    /// </summary>
    static ConnectedAnimationService Service
    {
        get
        {
            var service = ConnectedAnimationService.GetForCurrentView();
            if (!_tuned)
            {
                _tuned = true;
                service.DefaultDuration = TimeSpan.FromMilliseconds(460);
                service.DefaultEasingFunction = Microsoft.UI.Xaml.Media.CompositionTarget.GetCompositorForCurrentThread()
                    .CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.2f, 0.9f), new System.Numerics.Vector2(0.1f, 1f));
            }
            return service;
        }
    }
    static bool _tuned;

    /// <summary>The photo whose sharp copy is loading or loaded, ahead of its page opening.</summary>
    static (IPhotoDetail Photo, int Width, Task<ImageSource?> Image)? _early;

    /// <summary>The page's photo width in real pixels for a frame <paramref name="points"/> wide.</summary>
    internal static int Pixels(double points, XamlRoot? root) =>
        (int)Math.Min(4096, Math.Ceiling(Math.Max(320, points) * (root?.RasterizationScale ?? 1) / 256) * 256);

    /// <summary>The page shows its photo as wide as the content area (less the page's margins), at most the page width.</summary>
    static int PagePixels(FrameworkElement frame) =>
        Pixels(Math.Min((double)Application.Current.Resources["PageMaxWidth"], frame.ActualWidth - 80), frame.XamlRoot);

    /// <summary>The pointer is over a card: its sharp photo starts loading, so its page can open with it.</summary>
    public static void Prefetch(Frame frame, UIElement card)
    {
        if (card is not FrameworkElement { Tag: IPhotoDetail photo } || _early?.Photo == photo) return;
        var width = PagePixels(frame);
        _early = (photo, width, photo.LoadLargeAsync(width));
    }

    /// <summary>The sharp photo for this page, if it's already here.</summary>
    internal static (ImageSource Image, int Width)? Ready(IPhotoDetail? photo) =>
        _early is { } early && early.Photo == photo && early.Image.IsCompletedSuccessfully && early.Image.Result is { } image
            ? (image, early.Width) : null;

    /// <summary>
    /// A card was clicked (not on one of its own buttons or links): its page opens, with the sharp photo when it comes
    /// within a moment (it usually started on hover), else with the card's picture and the sharp one after.
    /// </summary>
    public static bool Open(Frame frame, TappedRoutedEventArgs e, UIElement card, UIElement? picture = null)
    {
        // The card carries its photo in Tag: x:Bind templates in an ItemsRepeater don't get a DataContext.
        if (OnButton(e.OriginalSource as DependencyObject, card) || card is not FrameworkElement { Tag: IPhotoDetail photo })
            return false;
        Prefetch(frame, card);
        _ = OpenAsync(frame, photo, picture ?? card);
        return true;
    }

    static async Task OpenAsync(Frame frame, IPhotoDetail photo, UIElement picture)
    {
        if (_early is { } early) await Task.WhenAny(early.Image, Task.Delay(700));
        Service.PrepareToAnimate(Forward, picture);
        frame.Navigate(typeof(PhotoDetailPage), photo, new SuppressNavigationTransitionInfo());
    }

    internal static void Arrive(UIElement photo)
    {
        if (Service.GetAnimation(Forward) is not { } animation) return;
        animation.Configuration = new BasicConnectedAnimationConfiguration();
        animation.TryStart(photo);
    }

    internal static void Leave(UIElement photo)
    {
        var animation = Service.PrepareToAnimate(Backward, photo);
        animation.Configuration = new BasicConnectedAnimationConfiguration();
    }

    /// <summary>Back on the page with the cards: the photo flies into the card it came from (gone: it just fades).</summary>
    public static void Return(UIElement? card)
    {
        if (Service.GetAnimation(Backward) is not { } animation) return;
        // A page just made (not kept from before) isn't in the window yet: TryStart waits for it to be laid out.
        if (card is null || !animation.TryStart(card)) animation.Cancel();
    }

    static bool OnButton(DependencyObject? from, DependencyObject card)
    {
        for (var node = from; node is not null && node != card; node = VisualTreeHelper.GetParent(node))
            if (node is ButtonBase) return true;
        return false;
    }
}
