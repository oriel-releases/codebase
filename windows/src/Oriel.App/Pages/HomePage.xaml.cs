using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace Oriel.UI.Pages;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; } = App.Get<HomeViewModel>();

    public HomePage()
    {
        InitializeComponent();
        ChangeText.Text = ViewModel.ChangeLabel;
        Loaded += (_, _) => ViewModel.PropertyChanged += OnViewModelChanged;
        Unloaded += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelChanged;
            StopPicking();
        };
        KeyDown += (_, e) =>
        {
            if (_picking is null || e.Key != Windows.System.VirtualKey.Escape) return;
            StopPicking();
            e.Handled = true;
        };
        // While picking, a click anywhere but the photo (or the eyedropper, which stops it itself) puts it away.
        AddHandler(PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            if (_picking is not null && !Within(e.OriginalSource, PreviewBox) && !Within(e.OriginalSource, EyedropperButton))
                StopPicking();
        }), true);
        BuildLoupe();
    }

    static bool Within(object source, Microsoft.UI.Xaml.DependencyObject parent)
    {
        for (var d = source as Microsoft.UI.Xaml.DependencyObject; d is not null; d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d))
            if (d == parent) return true;
        return false;
    }

    /// <summary>
    /// The ring around the chosen accent color fades from one color to another, but not on its first show: a new ring
    /// starts fully visible and gets its state a moment later, so with the fade on from the start every ring flashed on.
    /// </summary>
    void Ring_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var ring = (Microsoft.UI.Xaml.Shapes.Ellipse)sender;
        if (ring.OpacityTransition is not null) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => ring.OpacityTransition = new Microsoft.UI.Xaml.ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) });
    }

    // ---- The eyedropper: a magnifier on the photo shows the spot it would pick; a click makes it the accent ----

    /// <summary>The photo's pixels while picking, else null.</summary>
    Oriel.UI.Services.PhotoPixels? _picking;

    /// <summary>The magnifier's pixels: 11 x 11, each drawn 9 px wide.</summary>
    const int Cells = 11, CellSize = 9;
    readonly Microsoft.UI.Xaml.Shapes.Rectangle[] _cells = new Microsoft.UI.Xaml.Shapes.Rectangle[Cells * Cells];

    void BuildLoupe()
    {
        for (var i = 0; i < _cells.Length; i++)
        {
            var cell = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = CellSize, Height = CellSize, Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(),
            };
            Microsoft.UI.Xaml.Controls.Canvas.SetLeft(cell, i % Cells * CellSize);
            Microsoft.UI.Xaml.Controls.Canvas.SetTop(cell, i / Cells * CellSize);
            PickCells.Children.Add(_cells[i] = cell);
        }
    }

    async void Eyedropper_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_picking is not null) { StopPicking(); return; }
        if (ViewModel.AccentPhotoFile is not { } file) return;
        if (await Oriel.UI.Services.PhotoPixels.LoadAsync(file) is not { } pixels) return;
        _picking = pixels;
        // It opens in the middle of the photo at once, so it's plain what the button does; then it follows the pointer.
        PlaceLoupe(new Windows.Foundation.Point(PreviewBox.ActualWidth / 2, PreviewBox.ActualHeight / 2));
        PickRing.Opacity = 1;
        PickRing.Scale = new System.Numerics.Vector3(1, 1, 1);
    }

    void StopPicking()
    {
        if (_picking is null) return;
        _picking = null;
        PickRing.Opacity = 0;
        PickRing.Scale = new System.Numerics.Vector3(0.6f, 0.6f, 1);
        ShowCross(false);
        ViewModel.PreviewCustomAccent(null);
    }

    /// <summary>Where a spot of the box falls on the photo, 0..1: it fills the box (UniformToFill), centered, so some is cut off.</summary>
    (double X, double Y) OnPhoto(Windows.Foundation.Point at, Oriel.UI.Services.PhotoPixels p)
    {
        double w = PreviewBox.ActualWidth, h = PreviewBox.ActualHeight;
        var scale = Math.Max(w / p.Width, h / p.Height);
        return ((at.X - (w - p.Width * scale) / 2) / (p.Width * scale), (at.Y - (h - p.Height * scale) / 2) / (p.Height * scale));
    }

    /// <summary>Centers the magnifier on the spot, fills it with the pixels around it, and shows their color on the custom button.</summary>
    void PlaceLoupe(Windows.Foundation.Point at)
    {
        if (_picking is not { } p) return;
        var (fx, fy) = OnPhoto(at, p);
        var (cx, cy) = p.PixelAt(fx, fy);
        for (var i = 0; i < _cells.Length; i++)
            ((Microsoft.UI.Xaml.Media.SolidColorBrush)_cells[i].Fill).Color = p.Color(cx + i % Cells - Cells / 2, cy + i / Cells - Cells / 2);
        var hex = p.HexAt(fx, fy);
        PickRingColor.Stroke = AccentSwatchViewModel.BrushOf(hex);
        ViewModel.PreviewCustomAccent(hex);
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(PickRing, at.X - PickRing.Width / 2);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(PickRing, at.Y - PickRing.Height / 2);
    }

    /// <summary>
    /// The crosshair over the photo while picking: made once and set only when it changes, since setting a cursor makes
    /// Windows redraw it (set on every move, it flickered).
    /// </summary>
    readonly Microsoft.UI.Input.InputCursor _cross = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Cross);
    bool _crossShown;

    void ShowCross(bool on)
    {
        if (_crossShown == on) return;
        _crossShown = on;
        ProtectedCursor = on ? _cross : null;
    }

    void Preview_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_picking is null) return;
        ShowCross(true);
        PlaceLoupe(e.GetCurrentPoint(PreviewBox).Position);
    }

    /// <summary>The magnifier stays where the pointer left the photo, until a click, Esc or the eyedropper again.</summary>
    void Preview_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => ShowCross(false);

    void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeViewModel.ChangeLabel)) SwapText(ViewModel.ChangeLabel);
        if (e.PropertyName == nameof(HomeViewModel.Preview)) StopPicking(); // its pixels are another photo's now
    }

    const double TextMs = 130;
    string _shownText = "";

    /// <summary>The old words slide up and fade, the new ones rise in from below.</summary>
    void SwapText(string text)
    {
        if (text == _shownText) return;
        _shownText = text;
        var fadeOut = new Storyboard();
        fadeOut.Children.Add(Animate(ChangeText, "Opacity", 0, EasingMode.EaseIn));
        fadeOut.Children.Add(Animate(ChangeTextShift, "Y", -8, EasingMode.EaseIn));
        fadeOut.Completed += (_, _) =>
        {
            if (_shownText != text) return; // a newer word is on its way
            ChangeText.Text = text;
            ChangeTextShift.Y = 8;
            var fadeIn = new Storyboard();
            fadeIn.Children.Add(Animate(ChangeText, "Opacity", 1, EasingMode.EaseOut));
            fadeIn.Children.Add(Animate(ChangeTextShift, "Y", 0, EasingMode.EaseOut));
            fadeIn.Begin();
        };
        fadeOut.Begin();
    }

    static DoubleAnimation Animate(Microsoft.UI.Xaml.DependencyObject target, string property, double to, EasingMode mode)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(TextMs),
            EasingFunction = new CubicEase { EasingMode = mode },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    /// <summary>The Wallpaper of the day card: its photo page, sharp (loading starts on hover), with "Set as wallpaper".</summary>
    void Today_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) =>
        PhotoDetailNav.Open(Frame, e, (Microsoft.UI.Xaml.UIElement)sender, TodayThumb);

    void Today_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        PhotoDetailNav.Prefetch(Frame, (Microsoft.UI.Xaml.UIElement)sender);

    async void Share_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (await ViewModel.CreateShareLinkAsync() is not { } link) return;
        // The system share sheet, with the picture; if it can't open (rare), copy the link instead.
        var image = ViewModel.ShareEntry is { } entry ? await Oriel.UI.Services.ShareImage.FileForAsync(entry) : null;
        if (Oriel.UI.Services.ShareSheet.Show(App.MainWindowHandle, "A wallpaper for you",
                "A wallpaper I like. Open this link to set it on your computer in one click:", new Uri(link), image)) return;
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(link);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        _ = ViewModel.MarkLinkCopiedAsync();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        // Back from the Wallpaper of the day's page: its photo flies back into the card (else it would hang in the
        // air until the animation gave up).
        PhotoDetailNav.Return(e.NavigationMode == NavigationMode.Back ? TodayThumb : null);
        await ViewModel.ActivateAsync();
    }

    long _lastPress;

    /// <summary>
    /// A click within a moment of the last one is a "like": the wallpaper becomes a favorite (never un-favorites) and a
    /// star bursts up. Keep clicking: a star every time, as many as you like.
    /// </summary>
    void Preview_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_picking is { } p)
        {
            var (fx, fy) = OnPhoto(e.GetCurrentPoint(PreviewBox).Position, p);
            var hex = p.HexAt(fx, fy);
            StopPicking();
            _ = ViewModel.PickAccentAsync(hex);
            e.Handled = true;
            return;
        }
        var now = Environment.TickCount64;
        var quick = now - _lastPress < 350;
        _lastPress = now;
        if (!quick || !ViewModel.HasCurrent) return;
        BurstStar(e.GetCurrentPoint(StarLayer).Position);
        if (!ViewModel.IsFavorite) ViewModel.IsFavorite = true;
    }

    /// <summary>
    /// A gold star pops in where you clicked, floats up, keeps growing and fades away. It's laid out at its biggest and
    /// moved by the compositor (not a XAML transform, which draws the glyph once at its starting size and then stretches
    /// that), so it stays sharp all the way.
    /// </summary>
    void BurstStar(Windows.Foundation.Point at)
    {
        const float Size = 144, Rise = 150, Ms = 950;
        var star = new FontIcon
        {
            Glyph = "", FontSize = Size, Width = Size, Height = Size,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["OrielStarBrush"],
        };
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(star, at.X - Size / 2);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(star, at.Y - Size / 2);
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(star, true);
        StarLayer.Children.Add(star);

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(star);
        var compositor = visual.Compositor;
        visual.CenterPoint = new System.Numerics.Vector3(Size / 2, Size / 2, 0);
        visual.Scale = new System.Numerics.Vector3(0.15f, 0.15f, 1);
        var easeOut = compositor.CreateCubicBezierEasingFunction(new(0.215f, 0.61f), new(0.355f, 1f));
        var duration = TimeSpan.FromMilliseconds(Ms);

        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0, new(0.15f, 0.15f, 1));
        scale.InsertKeyFrame(170 / Ms, new(0.625f, 0.625f, 1), easeOut);
        scale.InsertKeyFrame(1, new(1, 1, 1), easeOut);
        scale.Duration = duration;
        var lift = compositor.CreateScalarKeyFrameAnimation();
        lift.InsertKeyFrame(0, 0);
        lift.InsertKeyFrame(110 / Ms, 0);
        lift.InsertKeyFrame(1, -Rise, easeOut);
        lift.Duration = duration;
        var turn = compositor.CreateScalarKeyFrameAnimation();
        turn.InsertKeyFrame(0, Random.Shared.Next(-15, 16));
        turn.InsertKeyFrame(1, 0, easeOut);
        turn.Duration = duration;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 1);
        fade.InsertKeyFrame(0.55f, 1);
        fade.InsertKeyFrame(1, 0, easeOut);
        fade.Duration = duration;

        var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        visual.StartAnimation("Scale", scale);
        visual.StartAnimation("Translation.Y", lift);
        visual.StartAnimation("RotationAngleInDegrees", turn);
        visual.StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() => StarLayer.Children.Remove(star));
    }
}
