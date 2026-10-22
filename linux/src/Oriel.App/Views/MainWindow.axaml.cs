using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace Oriel.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (e.Source is not Button button || !button.Classes.Contains("nav")) return;
            // Another page picked in the menu: an open photo goes with it.
            PhotoDetail.CloseNow();
            // Only a new pick plays its icon: clicking the page you're on doesn't. (Click comes before the command runs,
            // so Section is still the page being left.)
            if (button.Content is Panel panel && panel.Children.OfType<PathIcon>().FirstOrDefault() is { } icon &&
                DataContext is ViewModels.MainViewModel vm && vm.Section != button.CommandParameter as string)
            {
                // Only decoration: whatever happens to it, the page still changes (the command runs after this).
                try { PlayIcon(icon, button.CommandParameter as string); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Menu icon animation skipped: {ex.Message}"); }
            }
        }, RoutingStrategies.Bubble);
        // A page picked in the menu fades in, rising softly into place (PageIn).
        PageHost.PropertyChanged += (_, e) => { if (e.Property == ContentControl.ContentProperty) PageIn(); };
        KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Escape || !PhotoDetail.IsOpen) return;
            PhotoDetail.Close();
            e.Handled = true;
        };
    }

    /// <summary>The same curves as Windows and the phone: arrive already moving, settle slowly.</summary>
    static readonly Easing Settle = new SplineEasing(0.05, 0.7, 0.1, 1);
    static readonly Easing Soft = new SplineEasing(0.2, 0, 0, 1);
    int _pageVersion;

    async void PageIn()
    {
        var version = ++_pageVersion;
        PageHost.Transitions = null;
        PageHost.Opacity = 0;
        PageHost.RenderTransform = TransformOperations.Parse("translateY(18px)");
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Render);
        if (version != _pageVersion) return;
        PageHost.Transitions =
        [
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = Soft },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(480), Easing = Settle },
        ];
        PageHost.Opacity = 1;
        PageHost.RenderTransform = TransformOperations.Parse("translateY(0px)");
    }

    readonly Dictionary<PathIcon, CancellationTokenSource> _playing = [];

    /// <summary>
    /// Played when a menu item is clicked, like on Windows: Home hops and pops, Explore spins like a compass finding north,
    /// History winds back a turn.
    /// </summary>
    void PlayIcon(PathIcon icon, string? section)
    {
        // (ms, rotation in degrees, scale, lift in px)
        (int Ms, double Turn, double Scale, double Lift)[] frames = section switch
        {
            // A compass finding north: a little pull back, a full swing round, overshoot, settle.
            "explore" => [(0, 0, 1, 0), (120, -25, 1.12, 0), (520, 385, 1, 0), (680, 350, 1, 0), (820, 362, 1, 0), (920, 360, 1, 0)],
            "history" => [(0, 0, 1, 0), (550, -360, 1, 0)],
            _ => [(0, 0, 1, 0), (140, 0, 1.28, -3), (300, 0, 1, 0)],
        };
        var length = frames[^1].Ms;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(length),
            Easing = section == "history" ? new ExponentialEaseOut() : new CubicEaseInOut(),
        };
        // Avalonia animates a transform's own numbers (lift, turn, scale), not RenderTransform as a whole (that has no
        // animator, and threw on every click): the icon gets one transform of each kind, and each keyframe sets them.
        if (icon.RenderTransform is not TransformGroup { Children.Count: 3 })
            icon.RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new RotateTransform(), new TranslateTransform() } };
        foreach (var (ms, turn, scale, lift) in frames)
        {
            animation.Children.Add(new KeyFrame
            {
                Cue = new Cue((double)ms / length),
                Setters =
                {
                    new Setter(ScaleTransform.ScaleXProperty, scale),
                    new Setter(ScaleTransform.ScaleYProperty, scale),
                    new Setter(RotateTransform.AngleProperty, turn),
                    new Setter(TranslateTransform.YProperty, lift),
                },
            });
        }

        if (_playing.Remove(icon, out var running)) running.Cancel();
        var cts = new CancellationTokenSource();
        _playing[icon] = cts;
        _ = animation.RunAsync(icon, cts.Token);
    }
}
