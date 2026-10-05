using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Oriel.UI.Pages;

public sealed partial class CollectionsPage : Page
{
    public CollectionsViewModel ViewModel { get; } = App.Get<CollectionsViewModel>();

    public CollectionsPage()
    {
        InitializeComponent();
    }

    void DailyMix_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(DailyMixPage), null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });

    void Search_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !ViewModel.SearchCommand.CanExecute(null)) return;
        ViewModel.SearchCommand.Execute(null);
        e.Handled = true;
    }
}
