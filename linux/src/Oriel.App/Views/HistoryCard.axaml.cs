namespace Oriel.UI.Views;

public partial class HistoryCard : HistoryItemControl
{
    public HistoryCard()
    {
        InitializeComponent();
        PhotoDetailView.Wire(this);
    }
}
