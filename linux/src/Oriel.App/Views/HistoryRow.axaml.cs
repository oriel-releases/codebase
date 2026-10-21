using Avalonia.Controls;

namespace Oriel.UI.Views;

public partial class HistoryRow : HistoryItemControl
{
    public HistoryRow()
    {
        InitializeComponent();
        // A row's thumbnail is what flies to the photo's page.
        PhotoDetailView.Wire(this, () => this.FindControl<Border>("Thumb") ?? (Control)this);
    }
}
