using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Oriel.UI.Controls;

/// <summary>
/// One row of a settings group: icon tile, title, optional description, and a control on the right (the XAML
/// child), with a line above it unless it's the group's <see cref="First"/>. The text takes only the space the control leaves and wraps, so a row never grows
/// past the page.
/// </summary>
[ContentProperty(Name = nameof(Action))]
public sealed partial class SettingRow : UserControl
{
    public SettingRow() => InitializeComponent();

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingRow), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata(""));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingRow), new PropertyMetadata(""));

    public static readonly DependencyProperty FirstProperty =
        DependencyProperty.Register(nameof(First), typeof(bool), typeof(SettingRow), new PropertyMetadata(false));

    /// <summary>The group's first row: no line above it.</summary>
    public bool First
    {
        get => (bool)GetValue(FirstProperty);
        set => SetValue(FirstProperty, value);
    }

    public static readonly DependencyProperty ActionProperty =
        DependencyProperty.Register(nameof(Action), typeof(object), typeof(SettingRow), new PropertyMetadata(null));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Segoe Fluent Icons glyph, e.g. "&#xE823;".</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>The control on the right: a toggle, a picker, buttons...</summary>
    public object? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    Visibility DividerVisibility(bool first) => first ? Visibility.Collapsed : Visibility.Visible;

    // An empty TextBlock still takes a line; hide it so single-line rows stay centered.
    Visibility VisibleIfAny(string text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
}
