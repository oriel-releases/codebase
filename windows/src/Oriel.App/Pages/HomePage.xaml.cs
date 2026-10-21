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
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelChanged;
    }

    void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeViewModel.ChangeLabel)) SwapText(ViewModel.ChangeLabel);
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
        // The system share sheet; if it can't open (rare), copy the link instead.
        if (Oriel.UI.Services.ShareSheet.Show(App.MainWindowHandle, "A wallpaper for you",
                "A wallpaper I like. Open this link to set it on your computer in one click:", new Uri(link))) return;
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
        var now = Environment.TickCount64;
        var quick = now - _lastPress < 350;
        _lastPress = now;
        if (!quick || !ViewModel.HasCurrent) return;
        BurstStar(e.GetCurrentPoint(StarLayer).Position);
        if (!ViewModel.IsFavorite) ViewModel.IsFavorite = true;
    }

    /// <summary>A gold star pops in where you clicked, floats up, keeps growing and fades away.</summary>
    void BurstStar(Windows.Foundation.Point at)
    {
        const double Size = 72, Rise = 150, Ms = 950;
        var star = new FontIcon
        {
            Glyph = "\uE735", FontSize = Size, Width = Size, Height = Size,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["OrielStarBrush"],
        };
        var rotate = new Microsoft.UI.Xaml.Media.RotateTransform { CenterX = Size / 2, CenterY = Size / 2, Angle = Random.Shared.Next(-15, 16) };
        var scale = new Microsoft.UI.Xaml.Media.ScaleTransform { CenterX = Size / 2, CenterY = Size / 2, ScaleX = 0.3, ScaleY = 0.3 };
        var move = new Microsoft.UI.Xaml.Media.TranslateTransform();
        star.RenderTransform = new Microsoft.UI.Xaml.Media.TransformGroup { Children = { rotate, scale, move } };
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(star, at.X - Size / 2);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(star, at.Y - Size / 2);
        StarLayer.Children.Add(star);

        var story = new Storyboard();
        story.Children.Add(Frames(scale, "ScaleX", (0, 0.3), (170, 1.25), (Ms, 2.0)));
        story.Children.Add(Frames(scale, "ScaleY", (0, 0.3), (170, 1.25), (Ms, 2.0)));
        story.Children.Add(Frames(move, "Y", (0, 0), (110, 0), (Ms, -Rise)));
        story.Children.Add(Frames(rotate, "Angle", (0, rotate.Angle), (Ms, 0)));
        story.Children.Add(Frames(star, "Opacity", (0, 1), (Ms * 0.55, 1), (Ms, 0)));
        story.Completed += (_, _) => StarLayer.Children.Remove(star);
        story.Begin();
    }

    static DoubleAnimationUsingKeyFrames Frames(Microsoft.UI.Xaml.DependencyObject target, string property, params (double Ms, double Value)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames { EnableDependentAnimation = true };
        foreach (var (ms, value) in frames)
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(ms), Value = value,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }
}
