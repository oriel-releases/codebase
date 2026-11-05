using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Oriel.UI;

/// <summary>
/// A favorite toggle that pops when it's switched on (grows, overshoots, settles), like the star bursting on Home:
/// <c>local:CheckPop.Enabled="True"</c> on a ToggleButton.
/// </summary>
public static class CheckPop
{
    static readonly Windows.UI.ViewManagement.UISettings Settings = new();

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(CheckPop), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Microsoft.UI.Xaml.Controls.Primitives.ToggleButton toggle || e.NewValue is not true) return;
        // Only a click pops it: a photo that is simply shown as a favorite (a binding) stays still.
        toggle.Click += (_, _) => { if (toggle.IsChecked == true) Pop(toggle); };
    }

    static void Pop(FrameworkElement element)
    {
        if (!Settings.AnimationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var compositor = visual.Compositor;
        var pop = compositor.CreateVector3KeyFrameAnimation();
        pop.InsertKeyFrame(0.3f, new Vector3(1.28f, 1.28f, 1));
        pop.InsertKeyFrame(0.65f, new Vector3(0.92f, 0.92f, 1));
        pop.InsertKeyFrame(1, Vector3.One);
        pop.Duration = TimeSpan.FromMilliseconds(420);
        visual.StartAnimation("Scale", pop);
    }
}
