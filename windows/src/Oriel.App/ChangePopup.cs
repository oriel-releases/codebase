using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Oriel.Core.Services;
using Windows.Graphics;
using Windows.UI;

namespace Oriel.UI;

/// <summary>
/// A small dark pill that rises from the taskbar (like the "Desktop 4" label when switching desktops)
/// while a wallpaper change is under way. The window is the pill: borderless, rounded by Windows,
/// never takes focus and never shows in the taskbar or Alt+Tab.
/// </summary>
public sealed class ChangePopup : Window
{
    const double PillHeight = 40, Gap = 20, SlideMs = 240, TextMs = 120, MinVisibleMs = 700;

    static readonly string[] Words = ["Finding…", "Changing…"];

    readonly TextBlock _label;
    readonly TranslateTransform _textShift = new();
    readonly DispatcherQueueTimer _slideTimer;
    readonly int _restY, _hiddenY, _x;
    DateTime _shownAt, _slideStart;
    double _fromY, _toY;
    bool _easeOut;
    Action? _slideDone;
    string _word = Words[0];

    public ChangePopup()
    {
        _label = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = _textShift,
        };
        // Fix the text width to the longest word so the pill doesn't change size between words.
        _label.Text = Words.MaxBy(w => w.Length)!;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Padding = new Thickness(18, 0, 18, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new ProgressRing { Width = 16, Height = 16, IsActive = true, Foreground = new SolidColorBrush(Colors.White) },
                _label,
            },
        };
        row.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        _label.Width = _label.DesiredSize.Width;
        _label.Text = Words[0];
        var pillWidth = row.DesiredSize.Width;

        Content = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x26, 0x26, 0x26)), Children = { row } };

        ExtendsContentIntoTitleBar = true;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
        }
        AppWindow.IsShownInSwitchers = false;

        // A plain popup: no caption or frame, no activation, rounded by Windows, no outline.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowLongPtr(hwnd, GwlStyle, WsPopup);
        SetWindowLongPtr(hwnd, GwlExStyle, GetWindowLongPtr(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow);
        var round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
        var noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref noBorder, sizeof(int));
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        var scale = GetDpiForWindow(hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var w = (int)(pillWidth * scale);
        var h = (int)(PillHeight * scale);
        _x = area.X + (area.Width - w) / 2;
        _restY = area.Y + area.Height - h - (int)(Gap * scale);
        _hiddenY = area.Y + area.Height; // just below the work area: behind the taskbar
        AppWindow.MoveAndResize(new RectInt32(_x, _hiddenY, w, h));

        _slideTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _slideTimer.Interval = TimeSpan.FromMilliseconds(15);
        _slideTimer.Tick += (_, _) => SlideStep();
    }

    public void ShowPopup()
    {
        AppWindow.Show(activateWindow: false);
        // Directly behind the taskbar, still above ordinary windows: the pill slides out from under it.
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar != 0)
            SetWindowPos(WinRT.Interop.WindowNative.GetWindowHandle(this), taskbar, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        _shownAt = DateTime.UtcNow;
        SlideTo(_restY, easeOut: true, done: null);
    }

    public void SetPhase(ApplyPhase phase) => SetWord(phase == ApplyPhase.Changing ? Words[1] : Words[0]);

    /// <summary>Switches the word with a small slide and fade.</summary>
    void SetWord(string word)
    {
        if (word == _word) return;
        _word = word;
        var fadeOut = new Storyboard();
        fadeOut.Children.Add(Animate(_label, "Opacity", 0, TextMs, EasingMode.EaseIn));
        fadeOut.Children.Add(Animate(_textShift, "Y", -8, TextMs, EasingMode.EaseIn));
        fadeOut.Completed += (_, _) =>
        {
            _label.Text = word;
            _textShift.Y = 8;
            var fadeIn = new Storyboard();
            fadeIn.Children.Add(Animate(_label, "Opacity", 1, TextMs, EasingMode.EaseOut));
            fadeIn.Children.Add(Animate(_textShift, "Y", 0, TextMs, EasingMode.EaseOut));
            fadeIn.Begin();
        };
        fadeOut.Begin();
    }

    /// <summary>Sinks back below the taskbar and closes. Stays up long enough to be read.</summary>
    public async void Dismiss()
    {
        var remaining = MinVisibleMs - (DateTime.UtcNow - _shownAt).TotalMilliseconds;
        if (remaining > 0) await Task.Delay((int)remaining);
        SlideTo(_hiddenY, easeOut: false, done: Close);
    }

    // The window itself moves; a timer steps it along an eased curve.
    void SlideTo(int y, bool easeOut, Action? done)
    {
        _fromY = AppWindow.Position.Y;
        _toY = y;
        _easeOut = easeOut;
        _slideDone = done;
        _slideStart = DateTime.UtcNow;
        _slideTimer.Start();
    }

    void SlideStep()
    {
        var t = Math.Min(1, (DateTime.UtcNow - _slideStart).TotalMilliseconds / SlideMs);
        var eased = _easeOut ? 1 - Math.Pow(1 - t, 3) : Math.Pow(t, 3);
        AppWindow.Move(new PointInt32(_x, (int)Math.Round(_fromY + (_toY - _fromY) * eased)));
        if (t < 1) return;
        _slideTimer.Stop();
        _slideDone?.Invoke();
    }

    static DoubleAnimation Animate(DependencyObject target, string property, double to, double ms, EasingMode easing)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(ms)),
            EasingFunction = new CubicEase { EasingMode = easing },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    const int GwlStyle = -16, GwlExStyle = -20;
    const nint WsPopup = unchecked((int)0x80000000), WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    const int DwmwaWindowCornerPreference = 33, DwmwaBorderColor = 34;
    const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpFrameChanged = 0x20;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindow(string? className, string? title);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
