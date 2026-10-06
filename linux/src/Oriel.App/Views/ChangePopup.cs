using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Oriel.Core.Services;

namespace Oriel.UI.Views;

/// <summary>
/// A small dark pill that rises from the bottom edge of the screen while a wallpaper change is under way.
/// Borderless, never takes focus, not in the taskbar. The window is moved a few times by a short timer,
/// which stops when the pill has arrived; nothing runs while it sits there.
/// </summary>
public sealed class ChangePopup : Window
{
    const double PillHeight = 40, Gap = 20, SlideMs = 240, MinVisibleMs = 700;

    static readonly string[] Words = ["Finding…", "Changing…"];

    readonly TextBlock _label;
    readonly DispatcherTimer _slideTimer;
    DateTime _shownAt, _slideStart;
    double _fromY, _toY;
    bool _easeOut;
    Action? _slideDone;
    int _x, _restY, _hiddenY;

    public ChangePopup()
    {
        _label = new TextBlock
        {
            Text = Words[0],
            Foreground = Brushes.White,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            // Wide enough for the longest word, so the pill doesn't change size between words.
            MinWidth = 64,
        };
        var spinner = new Arc
        {
            Width = 16, Height = 16, StartAngle = 0, SweepAngle = 270, StrokeThickness = 2,
            Stroke = Brushes.White, StrokeLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center,
        };
        spinner.Classes.Add("spinner");
        spinner.Classes.Add("busy");

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x14, 0x14, 0x14)),
            CornerRadius = new CornerRadius(PillHeight / 2),
            Padding = new Thickness(18, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { spinner, _label },
            },
        };

        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Height = PillHeight;

        _slideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _slideTimer.Tick += (_, _) => SlideStep();
    }

    public void ShowPopup()
    {
        var screen = Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = screen?.Scaling ?? 1.0;

        // Placed before showing, and kept centered by the measured width once it is known.
        Position = new PixelPoint(area.X + area.Width / 2 - (int)(80 * scale), area.Bottom);
        Opened += (_, _) =>
        {
            var w = (int)(Bounds.Width * scale);
            var h = (int)(PillHeight * scale);
            _x = area.X + (area.Width - w) / 2;
            _restY = area.Bottom - h - (int)(Gap * scale);
            _hiddenY = area.Bottom; // just below the work area: behind the panel
            Position = new PixelPoint(_x, _hiddenY);
            _shownAt = DateTime.UtcNow;
            SlideTo(_restY, easeOut: true, done: null);
        };
        Show();
    }

    public void SetPhase(ApplyPhase phase) => _label.Text = phase == ApplyPhase.Changing ? Words[1] : Words[0];

    /// <summary>Sinks back below the screen edge and closes. Stays up long enough to be read.</summary>
    public async void Dismiss()
    {
        var remaining = MinVisibleMs - (DateTime.UtcNow - _shownAt).TotalMilliseconds;
        if (remaining > 0) await Task.Delay((int)remaining);
        SlideTo(_hiddenY, easeOut: false, done: Close);
    }

    void SlideTo(int y, bool easeOut, Action? done)
    {
        _fromY = Position.Y;
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
        Position = new PixelPoint(_x, (int)Math.Round(_fromY + (_toY - _fromY) * eased));
        if (t < 1) return;
        _slideTimer.Stop();
        _slideDone?.Invoke();
    }
}
