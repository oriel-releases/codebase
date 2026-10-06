using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Oriel.UI;

/// <summary>
/// Photos ease in instead of popping: they start a little bigger and transparent, then settle to full size.
/// Set <c>local:ImageReveal.Enabled="True"</c> on an Image. It only animates the compositor's opacity and
/// scale (run by the GPU, nothing per frame on the UI thread) and does nothing if Windows animations are off.
/// </summary>
public static class ImageReveal
{
    const float StartScale = 1.1f;
    static readonly TimeSpan FadeTime = TimeSpan.FromMilliseconds(450);
    static readonly TimeSpan ScaleTime = TimeSpan.FromMilliseconds(700);

    static readonly Windows.UI.ViewManagement.UISettings Settings = new();
    /// <summary>Per image: true while a remote photo is still downloading, so the reveal waits for it.</summary>
    sealed class State { public bool WaitingForLoad; }
    static readonly ConditionalWeakTable<Image, State> Hooked = new();

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(ImageReveal),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image || e.NewValue is not true || Hooked.TryGetValue(image, out _)) return;
        var state = new State();
        Hooked.Add(image, state);

        // Scale around the middle, following the image's size, without any layout code.
        var visual = ElementCompositionPreview.GetElementVisual(image);
        var center = visual.Compositor.CreateExpressionAnimation(
            "Vector3(this.Target.Size.X * 0.5f, this.Target.Size.Y * 0.5f, 0f)");
        visual.StartAnimation("CenterPoint", center);

        image.RegisterPropertyChangedCallback(Image.SourceProperty, (s, _) => OnSourceChanged((Image)s, state));
        image.ImageOpened += (s, _) =>
        {
            if (!state.WaitingForLoad) return;
            state.WaitingForLoad = false;
            Play((Image)s);
        };
    }

    static void OnSourceChanged(Image image, State state)
    {
        // A photo still downloading (search results) stays hidden until it opens; a decoded one reveals now.
        state.WaitingForLoad = image.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { PixelWidth: 0 };
        if (state.WaitingForLoad && Settings.AnimationsEnabled)
            ElementCompositionPreview.GetElementVisual(image).Opacity = 0;
        else
        {
            state.WaitingForLoad = false;
            Play(image);
        }
    }

    static void Play(Image image)
    {
        var visual = ElementCompositionPreview.GetElementVisual(image);
        if (image.Source is null || !Settings.AnimationsEnabled)
        {
            visual.Opacity = 1;
            visual.Scale = Vector3.One;
            return;
        }

        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.8f), new Vector2(0.2f, 1f));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, ease);
        fade.Duration = FadeTime;

        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new Vector3(StartScale, StartScale, 1f));
        scale.InsertKeyFrame(1f, Vector3.One, ease);
        scale.Duration = ScaleTime;

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Scale", scale);
    }
}
