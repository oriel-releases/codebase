using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Oriel.UI.Pages;

public sealed partial class CollectionsPage : Page
{
    public CollectionsViewModel ViewModel { get; } = App.Get<CollectionsViewModel>();

    public CollectionsPage()
    {
        InitializeComponent();
    }

    void Interests_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(InterestsPage), null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });

    void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Content is string words) ViewModel.SearchForCommand.Execute(words);
    }

    /// <summary>Our own ✕ is in the bar; hide the text box's built-in one so there aren't two.</summary>
    void SearchBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (FindByName((DependencyObject)sender, "DeleteButton") is not Button delete) return;
        delete.Width = 0;
        delete.MinWidth = 0;
        delete.Padding = new Thickness(0);
        delete.Opacity = 0;
        delete.IsHitTestVisible = false;
    }

    static DependencyObject? FindByName(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var n } && n == name) return child;
            if (FindByName(child, name) is { } found) return found;
        }
        return null;
    }

    void Search_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !ViewModel.SearchCommand.CanExecute(null)) return;
        ViewModel.SearchCommand.Execute(null);
        e.Handled = true;
    }
}
