using Microsoft.UI.Xaml;

namespace Oriel.UI;

/// <summary>Visibility helpers for the favorite button: an outline star, or a filled yellow one once checked.</summary>
public static class Star
{
    public static Visibility Shown(bool favorite) => favorite ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Hidden(bool favorite) => favorite ? Visibility.Collapsed : Visibility.Visible;
}
