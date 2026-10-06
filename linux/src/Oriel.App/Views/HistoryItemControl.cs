using Avalonia.Controls;
using Avalonia.VisualTree;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

/// <summary>Base of the History card and row: the thumbnail exists only while the item is on screen.</summary>
public abstract class HistoryItemControl : UserControl
{
    HistoryItemViewModel? _shown;

    protected HistoryItemControl()
    {
        AttachedToVisualTree += (_, _) => Show(DataContext as HistoryItemViewModel);
        DetachedFromVisualTree += (_, _) => Show(null);
        DataContextChanged += (_, _) => { if (this.IsAttachedToVisualTree()) Show(DataContext as HistoryItemViewModel); };
    }

    void Show(HistoryItemViewModel? item)
    {
        if (ReferenceEquals(_shown, item)) return;
        _shown?.UnloadThumbnail();
        _shown = item;
        if (item is not null) _ = item.LoadThumbnailAsync();
    }
}
