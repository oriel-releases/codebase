using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Oriel.UI.Controls;

/// <summary>The first-run tour. Covers the app after the first sign-in, like the sign-in screen does.</summary>
public sealed partial class OnboardingView : UserControl
{
    public OnboardingViewModel ViewModel { get; } = App.Get<OnboardingViewModel>();

    int _shownStep;

    public OnboardingView()
    {
        InitializeComponent();
        _shownStep = ViewModel.Step;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(OnboardingViewModel.Step)) return;
            var forward = ViewModel.Step > _shownStep;
            _shownStep = ViewModel.Step;
            AnimateProgress(ViewModel.Progress);
            SlideIn(ViewModel.Step, forward);
        };
    }

    /// <summary>Hovering an interest tile zooms its photo a little, around the middle.</summary>
    void Tile_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => Zoom(sender, 1.06f);

    void Tile_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => Zoom(sender, 1f);

    static void Zoom(object tile, float scale)
    {
        if ((tile as Button)?.Content is not Grid { Children: [Grid photo, ..] }) return;
        photo.CenterPoint = new System.Numerics.Vector3((float)photo.ActualWidth / 2, (float)photo.ActualHeight / 2, 0);
        photo.Scale = new System.Numerics.Vector3(scale, scale, 1);
    }

    /// <summary>The column is as wide as the window allows, up to 640, and centered with equal space on both sides.</summary>
    void Scroller_SizeChanged(object sender, SizeChangedEventArgs e) =>
        Column.Width = Math.Max(0, Math.Min(640, e.NewSize.Width - 64));

    /// <summary>
    /// The time picker in the "Once a day" card. Wired here, not bound: a TwoWay binding on TimePicker pushes 00:00 back
    /// when it loads. The card can be recreated, so it is wired each time it loads.
    /// </summary>
    void DailyTime_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TimePicker picker || picker.Tag is true) return;
        picker.Tag = true;
        picker.SelectedTime = ViewModel.DailyTime;
        picker.SelectedTimeChanged += (_, args) =>
        {
            if (args.NewTime is { } time) ViewModel.DailyTime = time;
        };
    }

    /// <summary>The progress line grows (or shrinks, going back) to the new step instead of jumping.</summary>
    void AnimateProgress(double to)
    {
        var grow = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(450),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(grow, ProgressFill);
        Storyboard.SetTargetProperty(grow, "ScaleX");
        var story = new Storyboard();
        story.Children.Add(grow);
        story.Begin();
    }

    /// <summary>The new step fades in sliding from the side it comes from: the right going forward, the left going back.</summary>
    void SlideIn(int step, bool forward)
    {
        if (FindName($"Step{step}") is not FrameworkElement panel) return;
        var shift = new TranslateTransform { X = forward ? 28 : -28 };
        panel.RenderTransform = shift;
        panel.Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = ease };
        Storyboard.SetTarget(slide, shift);
        Storyboard.SetTargetProperty(slide, "X");
        var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(260), EasingFunction = ease };
        Storyboard.SetTarget(fade, panel);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var story = new Storyboard();
        story.Children.Add(slide);
        story.Children.Add(fade);
        story.Begin();
    }
}
