using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Oriel.Core.Services;
using Oriel.Core.Settings;

namespace Oriel.UI.Services;

/// <summary>
/// System tray icon with a menu: Next wallpaper / Open / Pause / Quit. Uses the StatusNotifierItem
/// protocol (KDE, XFCE, Cinnamon, and GNOME with the AppIndicator extension); where there is no tray, Oriel
/// still works from its window and the menu entry. When a wallpaper arrives from another device of the account, the
/// icon gives a little pulse (shrinks and springs back), like on Windows.
/// </summary>
public sealed class TrayIconService(ISettingsStore settings, WallpaperService wallpapers) : IDisposable
{
    /// <summary>The icon's sizes through one pulse, as a share of its full size (one frame every <see cref="FrameMs"/>).</summary>
    static readonly double[] PulseScales = [0.86, 0.72, 0.68, 0.78, 0.94, 1.04, 1];
    const int FrameMs = 45;
    const int FrameSize = 64;

    TrayIcon? _icon;
    WindowIcon? _appIcon;
    WindowIcon[]? _pulseFrames;
    bool _pulsing;
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
            Icon = _appIcon = new WindowIcon(stream),
            Menu = menu,
        };
        _icon.Clicked += (_, _) => open();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _icon });

        Refresh();
        settings.SettingsChanged += OnSettingsChanged;
        wallpapers.PhaseChanged += OnPhase;
    }

    void OnPhase(object? sender, ApplyPhase phase)
    {
        if (phase == ApplyPhase.Receiving) Dispatcher.UIThread.Post(() => _ = PulseAsync());
    }

    async Task PulseAsync()
    {
        if (_pulsing || _icon is null || _appIcon is null) return;
        _pulsing = true;
        try
        {
            _pulseFrames ??= PulseScales.Select(Scaled).ToArray();
            foreach (var frame in _pulseFrames)
            {
                if (_icon is null) return;
                _icon.Icon = frame;
                await Task.Delay(FrameMs);
            }
            if (_icon is not null) _icon.Icon = _appIcon;
        }
        finally
        {
            _pulsing = false;
        }
    }

    /// <summary>The app icon drawn smaller in the middle of its own square (the tray keeps the square's size).</summary>
    static WindowIcon Scaled(double scale)
    {
        using var stream = AssetLoader.Open(new Uri("avares://Oriel/Assets/AppIcon.png"));
        using var source = new Avalonia.Media.Imaging.Bitmap(stream);
        var frame = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(FrameSize, FrameSize));
        using (var context = frame.CreateDrawingContext())
        {
            var side = FrameSize * scale;
            var inset = (FrameSize - side) / 2;
            context.DrawImage(source, new Rect(inset, inset, side, side));
        }
        using var png = new MemoryStream();
        frame.Save(png);
        png.Position = 0;
        return new WindowIcon(png);
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
        wallpapers.PhaseChanged -= OnPhase;
        _icon?.Dispose();
        _icon = null;
    }
}
