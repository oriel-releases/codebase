using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Oriel.UI.Pages;

/// <summary>Share your wallpaper with a code, or follow a friend's.</summary>
public sealed partial class FriendsPage : Page
{
    public FriendsViewModel ViewModel { get; } = App.Get<FriendsViewModel>();

    public FriendsPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Detach();
    }

    void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(ViewModel.MyCode);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        _ = ViewModel.MarkCopiedAsync();
    }

    void Code_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !ViewModel.FollowCommand.CanExecute(null)) return;
        ViewModel.FollowCommand.Execute(null);
        e.Handled = true;
    }
}
