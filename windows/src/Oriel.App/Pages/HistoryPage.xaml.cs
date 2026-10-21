using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Oriel.UI.Pages;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; } = App.Get<HistoryViewModel>();

    public HistoryPage()
    {
        InitializeComponent();
        _ = new Controls.TabHighlight(ViewStrip, ViewIndicator);
        ViewModel.ConfirmDelete = ConfirmDeleteAsync;
    }

    /// <summary>The Favorites menu item opens this same page with the parameter "favorites".</summary>
    public bool FavoritesOnly { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        // Back from a photo's own page: everything as it was, and the photo flies back into its card.
        if (e.NavigationMode == NavigationMode.Back && _opened is not null)
        {
            PhotoDetailNav.Return(_opened);
            return;
        }
        ShowMode(e.Parameter as string == "favorites");
    }

    /// <summary>The card (or list row's thumbnail) last opened.</summary>
    UIElement? _opened;

    /// <summary>A click on a card, not on its buttons: the photo on its own page.</summary>
    /// <summary>The pointer is over a card: its sharp photo starts loading, so its page opens with it.</summary>
    void Card_PointerEntered(object sender, PointerRoutedEventArgs e) => PhotoDetailNav.Prefetch(Frame, (UIElement)sender);

    void Card_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var card = (FrameworkElement)sender;
        var picture = Find(card, "Thumb") ?? card;
        if (PhotoDetailNav.Open(Frame, e, card, picture)) _opened = picture;
    }

    /// <summary>Shows everything (History) or only starred photos (Favorites) on this same page.</summary>
    public async void ShowMode(bool favorites)
    {
        FavoritesOnly = favorites;
        await ViewModel.LoadAsync(favorites);
    }

    static FrameworkElement? Find(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && fe.Name == name) return fe;
            if (Find(child, name) is { } nested) return nested;
        }
        return null;
    }

    async Task<bool> ConfirmDeleteAsync(string text)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete this wallpaper?",
            Content = text,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
