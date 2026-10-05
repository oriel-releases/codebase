using Oriel.UI.Services;
using Oriel.UI.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using Microsoft.Windows.Storage.Pickers;

namespace Oriel.UI.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = App.Get<SettingsViewModel>();

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

    /// <summary>Records the pressed combination as the shortcut. Needs a modifier (Ctrl, Alt or Win) so plain typing never triggers it.</summary>
    void Shortcut_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        static bool Down(VirtualKey key) =>
            InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

        uint mods = 0;
        if (Down(VirtualKey.Control)) mods |= Hotkey.Ctrl;
        if (Down(VirtualKey.Menu)) mods |= Hotkey.Alt;
        if (Down(VirtualKey.Shift)) mods |= Hotkey.Shift;
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) mods |= Hotkey.Win;

        var key = (uint)e.Key;
        if (Hotkey.KeyName(key) is null || (mods & ~Hotkey.Shift) == 0) return;
        _ = ViewModel.SetShortcutAsync(new Hotkey(mods, key));
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
