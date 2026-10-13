using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Oriel.UI.Controls;

/// <summary>
/// Photos ease in instead of popping. Set <c>controls:ImageReveal.Enabled="True"</c> on an Image: each new
/// picture starts transparent and fades up. One short opacity transition, nothing running afterwards.
/// </summary>
public sealed class ImageReveal : AvaloniaObject
{
    static readonly TimeSpan FadeTime = TimeSpan.FromMilliseconds(450);
    /// <summary>Pictures already revealed once: shown again (back on a page, History <-> Favorites) they just appear.</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Avalonia.Media.IImage, object> Revealed = new();

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ImageReveal, Image, bool>("Enabled");

    public static bool GetEnabled(Image image) => image.GetValue(EnabledProperty);
    public static void SetEnabled(Image image, bool value) => image.SetValue(EnabledProperty, value);

    static ImageReveal()
    {
        Image.SourceProperty.Changed.AddClassHandler<Image>(OnSourceChanged);
    }

    static void OnSourceChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        if (!GetEnabled(image)) return;
        image.Transitions = null;
        if (e.NewValue is null || (e.NewValue is Avalonia.Media.IImage known && Revealed.TryGetValue(known, out _)))
        {
            image.Opacity = 1;
            return;
        }
        if (e.NewValue is Avalonia.Media.IImage shown) Revealed.AddOrUpdate(shown, Revealed);
        image.Opacity = 0;
        Dispatcher.UIThread.Post(() =>
        {
            image.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = FadeTime }];
            image.Opacity = 1;
        }, DispatcherPriority.Background);
    }
}
