using Oriel.Core.Auth;
using Oriel.UI.Pages;
using Oriel.UI.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace Oriel.UI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Mica is drawn by Windows from the wallpaper: a faint blurred tint, no images or blur work of our own.
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = 560;
            p.PreferredMinimumHeight = 480;
        }
        ResizeAndCenter(1100, 780);

        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
                _ = App.Get<HomeViewModel>().RefreshAsync();
        };
        // The pane opens itself on first layout; start it shrunk to icons.
        Nav.Loaded += (_, _) => Nav.IsPaneOpen = false;
        ContentFrame.Navigate(typeof(HomePage));

        var auth = App.Get<AuthService>();
        void OnAuthChanged(object? s, EventArgs e) => App.Dispatcher.TryEnqueue(UpdateSignInGate);
        auth.Changed += OnAuthChanged;
        void OnSigningOut(object? s, EventArgs e) => App.Dispatcher.TryEnqueue(() =>
        {
            SigningOut.Visibility = auth.IsSigningOut ? Visibility.Visible : Visibility.Collapsed;
            SigningOutRing.IsActive = auth.IsSigningOut;
        });
        auth.SigningOutChanged += OnSigningOut;
        Onboarding.ViewModel.Finished += OnOnboardingFinished;
        Shared.ViewModel.Changed += OnSharedChanged;
        Closed += (_, _) =>
        {
            auth.Changed -= OnAuthChanged;
            auth.SigningOutChanged -= OnSigningOut;
            Onboarding.ViewModel.Finished -= OnOnboardingFinished;
            Shared.ViewModel.Changed -= OnSharedChanged;
        };
        UpdateSignInGate();
    }

    void OnSharedChanged(object? sender, EventArgs e) => App.Dispatcher.TryEnqueue(UpdateSignInGate);

    void OnOnboardingFinished(object? sender, EventArgs e) => App.Dispatcher.TryEnqueue(UpdateSignInGate);

    /// <summary>Signed out: only the sign-in screen is reachable. The first time in, the tour comes before the app.</summary>
    void UpdateSignInGate()
    {
        var signedIn = App.Get<AuthService>().IsSignedIn;
        var touring = signedIn && Onboarding.ViewModel.ShouldShow();
        SignIn.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        Onboarding.Visibility = touring ? Visibility.Visible : Visibility.Collapsed;
        Nav.Visibility = signedIn && !touring ? Visibility.Visible : Visibility.Collapsed;
        // A shared photo waits behind sign-in and the tour, then shows over the app.
        Shared.Visibility = signedIn && !touring && Shared.ViewModel.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        if (signedIn && !touring) _ = App.Get<HomeViewModel>().RefreshAsync();
    }

    void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e) =>
        // Fires while InitializeComponent applies IsSelected, before the frame exists.
        Show(e.IsSettingsSelected, e.SelectedItemContainer?.Tag as string);

    /// <summary>Clicking the current item again leaves a sub page (e.g. Interests) and returns to its main page.</summary>
    void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer is NavigationViewItem item) PlayIcon(item);
        Show(e.IsSettingsInvoked, e.InvokedItemContainer?.Tag as string);
    }

    readonly Dictionary<NavigationViewItem, Storyboard> _iconAnimations = [];

    /// <summary>Played when a tab is clicked. Home hops and pops, Search wiggles like a magnifier, History winds back a turn.</summary>
    void PlayIcon(NavigationViewItem item)
    {
        if (item.Icon is not FontIcon icon) return;
        if (icon.RenderTransform is not CompositeTransform t)
            icon.RenderTransform = t = new CompositeTransform();
        icon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

        if (_iconAnimations.TryGetValue(item, out var running)) running.Stop();
        var story = new Storyboard();
        if (item.Tag as string == "history")
        {
            story.Children.Add(Animate(t, "Rotation", [(0, 0), (550, -360)], new CubicEase { EasingMode = EasingMode.EaseOut }));
        }
        else if (item.Tag as string == "collections")
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            story.Children.Add(Animate(t, "Rotation", [(0, 0), (110, -18), (260, 14), (400, 0)], ease));
            story.Children.Add(Animate(t, "ScaleX", [(0, 1), (150, 1.2), (400, 1)], ease));
            story.Children.Add(Animate(t, "ScaleY", [(0, 1), (150, 1.2), (400, 1)], ease));
        }
        else
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            story.Children.Add(Animate(t, "ScaleX", [(0, 1), (140, 1.28), (300, 1)], ease));
            story.Children.Add(Animate(t, "ScaleY", [(0, 1), (140, 1.28), (300, 1)], ease));
            story.Children.Add(Animate(t, "TranslateY", [(0, 0), (140, -3), (300, 0)], ease));
        }
        _iconAnimations[item] = story;
        story.Begin();
    }

    static DoubleAnimationUsingKeyFrames Animate(DependencyObject target, string property,
        (int Ms, double Value)[] frames, EasingFunctionBase ease)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        foreach (var (ms, value) in frames)
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(ms),
                Value = value,
                EasingFunction = ease,
            });
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    /// <summary>Opens the Friends page (after a friend's follow link).</summary>
    public void ShowFriends()
    {
        if (Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag as string == "friends") is { } item)
            Nav.SelectedItem = item;
    }

    void Show(bool settings, string? tag)
    {
        if (ContentFrame is null) return;
        var page = settings
            ? typeof(SettingsPage)
            : tag switch
            {
                "collections" => typeof(CollectionsPage),
                "history" or "favorites" => typeof(HistoryPage),
                "friends" => typeof(FriendsPage),
                _ => typeof(HomePage),
            };
        // History and Favorites are one page. Switching between them reloads that page instead of navigating:
        // two History pages alive at once would fight over the shared view-mode radio buttons.
        var favorites = !settings && tag == "favorites";
        if (page == typeof(HistoryPage) && ContentFrame.Content is HistoryPage current)
        {
            if (current.FavoritesOnly != favorites) current.ShowMode(favorites);
            return;
        }
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, favorites ? "favorites" : null, new EntranceNavigationTransitionInfo());
    }

    void ResizeAndCenter(int width, int height)
    {
        var scale = (Content?.XamlRoot?.RasterizationScale) ?? GetDpiScale();
        var w = (int)(width * scale);
        var h = (int)(height * scale);
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        w = Math.Min(w, area.Width);
        h = Math.Min(h, area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
    }

    double GetDpiScale() => GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetDpiForWindow(nint hwnd);
}
