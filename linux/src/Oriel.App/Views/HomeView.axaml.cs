using Avalonia.Controls;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        // Shown: the preview image is worth loading again.
        AttachedToVisualTree += (_, _) => _ = (DataContext as HomeViewModel)?.ActivateAsync();
    }
}
