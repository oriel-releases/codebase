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

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await ViewModel.LoadAsync();

    // Each item's action buttons stay hidden until the pointer is over it (or it has keyboard focus).
    void Card_Enter(object sender, PointerRoutedEventArgs e) => ShowActions(sender, true);
    void Card_Exit(object sender, PointerRoutedEventArgs e) => ShowActions(sender, false);
    void Card_Focus(object sender, RoutedEventArgs e) => ShowActions(sender, true);
    void Card_Blur(object sender, RoutedEventArgs e) => ShowActions(sender, false);

    static void ShowActions(object card, bool visible)
    {
        var root = (DependencyObject)card;
        if (Find(root, "Actions") is not { } actions) return;
        actions.Opacity = visible ? 1 : 0;
        // The text runs the full width until the buttons appear over its end; then it trims short of them.
        if (Find(root, "Details") is { } details)
            details.Margin = new Thickness(0, 0, visible ? actions.ActualWidth + 8 : 0, 0);
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
