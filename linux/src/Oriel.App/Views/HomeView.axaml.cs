using Avalonia.Controls;
using Avalonia.Input.Platform;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        // Shown: the preview image is worth loading again.
        AttachedToVisualTree += (_, _) => _ = (DataContext as HomeViewModel)?.ActivateAsync();
    }

    const string Message = "A wallpaper I like. Open this link to set it on your computer in one click:";

    async void CopyLink_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel { ShareLink: { } link } vm) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(link);
        vm.CountShare();
        await vm.MarkLinkCopiedAsync();
    }

    void ShareTelegram_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Open(link => $"https://t.me/share/url?url={Uri.EscapeDataString(link)}&text={Uri.EscapeDataString(Message)}");

    void ShareWhatsApp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Open(link => "https://wa.me/?text=" + Uri.EscapeDataString(Message + " " + link));

    void ShareEmail_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Open(link => "mailto:?subject=" + Uri.EscapeDataString("A wallpaper for you") + "&body=" + Uri.EscapeDataString(Message) + "%0A%0A" + Uri.EscapeDataString(link));

    void Open(Func<string, string> build)
    {
        if (DataContext is not HomeViewModel { ShareLink: { } link } vm) return;
        Oriel.UI.Services.Launcher.OpenUrl(new Uri(build(link)));
        vm.CountShare();
    }
}
