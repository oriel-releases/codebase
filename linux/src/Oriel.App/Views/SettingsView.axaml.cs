using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Oriel.Core.Platform;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Connect(DataContext as SettingsViewModel);
        ShortcutBox.KeyDown += Shortcut_KeyDown;
    }

    void Connect(SettingsViewModel? vm)
    {
        if (vm is null) return;
        // Rows that depend on another setting come and go with a slide and a fade (Reveal), never all at once.
        MinLikesRow.IsVisible = vm.SkipLowLikes;
        DailyTimeRow.IsVisible = vm.IsDaily;
        ApplyAtLoginRow.IsVisible = vm.IsDaily;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.SkipLowLikes)) Reveal(MinLikesRow, vm.SkipLowLikes);
            if (e.PropertyName == nameof(SettingsViewModel.IsDaily))
            {
                Reveal(DailyTimeRow, vm.IsDaily);
                Reveal(ApplyAtLoginRow, vm.IsDaily);
            }
        };
        DailyTimePicker.SelectedTime = vm.DailyTime;
        DailyTimePicker.SelectedTimeChanged += (_, e) =>
        {
            if (e.NewTime is { } time) vm.DailyTime = time;
        };
        vm.PickFolder = PickFolderAsync;
        vm.ConfirmTurnOffStartup = () => ConfirmDialog.AskAsync(this, "Turn off Start at login?",
            "Oriel won't open when you log in, so the tray icon and your shortcut won't work until you open it yourself. " +
            "Scheduled wallpaper changes will still run.", "Turn off", "Keep on");
    }

    /// <summary>Records the pressed combination as the shortcut. Needs a modifier (Ctrl, Alt or Super) so plain typing never triggers it.</summary>
    void Shortcut_KeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not SettingsViewModel vm) return;

        uint mods = 0;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) mods |= Hotkey.Ctrl;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) mods |= Hotkey.Alt;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) mods |= Hotkey.Shift;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) mods |= Hotkey.Super;

        if (Hotkey.NormalizeKey(KeyName(e.Key)) is not { } key || (mods & ~Hotkey.Shift) == 0) return;
        _ = vm.SetShortcutAsync(new Hotkey(mods, key));
    }

    static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Space or Key.PageUp or Key.End or Key.Home or Key.Left or Key.Up or Key.Right or Key.Down or Key.Insert or Key.Delete
            => key.ToString(),
        Key.Next => "PageDown",
        _ => null,
    };

    async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanPickFolder: true } storage) return null;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose an empty folder for your wallpapers",
            AllowMultiple = false,
        });
        return folders.Count == 1 ? folders[0].TryGetLocalPath() : null;
    }

    readonly Dictionary<Control, int> _reveals = [];

    /// <summary>A row comes in from just above with a fade, and goes the same way (like on Windows).</summary>
    async void Reveal(Control row, bool show)
    {
        var version = _reveals[row] = _reveals.GetValueOrDefault(row) + 1;
        var above = Avalonia.Media.Transformation.TransformOperations.Parse("translateY(-10px)");
        var home = Avalonia.Media.Transformation.TransformOperations.Parse("translateY(0px)");
        if (show)
        {
            row.Opacity = 0;
            row.RenderTransform = above;
            row.IsVisible = true;
            await Task.Delay(16);
            if (version != _reveals[row]) return;
            row.Opacity = 1;
            row.RenderTransform = home;
        }
        else
        {
            row.Opacity = 0;
            row.RenderTransform = above;
            await Task.Delay(220);
            if (version == _reveals[row]) row.IsVisible = false;
        }
    }
}
