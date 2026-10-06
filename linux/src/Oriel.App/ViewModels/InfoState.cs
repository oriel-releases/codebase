using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Oriel.UI.ViewModels;

public enum InfoSeverity { Informational, Success, Warning, Error }

/// <summary>What an info banner shows: a title and a message in a severity colour, until it is closed.</summary>
public sealed partial class InfoState : ObservableObject
{
    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial InfoSeverity Severity { get; set; }

    /// <summary>Most banners can be dismissed; a standing warning (nothing selected) can't.</summary>
    [ObservableProperty] public partial bool IsClosable { get; set; } = true;

    public bool HasTitle => Title.Length > 0;

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(HasTitle));

    public void Show(InfoSeverity severity, string title, string message)
    {
        Severity = severity;
        Title = title;
        Message = message;
        IsOpen = true;
    }

    [RelayCommand]
    public void Close() => IsOpen = false;
}
