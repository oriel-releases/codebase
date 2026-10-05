using CommunityToolkit.Mvvm.Input;
using Oriel.Core.Settings;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace Oriel.UI.Services;

/// <summary>System tray icon with a native popup menu: Next wallpaper / Open / Pause / Quit.</summary>
public sealed class TrayIconService(ISettingsStore settings) : IDisposable
{
    TaskbarIcon? _icon;
    ToggleMenuFlyoutItem? _pause;
    bool? _shownPaused;

    public void Create(Action open, Func<Task> changeNow, Action quit)
    {
        _pause = new ToggleMenuFlyoutItem
        {
            Text = "Pause daily changes",
            Command = new RelayCommand(() => settings.Update(s => s.Paused = !s.Paused)),
        };

        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Next wallpaper", Command = new AsyncRelayCommand(changeNow) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Open Oriel", Command = new RelayCommand(open) });
        menu.Items.Add(_pause);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Quit", Command = new RelayCommand(quit) });

        _icon = new TaskbarIcon
        {
            ToolTipText = "Oriel",
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")),
            // PopupMenu is a native Win32 menu: no XAML window is kept alive for it.
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(open),
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);

        Refresh();
        settings.SettingsChanged += OnSettingsChanged;
    }

    void OnSettingsChanged(object? sender, EventArgs e) =>
        App.Dispatcher.TryEnqueue(Refresh);

    void Refresh()
    {
        var paused = settings.Load().Paused;
        if (_shownPaused == paused) return;
        _shownPaused = paused;
        if (_pause is not null) _pause.IsChecked = paused;
        if (_icon is not null) _icon.ToolTipText = paused ? "Oriel (paused)" : "Oriel";
    }

    public void Dispose()
    {
        settings.SettingsChanged -= OnSettingsChanged;
        _icon?.Dispose();
        _icon = null;
    }
}
