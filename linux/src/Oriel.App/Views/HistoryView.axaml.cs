using Avalonia.Controls;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not HistoryViewModel vm) return;
            vm.ConfirmDelete = text => ConfirmDialog.AskAsync(this, "Delete this wallpaper?", text, "Delete", "Cancel");
            // The ink pill slides under the pick (its transition eases it there).
            void Place() => ViewIndicator.RenderTransform =
                Avalonia.Media.Transformation.TransformOperations.Parse(vm.IsGrid ? "translateX(0px)" : "translateX(78px)");
            Place();
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HistoryViewModel.IsGrid)) Place(); };
        };
    }
}
