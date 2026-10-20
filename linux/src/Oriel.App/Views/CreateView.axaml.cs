using Avalonia.Controls;

namespace Oriel.UI.Views;

public partial class CreateView : UserControl
{
    public CreateView()
    {
        InitializeComponent();
        // Coming to Create: new ideas to try, and how many are left today.
        AttachedToVisualTree += (_, _) => (DataContext as Oriel.UI.ViewModels.CreateViewModel)?.ShuffleIdeas();
    }
}
