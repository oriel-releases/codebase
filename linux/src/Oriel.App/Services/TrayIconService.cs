using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Oriel.Core.Settings;

namespace Oriel.UI.Services;

/// <summary>
/// System tray icon with a menu: Next wallpaper / Open / Pause / Quit. Uses the StatusNotifierItem
/// protocol (KDE, XFCE, Cinnamon, and GNOME with the AppIndicator extension); where there is no tray, Oriel
/// still works from its window and the menu entry.
/// </summary>
public sealed class TrayIconService(ISettingsStore settings) : IDisposable
{
    TrayIcon? _icon;
    NativeMenuItem? _pause;
    bool? _shownPaused;

    public void Create(Action open, Func<Task> changeNow, Action quit)
    {
        _pause = new NativeMenuItem("Pause daily changes") { ToggleType = MenuItemToggleType.CheckBox };
        _pause.Click += (_, _) => settings.Update(s => s.Paused = !s.Paused);

        var next = new NativeMenuItem("Next wallpaper");
        next.Click += (_, _) => _ = changeNow();
        var show = new NativeMenuItem("Open Oriel");
        show.Click += (_, _) => open();
        var exit = new NativeMenuItem("Quit");
        exit.Click += (_, _) => quit();

        var menu = new NativeMenu();
        menu.Add(next);
        menu.Add(show);
        menu.Add(_pause);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exit);

        using var stream = AssetLoader.Open(new Uri("avares://Oriel/Assets/AppIcon.png"));
        _icon = new TrayIcon
        {
            ToolTipText = "Oriel",
            Icon = new WindowIcon(stream),
            Menu = menu,
        };
        _icon.Clicked += (_, _) => open();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _icon });

        Refresh();
        settings.SettingsChanged += OnSettingsChanged;
    }

    void OnSettingsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);

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
