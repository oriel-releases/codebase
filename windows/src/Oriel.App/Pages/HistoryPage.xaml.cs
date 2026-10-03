using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Oriel.UI.Pages;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; } = App.Get<HistoryViewModel>();

    public HistoryPage()
    {
        InitializeComponent();
        ViewModel.ConfirmDelete = ConfirmDeleteAsync;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await ViewModel.LoadAsync();

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
