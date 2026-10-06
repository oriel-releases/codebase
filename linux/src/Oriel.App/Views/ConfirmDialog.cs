using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Oriel.UI.Views;

/// <summary>A small modal question with two buttons. The safe answer is the default.</summary>
public static class ConfirmDialog
{
    /// <returns>True if the person picked the confirming button.</returns>
    public static async Task<bool> AskAsync(Control from, string title, string text, string confirm, string cancel)
    {
        if (TopLevel.GetTopLevel(from) is not Window owner) return false;

        var result = false;
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        dialog.Bind(Window.BackgroundProperty, dialog.GetResourceObservable("OrielPaperBrush"));

        var yes = new Button { Content = confirm, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var no = new Button { Content = cancel, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
        yes.Click += (_, _) => { result = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { no, yes },
                },
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
