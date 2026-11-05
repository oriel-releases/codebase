using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace Oriel.UI.Controls;

/// <summary>
/// A favorite toggle that pops when it's switched on (grows, overshoots, settles), like the star bursting on Home:
/// <c>controls:CheckPop.Enabled="True"</c> on a ToggleButton. Only a click pops it, not a favorite simply being shown.
/// </summary>
public static class CheckPop
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ToggleButton, bool>("Enabled", typeof(CheckPop));

    public static bool GetEnabled(ToggleButton toggle) => toggle.GetValue(EnabledProperty);
    public static void SetEnabled(ToggleButton toggle, bool value) => toggle.SetValue(EnabledProperty, value);

    static CheckPop()
    {
        EnabledProperty.Changed.AddClassHandler<ToggleButton>((toggle, e) =>
        {
            if (e.NewValue is true) toggle.Click += (_, _) => { if (toggle.IsChecked == true) _ = Pop(toggle); };
        });
    }

    static Task Pop(ToggleButton toggle)
    {
        toggle.RenderTransformOrigin = RelativePoint.Center;
        return new Animation
        {
            Duration = TimeSpan.FromMilliseconds(420),
            Children =
            {
                Frame(0, 1), Frame(0.3, 1.28), Frame(0.65, 0.92), Frame(1, 1),
            },
        }.RunAsync(toggle);
    }

    static KeyFrame Frame(double cue, double scale) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(Visual.RenderTransformProperty, TransformOperations.Parse($"scale({scale.ToString(System.Globalization.CultureInfo.InvariantCulture)})")) },
    };
}
