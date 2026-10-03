using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace Oriel.UI.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = App.Get<SettingsViewModel>();

    public string AboutHeader => ViewModel.Stage is { Length: > 0 } stage
        ? $"Oriel {ViewModel.Version} {stage.ToLowerInvariant()}"
        : $"Oriel {ViewModel.Version}";

    public SettingsPage()
    {
        InitializeComponent();
        DailyTimePicker.SelectedTime = ViewModel.DailyTime;
        DailyTimePicker.SelectedTimeChanged += (_, e) =>
        {
            if (e.NewTime is { } time) ViewModel.DailyTime = time;
        };
        ViewModel.PickFolder = PickFolderAsync;
        Loaded += (_, _) => ViewModel.AttachUpdates();
        Unloaded += (_, _) => ViewModel.DetachUpdates();
    }

    async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
        };
        return (await picker.PickSingleFolderAsync())?.Path;
    }
}
