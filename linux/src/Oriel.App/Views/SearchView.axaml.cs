using Avalonia.Controls;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
        // Coming to Explore: this hour's photos when the hour has turned.
        AttachedToVisualTree += (_, _) => _ = (DataContext as CollectionsViewModel)?.LoadExploreAsync();
        // Near the bottom of Explore: the next few photos (search results have their own Show more).
        Scroller.ScrollChanged += (_, _) =>
        {
            if (DataContext is CollectionsViewModel { ShowStart: true, IsSearchShown: true } vm &&
                Scroller.Offset.Y >= Scroller.Extent.Height - Scroller.Viewport.Height - 800)
                vm.ShowMoreExplore();
        };
    }
}
