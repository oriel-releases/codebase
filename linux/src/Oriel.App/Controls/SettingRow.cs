using Avalonia;
using Avalonia.Controls;

namespace Oriel.UI.Controls;

/// <summary>
/// One row of a settings group, like the Friends list: an icon tile, a header and description, the control (the content)
/// on the right, and a line above it unless it's the group's <see cref="First"/>. Styled in Theme.axaml.
/// </summary>
public sealed class SettingRow : ContentControl
{
    public static readonly StyledProperty<Avalonia.Media.Geometry?> IconProperty =
        AvaloniaProperty.Register<SettingRow, Avalonia.Media.Geometry?>(nameof(Icon));

    public Avalonia.Media.Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly StyledProperty<bool> FirstProperty = AvaloniaProperty.Register<SettingRow, bool>(nameof(First));

    /// <summary>The group's first row: no line above it.</summary>
    public bool First
    {
        get => GetValue(FirstProperty);
        set => SetValue(FirstProperty, value);
    }

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
