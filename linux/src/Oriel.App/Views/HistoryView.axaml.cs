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
            if (DataContext is HistoryViewModel vm) vm.ConfirmDelete = text => ConfirmDialog.AskAsync(this, "Delete this wallpaper?", text, "Delete", "Cancel");
        };
    }
}
