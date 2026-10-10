using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Oriel.UI.Pages;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; } = App.Get<HomeViewModel>();

    public HomePage()
    {
        InitializeComponent();
    }

    void Share_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ViewModel.ShareLink is not { } link) return;
        ViewModel.CountShare();
        // The system share sheet; if it can't open (rare), copy the link instead.
        if (Oriel.UI.Services.ShareSheet.Show(App.MainWindowHandle, "A wallpaper for you",
                "A wallpaper I like. Open this link to set it on your computer in one click:", new Uri(link))) return;
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(link);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        _ = ViewModel.MarkLinkCopiedAsync();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await ViewModel.ActivateAsync();
}
