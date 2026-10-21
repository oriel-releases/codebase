using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Oriel.UI.Controls;

/// <summary>
/// Keeps the highlight behind a strip of RadioButton tabs under the checked one, like the phone's segmented control: it
/// slides there, quick at first and settling softly. The indicator sits in the same grid cell as the items panel, so both
/// share an origin.
/// </summary>
public sealed class TabHighlight
{
    readonly Panel _items;
    readonly Border _indicator;
    readonly TranslateTransform _shift = new();
    readonly Microsoft.UI.Xaml.Media.Animation.Storyboard _slide = new();
    readonly Microsoft.UI.Xaml.Media.Animation.DoubleAnimation _x = new()
    {
        Duration = TimeSpan.FromMilliseconds(320),
        EasingFunction = new Microsoft.UI.Xaml.Media.Animation.ExponentialEase
            { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut, Exponent = 5 },
    };
    bool _placed;

    public TabHighlight(Panel items, Border indicator)
    {
        _items = items;
        _indicator = indicator;
        _indicator.RenderTransform = _shift;
        _indicator.HorizontalAlignment = HorizontalAlignment.Left;
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(_x, _shift);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(_x, "X");
        _slide.Children.Add(_x);

        foreach (var tab in items.Children.OfType<RadioButton>())
            tab.Checked += (_, _) => MoveToChecked(animate: true);
        items.Loaded += (_, _) => MoveToChecked(animate: false);
        items.SizeChanged += (_, _) => MoveToChecked(animate: false);
    }

    void MoveToChecked(bool animate)
    {
        var tab = _items.Children.OfType<RadioButton>().FirstOrDefault(t => t.IsChecked == true);
        if (tab is null || tab.ActualWidth <= 0) return;

        var x = tab.TransformToVisual(_items).TransformPoint(new Point(0, 0)).X;
        _indicator.Width = tab.ActualWidth;
        if (!animate || !_placed)
        {
            _slide.Stop();
            _shift.X = x;
            _placed = true;
            return;
        }
        _x.From = _shift.X;
        _x.To = x;
        _slide.Begin();
    }
}
