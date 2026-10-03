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

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await ViewModel.ActivateAsync();
}
