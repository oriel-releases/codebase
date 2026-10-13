using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Oriel.UI.Pages;

/// <summary>Sub page of Search: which topics the daily wallpaper is picked from.</summary>
public sealed partial class InterestsPage : Page
{
    public CollectionsViewModel ViewModel { get; } = App.Get<CollectionsViewModel>();

    public InterestsPage()
    {
        InitializeComponent();
        // Your own interests without a photo get one (once each).
        Loaded += (_, _) => _ = ViewModel.FindCoversAsync();
    }

    void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    /// <summary>Hovering a tile zooms its photo a little, around the middle.</summary>
    void Tile_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => Zoom(sender, 1.06f);

    void Tile_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => Zoom(sender, 1f);

    static void Zoom(object tile, float scale)
    {
        if ((tile as Button)?.Content is not Grid { Children: [Grid photo, ..] }) return;
        photo.CenterPoint = new System.Numerics.Vector3((float)photo.ActualWidth / 2, (float)photo.ActualHeight / 2, 0);
        photo.Scale = new System.Numerics.Vector3(scale, scale, 1);
    }

}
