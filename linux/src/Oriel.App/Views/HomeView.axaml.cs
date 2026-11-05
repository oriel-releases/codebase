using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
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
        DetachedFromVisualTree += (_, _) => StopPicking();
        // While picking: Esc puts it away, and so does a click anywhere but the photo (or the eyedropper, which stops it itself).
        // The arrows move the spot one pixel of the photo (with Shift ten), Enter picks; first, before the focused button.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (_picking is null) return;
            var fast = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);
            switch (e.Key)
            {
                case Avalonia.Input.Key.Escape: StopPicking(); break;
                case Avalonia.Input.Key.Enter: PickHere(); break;
                case Avalonia.Input.Key.Left: Nudge(-1, 0, fast); break;
                case Avalonia.Input.Key.Right: Nudge(1, 0, fast); break;
                case Avalonia.Input.Key.Up: Nudge(0, -1, fast); break;
                case Avalonia.Input.Key.Down: Nudge(0, 1, fast); break;
                default: return;
            }
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_picking is not null && e.Source is Visual v && !IsWithin(v, PreviewBox) && !IsWithin(v, EyedropperButton))
                StopPicking();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        BuildLoupe();
    }

    static bool IsWithin(Visual v, Visual parent)
    {
        for (Visual? d = v; d is not null; d = d.GetVisualParent()) if (d == parent) return true;
        return false;
    }

    // ---- The eyedropper, like Windows: a ring on the spot, a magnifier floating over it; a click makes it the accent ----

    /// <summary>The photo's pixels while picking, else null.</summary>
    Oriel.UI.Services.PhotoPixels? _picking;

    /// <summary>The magnifier's pixels: 11 x 11, each drawn 10 px wide (110 across: whole pixels, crisp edges).</summary>
    const int Cells = 11, CellSize = 10;
    /// <summary>The gap between the spot and the magnifier floating over it.</summary>
    const double LoupeGap = 22;

    /// <summary>The spot being picked, in the photo box's coordinates.</summary>
    Point _at;

    /// <summary>Moves the spot by one pixel of the photo (ten with Shift), kept on the photo.</summary>
    void Nudge(int dx, int dy, bool fast)
    {
        if (_picking is not { } p) return;
        double w = PreviewBox.Bounds.Width, h = PreviewBox.Bounds.Height;
        var step = Math.Max(w / p.Width, h / p.Height) * (fast ? 10 : 1);
        PlaceLoupe(new Point(Math.Clamp(_at.X + dx * step, 0, w - 0.01), Math.Clamp(_at.Y + dy * step, 0, h - 0.01)));
    }

    /// <summary>The color at the spot becomes the accent (a click on the photo, or Enter).</summary>
    void PickHere()
    {
        if (_picking is not { } p) return;
        var (fx, fy) = OnPhoto(_at, p);
        var hex = p.HexAt(fx, fy);
        StopPicking();
        if (DataContext is HomeViewModel vm) _ = vm.PickAccentAsync(hex);
    }
    readonly Avalonia.Controls.Shapes.Rectangle[] _cells = new Avalonia.Controls.Shapes.Rectangle[Cells * Cells];

    void BuildLoupe()
    {
        for (var i = 0; i < _cells.Length; i++)
        {
            var cell = new Avalonia.Controls.Shapes.Rectangle { Width = CellSize, Height = CellSize, Fill = new SolidColorBrush() };
            Canvas.SetLeft(cell, i % Cells * CellSize);
            Canvas.SetTop(cell, i / Cells * CellSize);
            PickCells.Children.Add(_cells[i] = cell);
        }
    }

    async void Eyedropper_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_picking is not null) { StopPicking(); return; }
        if (DataContext is not HomeViewModel { AccentPhotoFile: { } file }) return;
        if (await Oriel.UI.Services.PhotoPixels.LoadAsync(file) is not { } pixels) return;
        _picking = pixels;
        // It opens in the middle of the photo at once, so it's plain what the button does; then it follows the pointer.
        PlaceLoupe(new Point(PreviewBox.Bounds.Width / 2, PreviewBox.Bounds.Height / 2));
        PickRing.Opacity = 1;
        // The ring is the pointer over the photo.
        PreviewBox.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.None);
    }

    void StopPicking()
    {
        if (_picking is null) return;
        _picking = null;
        PickRing.Opacity = 0;
        PreviewBox.Cursor = null;
        (DataContext as HomeViewModel)?.PreviewCustomAccent(null);
    }

    /// <summary>Where a spot of the box falls on the photo, 0..1: it fills the box (UniformToFill), centered, so some is cut off.</summary>
    (double X, double Y) OnPhoto(Point at, Oriel.UI.Services.PhotoPixels p)
    {
        double w = PreviewBox.Bounds.Width, h = PreviewBox.Bounds.Height;
        var scale = Math.Max(w / p.Width, h / p.Height);
        return ((at.X - (w - p.Width * scale) / 2) / (p.Width * scale), (at.Y - (h - p.Height * scale) / 2) / (p.Height * scale));
    }

    /// <summary>
    /// Moves the ring (the pointer) to the spot, floats the magnifier just above it (below when there's no room above), fills it with the pixels
    /// around the spot, and shows its color on the custom button.
    /// </summary>
    void PlaceLoupe(Point at)
    {
        if (_picking is not { } p) return;
        _at = at;
        var (fx, fy) = OnPhoto(at, p);
        var (cx, cy) = p.PixelAt(fx, fy);
        for (var i = 0; i < _cells.Length; i++)
            ((SolidColorBrush)_cells[i].Fill!).Color = p.Color(cx + i % Cells - Cells / 2, cy + i / Cells - Cells / 2);
        var hex = p.HexAt(fx, fy);
        PickRingColor.Stroke = new SolidColorBrush(Color.Parse(hex));
        (DataContext as HomeViewModel)?.PreviewCustomAccent(hex);
        Canvas.SetLeft(PickSpot, Math.Round(at.X - PickSpot.Width / 2));
        Canvas.SetTop(PickSpot, Math.Round(at.Y - PickSpot.Height / 2));
        var top = at.Y - LoupeGap - PickRing.Height;
        // Whole pixels: placed at fractions, its rings and box drew soft and uneven.
        Canvas.SetLeft(PickRing, Math.Round(at.X - PickRing.Width / 2));
        Canvas.SetTop(PickRing, Math.Round(top >= 0 ? top : at.Y + LoupeGap));
    }

    void Preview_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (_picking is null) return;
        PlaceLoupe(e.GetPosition(PreviewBox));
    }

    HomeViewModel? _watched;

    /// <summary>The button's words follow the view model's ChangeLabel, sliding when they change.</summary>
    void Watch(HomeViewModel? vm)
    {
        if (_watched is not null) _watched.PropertyChanged -= OnViewModelChanged;
        _watched = vm;
        if (vm is null) return;
        vm.PropertyChanged += OnViewModelChanged;
        // Position opens at once: what it shows is loaded as each photo shows.
        _ = App.Get<PositionEditorViewModel>().PrepareAsync();
        _shownText = vm.ChangeLabel;
        ChangeText.Text = vm.ChangeLabel;
    }

    void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeViewModel.ChangeLabel) && _watched is { } vm) _ = SwapTextAsync(vm.ChangeLabel);
        if (e.PropertyName == nameof(HomeViewModel.IsBusy) && _watched is { } busy) SpinChangeIcon(busy.IsBusy);
        if (e.PropertyName == nameof(HomeViewModel.Preview)) _ = App.Get<PositionEditorViewModel>().PrepareAsync();
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

    const double TurnMs = 800;
    CancellationTokenSource? _turning;

    /// <summary>
    /// Change now's arrows turn steadily while the wallpaper changes; when it's done they don't stop dead, they ease out
    /// from the speed they had to rest on a full turn.
    /// </summary>
    void SpinChangeIcon(bool busy)
    {
        if (ChangeIcon.RenderTransform is not RotateTransform rotate) return;
        if (busy)
        {
            if (_turning is not null) return;
            _turning = new CancellationTokenSource();
            _ = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(TurnMs),
                IterationCount = IterationCount.Infinite,
                Children = { Turn(0, 0), Turn(1, 360) },
            }.RunAsync(ChangeIcon, _turning.Token);
            return;
        }
        if (_turning is null) return;
        _turning.Cancel();
        _turning = null;
        var angle = rotate.Angle % 360;
        // At least half a turn more, starting at the speed it was turning (the curve's first slope is 1.5).
        var to = 360 - angle >= 180 ? 360 : 720;
        _ = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(1.5 * (to - angle) * TurnMs / 360),
            Easing = new SplineEasing(0.4, 0.6, 0.4, 1),
            FillMode = FillMode.None,
            Children = { Turn(0, angle), Turn(1, to) },
        }.RunAsync(ChangeIcon).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => rotate.Angle = 0));
    }

    static KeyFrame Turn(double cue, double angle) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(RotateTransform.AngleProperty, angle) },
    };

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
        if (_picking is not null)
        {
            PlaceLoupe(e.GetPosition(PreviewBox));
            PickHere();
            e.Handled = true;
            return;
        }
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

    void Position_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => PositionEditorView.Current?.Open();

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
