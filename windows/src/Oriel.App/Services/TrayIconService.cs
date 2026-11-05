using CommunityToolkit.Mvvm.Input;
using Oriel.Core.Services;
using Oriel.Core.Settings;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace Oriel.UI.Services;

/// <summary>
/// System tray icon with a native popup menu: Next wallpaper / Open / Pause / Quit. When a wallpaper arrives from
/// another device of the account, the icon gives a little pulse (shrinks and springs back).
/// </summary>
public sealed class TrayIconService(ISettingsStore settings, WallpaperService wallpapers) : IDisposable
{
    /// <summary>The icon's sizes through one pulse, as a share of its full size (one frame every <see cref="FrameMs"/>).</summary>
    static readonly float[] PulseScales = [0.86f, 0.72f, 0.68f, 0.78f, 0.94f, 1.04f, 1f];
    const int FrameMs = 45;

    TaskbarIcon? _icon;
    System.Drawing.Icon? _appIcon;
    System.Drawing.Icon[]? _pulseFrames;
    bool _pulsing;
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
            Icon = _appIcon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")),
            // PopupMenu is a native Win32 menu: no XAML window is kept alive for it.
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(open),
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);

        Refresh();
        settings.SettingsChanged += OnSettingsChanged;
        wallpapers.PhaseChanged += OnPhase;
    }

    void OnPhase(object? sender, ApplyPhase phase)
    {
        if (phase == ApplyPhase.Receiving) App.Dispatcher.TryEnqueue(() => _ = PulseAsync());
    }

    async Task PulseAsync()
    {
        if (_pulsing || _icon is null || _appIcon is null) return;
        _pulsing = true;
        try
        {
            _pulseFrames ??= PulseScales.Select(scale => Scaled(_appIcon, scale)).ToArray();
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
    static System.Drawing.Icon Scaled(System.Drawing.Icon icon, float scale)
    {
        var size = new System.Drawing.Size(GetSystemMetrics(SmallIconWidth), GetSystemMetrics(SmallIconHeight));
        using var source = new System.Drawing.Icon(icon, size).ToBitmap();
        using var frame = new System.Drawing.Bitmap(size.Width, size.Height);
        using (var g = System.Drawing.Graphics.FromImage(frame))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            var w = size.Width * scale;
            var h = size.Height * scale;
            g.DrawImage(source, (size.Width - w) / 2, (size.Height - h) / 2, w, h);
        }
        var handle = frame.GetHicon();
        // A copy that owns its own handle, so the one GetHicon made can be freed now.
        var owned = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return owned;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    const int SmallIconWidth = 49, SmallIconHeight = 50; // SM_CXSMICON, SM_CYSMICON: the tray's icon size at this DPI

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

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
        wallpapers.PhaseChanged -= OnPhase;
        _icon?.Dispose();
        _icon = null;
    }
}
