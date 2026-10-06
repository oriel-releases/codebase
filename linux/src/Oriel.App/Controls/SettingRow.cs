using Avalonia;
using Avalonia.Controls;

namespace Oriel.UI.Controls;

/// <summary>A Settings row: a header and description on the left, the control (the content) on the right. Styled in Theme.axaml.</summary>
public sealed class SettingRow : ContentControl
{
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<SettingRow, object?>(nameof(Header));

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
