using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;
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

    void NewTopic_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !ViewModel.AddTopicCommand.CanExecute(null)) return;
        ViewModel.AddTopicCommand.Execute(null);
        e.Handled = true;
    }

    void NewKeyword_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !ViewModel.AddKeywordCommand.CanExecute(null)) return;
        ViewModel.AddKeywordCommand.Execute(null);
        e.Handled = true;
    }
}
