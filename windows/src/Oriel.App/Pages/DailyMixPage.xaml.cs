using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Oriel.UI.Pages;

/// <summary>Sub page of Search: which topics the daily wallpaper is picked from.</summary>
public sealed partial class DailyMixPage : Page
{
    public CollectionsViewModel ViewModel { get; } = App.Get<CollectionsViewModel>();

    public DailyMixPage() => InitializeComponent();

    void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }
}
