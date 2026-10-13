using Avalonia.Controls;

namespace Oriel.UI.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
        // Coming to Search: new ideas to try.
        AttachedToVisualTree += (_, _) => (DataContext as Oriel.UI.ViewModels.CollectionsViewModel)?.ShuffleSuggestions();
    }
}
