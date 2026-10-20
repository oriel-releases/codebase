using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Oriel.UI;

/// <summary>
/// Words that change ("Imagining…" → "Painting…", "Changing…" → "Wallpaper set ✓") slide up into place and fade in,
/// like the Android app's labels. Set <c>local:TextSlide.Enabled="True"</c> on a TextBlock. Compositor only (the GPU
/// runs it), and nothing happens if Windows animations are off.
/// </summary>
public static class TextSlide
{
    static readonly TimeSpan Time = TimeSpan.FromMilliseconds(260);
    static readonly Windows.UI.ViewManagement.UISettings Settings = new();

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(TextSlide), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text || e.NewValue is not true) return;
        ElementCompositionPreview.SetIsTranslationEnabled(text, true);
        text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (s, _) => Play((TextBlock)s));
    }

    static void Play(TextBlock text)
    {
        if (!Settings.AnimationsEnabled || !text.IsLoaded) return;
        var visual = ElementCompositionPreview.GetElementVisual(text);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.3f, 1f));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = Time;
        visual.StartAnimation("Opacity", fade);

        var rise = compositor.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0, new Vector3(0, 9, 0));
        rise.InsertKeyFrame(1, Vector3.Zero, ease);
        rise.Duration = Time;
        visual.StartAnimation("Translation", rise);
    }
}
