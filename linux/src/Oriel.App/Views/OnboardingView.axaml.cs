using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

/// <summary>The first-run tour. Covers the app after the first sign-in, like the sign-in screen does.</summary>
public partial class OnboardingView : UserControl
{
    OnboardingViewModel? _vm;
    int _shownStep;

    public OnboardingView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnChanged;
            _vm = DataContext as OnboardingViewModel;
            if (_vm is null) return;
            _shownStep = _vm.Step;
            _vm.PropertyChanged += OnChanged;
            _ = _vm.LoadImagesAsync();
        };
    }

    void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OnboardingViewModel.Step) || _vm is null) return;
        var forward = _vm.Step > _shownStep;
        _shownStep = _vm.Step;
        // The progress line grows (or shrinks, going back) to the new step instead of jumping (see its transition).
        ProgressFill.RenderTransform = TransformOperations.Parse($"scaleX({_vm.Progress.ToString(CultureInfo.InvariantCulture)})");
        _ = SlideInAsync(_vm.Step, forward);
    }

    /// <summary>
    /// The time picker in the "Once a day" card. Wired here, not bound: a TwoWay binding on a TimePicker can push 00:00
    /// back when it loads. The card can be recreated, so it is wired each time it loads.
    /// </summary>
    void DailyTime_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not TimePicker picker || picker.Tag is true || _vm is null) return;
        picker.Tag = true;
        picker.SelectedTime = _vm.DailyTime;
        picker.SelectedTimeChanged += (_, args) =>
        {
            if (args.NewTime is { } time && _vm is not null) _vm.DailyTime = time;
        };
    }

    /// <summary>The new step fades in sliding from the side it comes from: the right going forward, the left going back.</summary>
    async Task SlideInAsync(int step, bool forward)
    {
        if (this.FindControl<Control>($"Step{step}") is not { } panel) return;
        panel.RenderTransform = new TranslateTransform();
        var from = forward ? 28d : -28d;
        var slide = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(320),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.XProperty, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.XProperty, 0d) } },
            },
        };
        await slide.RunAsync(panel);
    }
}
