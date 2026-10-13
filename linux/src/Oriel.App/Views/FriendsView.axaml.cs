using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Oriel.Core.Services;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

/// <summary>Share your wallpaper, follow a friend, and who follows you.</summary>
public partial class FriendsView : UserControl
{
    const string Message = "Follow my wallpaper on Oriel: whenever I change mine, yours changes too.";

    public FriendsView()
    {
        InitializeComponent();
        // Tidies the code as it's typed or pasted: capitals, no spaces, the dash in place, the caret at the end.
        CodeBox.TextChanged += (_, _) =>
        {
            var tidy = FollowService.FormatInput(CodeBox.Text);
            if (tidy == CodeBox.Text) return;
            CodeBox.Text = tidy;
            CodeBox.CaretIndex = tidy.Length;
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is FriendsViewModel vm)
                vm.Confirm = (title, text, confirm) => ConfirmDialog.AskAsync(this, title, text, confirm, "Cancel");
        };
    }

    FriendsViewModel? Vm => DataContext as FriendsViewModel;

    // ---------------------------------------------------------------- share, like Share on Home

    async void CopyLink_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || await vm.GetShareLinkAsync() is not { } link) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(link);
        await vm.MarkLinkCopiedAsync();
    }

    void ShareTelegram_Click(object? sender, RoutedEventArgs e) =>
        _ = OpenAsync(link => $"https://t.me/share/url?url={Uri.EscapeDataString(link)}&text={Uri.EscapeDataString(Message)}");

    void ShareWhatsApp_Click(object? sender, RoutedEventArgs e) =>
        _ = OpenAsync(link => "https://wa.me/?text=" + Uri.EscapeDataString(Message + " " + link));

    void ShareEmail_Click(object? sender, RoutedEventArgs e) =>
        _ = OpenAsync(link => "mailto:?subject=" + Uri.EscapeDataString("Follow my wallpaper") + "&body=" + Uri.EscapeDataString(Message) + "%0A%0A" + Uri.EscapeDataString(link));

    async Task OpenAsync(Func<string, string> build)
    {
        if (Vm is not { } vm || await vm.GetShareLinkAsync() is not { } link) return;
        Oriel.UI.Services.Launcher.OpenUrl(new Uri(build(link)));
    }

    // ---------------------------------------------------------------- the windows over the page

    void EnterCode_Click(object? sender, RoutedEventArgs e)
    {
        Vm?.StartEnteringCode();
        CodeBox.Focus();
    }

    void Backdrop_PointerPressed(object? sender, PointerPressedEventArgs e) => Vm?.CloseDialogsCommand.Execute(null);

    void RemoveFollower_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && (sender as Control)?.DataContext is FollowerRow row) _ = vm.RemoveAsync(row);
    }
}
