using Oriel.Core.Services;
using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Oriel.UI.Pages;

/// <summary>Share your wallpaper, follow a friend, and who follows you.</summary>
public sealed partial class FriendsPage : Page
{
    public FriendsViewModel ViewModel { get; } = App.Get<FriendsViewModel>();

    public FriendsPage()
    {
        InitializeComponent();
        ViewModel.Confirm = ConfirmAsync;
        Unloaded += (_, _) => ViewModel.Detach();
    }

    /// <summary>Like Share on Home: the system share sheet with the follow link, or the link copied when it can't open.</summary>
    async void Share_Click(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.GetShareLinkAsync() is not { } link) return;
        if (Oriel.UI.Services.ShareSheet.Show(App.MainWindowHandle, "Follow my wallpaper",
                "Follow my wallpaper on Oriel: whenever I change mine, yours changes too.", new Uri(link))) return;
        var package = new DataPackage();
        package.SetText(link);
        Clipboard.SetContent(package);
        _ = ViewModel.MarkLinkCopiedAsync();
    }

    async void EnterCode_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.StartEnteringCode();
        CodeDialog.XamlRoot = XamlRoot;
        await CodeDialog.ShowAsync();
    }

    /// <summary>Follow: the window stays open (showing why) until the code works.</summary>
    async void CodeDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        args.Cancel = !await ViewModel.FollowAsync();
        deferral.Complete();
    }

    async void Followers_Click(object sender, RoutedEventArgs e)
    {
        FollowersDialog.XamlRoot = XamlRoot;
        _ = ViewModel.LoadFollowersAsync();
        await FollowersDialog.ShowAsync();
    }

    void RemoveFollower_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FollowerRow row) _ = ViewModel.RemoveAsync(row);
    }

    /// <summary>Tidies the code as it's typed or pasted: capitals, no spaces, the dash in place, the caret at the end.</summary>
    void Code_TextChanging(TextBox box, TextBoxTextChangingEventArgs e)
    {
        var tidy = FollowService.FormatInput(box.Text);
        if (tidy == box.Text) return;
        box.Text = tidy;
        box.SelectionStart = tidy.Length;
    }

    void Message_Closed(InfoBar sender, InfoBarClosedEventArgs args) => ViewModel.Message = "";

    async Task<bool> ConfirmAsync(string title, string text, string confirm)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = text,
            PrimaryButtonText = confirm,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
