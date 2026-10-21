using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Oriel.UI;

/// <summary>
/// How Oriel moves, the same as on the phone: things arrive already moving and settle slowly (an ease-out curve), so a
/// change feels soft instead of a cut.
/// </summary>
public static class Motion
{
    static readonly Windows.UI.ViewManagement.UISettings Settings = new();

    /// <summary>A page opened from the menu: it fades in while rising the last few pixels into place.</summary>
    public static void PageIn(UIElement page)
    {
        var visual = ElementCompositionPreview.GetElementVisual(page);
        if (!Settings.AnimationsEnabled)
        {
            visual.Opacity = 1;
            return;
        }
        ElementCompositionPreview.SetIsTranslationEnabled(page, true);
        var compositor = visual.Compositor;
        var settle = compositor.CreateCubicBezierEasingFunction(new Vector2(0.05f, 0.7f), new Vector2(0.1f, 1f));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0f), new Vector2(0f, 1f)));
        fade.Duration = TimeSpan.FromMilliseconds(320);

        var rise = compositor.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0f, new Vector3(0, 18, 0));
        rise.InsertKeyFrame(1f, Vector3.Zero, settle);
        rise.Duration = TimeSpan.FromMilliseconds(480);

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", rise);
    }
}
