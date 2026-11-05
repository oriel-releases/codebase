using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Oriel.UI;

/// <summary>
/// Photos drawn in the shape of this computer's screen, so a wallpaper looks as it will on the desktop.
/// A frame is a <see cref="ScreenFrame"/>. A grid of them: <c>local:ScreenShape.ItemWidth="280"</c> on its
/// UniformGridLayout sets the cards' smallest width and the matching height (with <c>ItemsStretch="Uniform"</c> they
/// keep that shape as they grow).
/// </summary>
public static class ScreenShape
{
    const double FallbackRatio = 16.0 / 9;

    /// <summary>Width over height of the main screen, read once.</summary>
    public static double Ratio => _ratio ??= ReadRatio();
    static double? _ratio;

    static double ReadRatio()
    {
        var bounds = DisplayArea.Primary?.OuterBounds;
        return bounds is { Width: > 0, Height: > 0 } b ? (double)b.Width / b.Height : FallbackRatio;
    }

    public static double HeightFor(double width) => Math.Round(width / Ratio);

    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.RegisterAttached("ItemWidth", typeof(double), typeof(ScreenShape), new PropertyMetadata(0.0, OnItemWidthChanged));

    public static double GetItemWidth(DependencyObject d) => (double)d.GetValue(ItemWidthProperty);
    public static void SetItemWidth(DependencyObject d, double value) => d.SetValue(ItemWidthProperty, value);

    static void OnItemWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UniformGridLayout layout || e.NewValue is not double width || width <= 0) return;
        layout.MinItemWidth = width;
        layout.MinItemHeight = HeightFor(width);
    }
}

/// <summary>
/// A Grid in the screen's shape: as tall as the width it gets divided by the screen's ratio, whatever is inside.
/// (Sized by its content instead, a photo would bring its own shape: a grid of cards measures its first card for the
/// row height, and a portrait photo made every card tall.)
/// </summary>
public sealed partial class ScreenFrame : Grid
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = !double.IsNaN(Width) ? Width : availableSize.Width;
        if (double.IsInfinity(width)) return base.MeasureOverride(availableSize);
        var size = new Size(width, ScreenShape.HeightFor(width));
        base.MeasureOverride(size);
        return size;
    }
}
