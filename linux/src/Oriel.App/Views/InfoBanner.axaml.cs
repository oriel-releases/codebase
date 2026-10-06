using System.ComponentModel;
using Avalonia.Controls;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

/// <summary>A message bar tinted by severity. Its data is an <see cref="InfoState"/>.</summary>
public partial class InfoBanner : UserControl
{
    InfoState? _state;

    public InfoBanner()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as InfoState);
    }

    void Attach(InfoState? state)
    {
        if (_state is not null) _state.PropertyChanged -= OnChanged;
        _state = state;
        if (_state is not null) _state.PropertyChanged += OnChanged;
        ApplySeverity();
    }

    void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InfoState.Severity)) ApplySeverity();
    }

    void ApplySeverity()
    {
        var severity = _state?.Severity ?? InfoSeverity.Informational;
        Bar.Classes.Set("success", severity == InfoSeverity.Success);
        Bar.Classes.Set("warning", severity == InfoSeverity.Warning);
        Bar.Classes.Set("error", severity == InfoSeverity.Error);
    }
}
