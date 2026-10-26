using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Input.Platform;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        // The Wallpaper of the day card opens its photo page (sharp, loading on hover), like Explore's cards.
        PhotoDetailView.Wire(TodayCard, () => TodayThumb);
        // Shown: the preview image is worth loading again.
        AttachedToVisualTree += (_, _) => _ = (DataContext as HomeViewModel)?.ActivateAsync();
        DataContextChanged += (_, _) => Watch(DataContext as HomeViewModel);
    }

    HomeViewModel? _watched;

    /// <summary>The button's words follow the view model's ChangeLabel, sliding when they change.</summary>
    void Watch(HomeViewModel? vm)
    {
        if (_watched is not null) _watched.PropertyChanged -= OnViewModelChanged;
        _watched = vm;
        if (vm is null) return;
        vm.PropertyChanged += OnViewModelChanged;
        _shownText = vm.ChangeLabel;
        ChangeText.Text = vm.ChangeLabel;
    }

    void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeViewModel.ChangeLabel) && _watched is { } vm) _ = SwapTextAsync(vm.ChangeLabel);
    }

    static readonly TimeSpan TextTime = TimeSpan.FromMilliseconds(130);
    string _shownText = "";

    /// <summary>The old words slide up and fade, the new ones rise in from below.</summary>
    async Task SwapTextAsync(string text)
    {
        if (text == _shownText) return;
        _shownText = text;
        await Slide(0, -8, 1, 0, new CubicEaseIn()).RunAsync(ChangeText);
        if (_shownText != text) return; // a newer word is on its way
        ChangeText.Text = text;
        await Slide(8, 0, 0, 1, new CubicEaseOut()).RunAsync(ChangeText);
    }

    static Animation Slide(double fromY, double toY, double fromOpacity, double toOpacity, Easing easing) => new()
    {
        Duration = TextTime,
        Easing = easing,
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.YProperty, fromY), new Setter(OpacityProperty, fromOpacity) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.YProperty, toY), new Setter(OpacityProperty, toOpacity) } },
        },
    };

    long _lastPress;

    /// <summary>
    /// A click within a moment of the last one is a "like": the wallpaper becomes a favorite (never un-favorites) and a
    /// star bursts up. Keep clicking: a star every time, as many as you like.
    /// </summary>
    void Preview_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var now = Environment.TickCount64;
        var quick = now - _lastPress < 350;
        _lastPress = now;
        if (!quick || DataContext is not HomeViewModel { HasCurrent: true } vm) return;
        BurstStar(e.GetPosition(StarLayer));
        if (!vm.IsFavorite) vm.IsFavorite = true;
    }

    // The star's outline on a 24-unit grid (the same shape as Android's favorite star).
    static readonly Geometry StarShape = Geometry.Parse("M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z");

    /// <summary>A gold star pops in where you clicked, floats up, keeps growing and fades away (one frame at a time).</summary>
    void BurstStar(Point at)
    {
        const double Size = 72, Rise = 150, Ms = 950;
        var star = new Avalonia.Controls.Shapes.Path
        {
            Data = StarShape, Fill = new SolidColorBrush(Color.Parse("#F5B301")), Stretch = Stretch.Uniform,
            Width = Size, Height = Size, RenderTransformOrigin = RelativePoint.Center,
        };
        Canvas.SetLeft(star, at.X - Size / 2);
        Canvas.SetTop(star, at.Y - Size / 2);
        StarLayer.Children.Add(star);
        var tilt = Random.Shared.Next(-15, 16);
        var top = TopLevel.GetTopLevel(this);
        TimeSpan? start = null;
        void Frame(TimeSpan now)
        {
            start ??= now;
            var t = Math.Min(1, (now - start.Value).TotalMilliseconds / Ms);
            var grow = t < 0.18 ? 0.3 + 0.95 * (t / 0.18) : 1.25 + 0.75 * ((t - 0.18) / 0.82);
            var lift = t < 0.12 ? 0 : Rise * (1 - Math.Pow(1 - (t - 0.12) / 0.88, 3));
            star.RenderTransform = new TransformGroup
            {
                Children = { new RotateTransform(tilt * (1 - t)), new ScaleTransform(grow, grow), new TranslateTransform(0, -lift) },
            };
            star.Opacity = t < 0.55 ? 1 : 1 - (t - 0.55) / 0.45;
            if (t < 1 && top is not null) top.RequestAnimationFrame(Frame);
            else StarLayer.Children.Remove(star);
        }
        if (top is null) StarLayer.Children.Remove(star);
        else top.RequestAnimationFrame(Frame);
    }

    const string Message = "A wallpaper I like. Open this link to set it on your computer in one click:";

    /// <summary>The picture itself on the clipboard (Telegram, WhatsApp and most apps paste it as a photo).</summary>
    async void CopyImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel { ShareEntry: { } entry } vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if (await Oriel.UI.Services.ShareImage.FileForAsync(entry) is not { } file) return;
        var link = await vm.CreateShareLinkAsync();
        await CopyImageAsync(clipboard, file, link is null ? null : $"{Message} {link}");
        await vm.MarkLinkCopiedAsync();
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

    async void CopyLink_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm || await vm.CreateShareLinkAsync() is not { } link) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(link);
        await vm.MarkLinkCopiedAsync();
    }

    void ShareTelegram_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _ = OpenAsync(link => $"https://t.me/share/url?url={Uri.EscapeDataString(link)}&text={Uri.EscapeDataString(Message)}");

    void ShareWhatsApp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _ = OpenAsync(link => "https://wa.me/?text=" + Uri.EscapeDataString(Message + " " + link));

    void ShareEmail_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _ = OpenAsync(link => "mailto:?subject=" + Uri.EscapeDataString("A wallpaper for you") + "&body=" + Uri.EscapeDataString(Message) + "%0A%0A" + Uri.EscapeDataString(link));

    async Task OpenAsync(Func<string, string> build)
    {
        if (DataContext is not HomeViewModel vm || await vm.CreateShareLinkAsync() is not { } link) return;
        Oriel.UI.Services.Launcher.OpenUrl(new Uri(build(link)));
    }
}
