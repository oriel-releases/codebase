using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Oriel.UI.Controls;

/// <summary>
/// Keeps the highlight behind a strip of RadioButton tabs under the checked one (no animation).
/// The indicator sits in the same grid cell as the items panel, so both share an origin.
/// </summary>
public sealed class TabHighlight
{
    readonly Panel _items;
    readonly Border _indicator;
    readonly TranslateTransform _shift = new();

    public TabHighlight(Panel items, Border indicator)
    {
        _items = items;
        _indicator = indicator;
        _indicator.RenderTransform = _shift;
        _indicator.HorizontalAlignment = HorizontalAlignment.Left;

        foreach (var tab in items.Children.OfType<RadioButton>())
            tab.Checked += (_, _) => MoveToChecked();
        items.Loaded += (_, _) => MoveToChecked();
        items.SizeChanged += (_, _) => MoveToChecked();
    }

    void MoveToChecked()
    {
        var tab = _items.Children.OfType<RadioButton>().FirstOrDefault(t => t.IsChecked == true);
        if (tab is null || tab.ActualWidth <= 0) return;

        _shift.X = tab.TransformToVisual(_items).TransformPoint(new Point(0, 0)).X;
        _indicator.Width = tab.ActualWidth;
    }
}
