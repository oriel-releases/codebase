using Avalonia.Controls;
using Avalonia.Input.Platform;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

/// <summary>Share your wallpaper with a code, or follow a friend's.</summary>
public partial class FriendsView : UserControl
{
    public FriendsView() => InitializeComponent();

    async void CopyCode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not FriendsViewModel vm) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(vm.MyCode);
        await vm.MarkCopiedAsync();
    }
}
