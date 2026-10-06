using Avalonia.Controls;
using Avalonia.VisualTree;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

/// <summary>A search result. Its thumbnail is downloaded while it is on screen and dropped when it scrolls away.</summary>
public partial class PhotoResultCard : UserControl
{
    PhotoResultViewModel? _shown;

    public PhotoResultCard()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Show(DataContext as PhotoResultViewModel);
        DetachedFromVisualTree += (_, _) => Show(null);
        DataContextChanged += (_, _) => { if (this.IsAttachedToVisualTree()) Show(DataContext as PhotoResultViewModel); };
    }

    void Show(PhotoResultViewModel? item)
    {
        if (ReferenceEquals(_shown, item)) return;
        _shown?.UnloadThumbnail();
        _shown = item;
        if (item is not null) _ = item.LoadThumbnailAsync();
    }
}
