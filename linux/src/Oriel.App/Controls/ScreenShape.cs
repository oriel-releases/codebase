using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Oriel.UI.Controls;

/// <summary>
/// Photos drawn in the shape of this computer's screen, so a wallpaper looks as it will on the desktop (like Windows).
/// A frame is a <see cref="ScreenFrame"/>. On a grid of cards: <c>controls:ScreenShape.ItemWidth="280"</c> sets the cards' smallest width and the matching
/// height (use <c>ItemsStretch="Uniform"</c> so they keep that shape as they grow).
/// </summary>
public sealed class ScreenShape : AvaloniaObject
{
    const double FallbackRatio = 16.0 / 9;

    /// <summary>Width over height of the main screen. Set by the main window from its screens, before any page shows.</summary>
    public static double Ratio { get; private set; } = FallbackRatio;

    public static void ReadFrom(TopLevel window)
    {
        if (window.Screens?.Primary?.Bounds is { Width: > 0, Height: > 0 } bounds) Ratio = (double)bounds.Width / bounds.Height;
    }

    public static double HeightFor(double width) => Math.Round(width / Ratio);

    public static readonly AttachedProperty<double> ItemWidthProperty =
        AvaloniaProperty.RegisterAttached<ScreenShape, UniformGridLayout, double>("ItemWidth");

    public static double GetItemWidth(UniformGridLayout layout) => layout.GetValue(ItemWidthProperty);
    public static void SetItemWidth(UniformGridLayout layout, double value) => layout.SetValue(ItemWidthProperty, value);

    static ScreenShape()
    {
        ItemWidthProperty.Changed.AddClassHandler<UniformGridLayout>((layout, e) =>
        {
            if (e.NewValue is not double width || width <= 0) return;
            layout.MinItemWidth = width;
            layout.MinItemHeight = HeightFor(width);
        });
    }
}

/// <summary>
/// A Border in the screen's shape: as tall as the width it gets divided by the screen's ratio, whatever is inside.
/// (Sized by its content instead, a photo would bring its own shape: a grid of cards measures its first card for the
/// row height, and a portrait photo made every card tall.)
/// </summary>
public sealed class ScreenFrame : Border
{
    protected override Type StyleKeyOverride => typeof(Border);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = !double.IsNaN(Width) ? Width : availableSize.Width;
        if (double.IsInfinity(width)) return base.MeasureOverride(availableSize);
        var size = new Size(width, ScreenShape.HeightFor(width));
        base.MeasureOverride(size);
        return size;
    }
}
