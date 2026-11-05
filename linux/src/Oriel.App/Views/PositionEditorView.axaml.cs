using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

/// <summary>
/// The position editor over the whole window (opened from Home's Position button): the photo fitted into the space,
/// the window in this screen's shape over it, the rest dimmed. Dragging or clicking moves the window along the photo;
/// Smart, Center and Custom glide it there. The choices and Set are the view model's. Like Windows'.
/// </summary>
public partial class PositionEditorView : UserControl
{
    /// <summary>The window's editor; set once by the main window.</summary>
    internal static PositionEditorView? Current { get; set; }

    const double SegmentWidth = 96;

    readonly PositionEditorViewModel _vm = App.Get<PositionEditorViewModel>();
    /// <summary>The window's middle as drawn (0..1 along): it glides toward the picked one instead of jumping.</summary>
    double _drawn = double.NaN;
    bool _dragging;
    bool _gliding;
    double _photoWidth;
    double _photoHeight;

    public PositionEditorView()
    {
        InitializeComponent();
        DataContext = _vm;
        Stage.SizeChanged += (_, _) => Layout();
        Board.PointerPressed += Board_PointerPressed;
        Board.PointerMoved += (_, e) => { if (_dragging) PlaceAt(e); };
        Board.PointerReleased += (_, _) => EndDrag();
        Board.PointerCaptureLost += (_, _) => EndDrag();
        _vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(PositionEditorViewModel.IsReady):
                    Layout();
                    break;
                case nameof(PositionEditorViewModel.ShownCenter):
                    Glide();
                    break;
                case nameof(PositionEditorViewModel.Mode):
                    PlacePill();
                    break;
            }
        };
        PlacePill();
    }

    public bool IsOpen => IsVisible;

    public async void Open()
    {
        if (!_vm.CanEdit) return;
        _drawn = double.NaN;
        IsVisible = true;
        await _vm.OpenAsync();
    }

    public void Close()
    {
        if (_vm.IsBusy) return;
        _vm.Close();
        IsVisible = false;
    }

    void Close_Click(object? sender, RoutedEventArgs e) => Close();

    async void Set_Click(object? sender, RoutedEventArgs e)
    {
        if (await _vm.SetAsync()) Close();
    }

    /// <summary>The ink pill under the picked choice (its transition eases it there).</summary>
    void PlacePill() => ModeIndicator.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse(
        $"translateX({(int)_vm.Mode * SegmentWidth}px)");

    /// <summary>The photo as big as the space allows, keeping its shape.</summary>
    void Layout()
    {
        if (!_vm.IsReady || _vm.PhotoAspect <= 0 || Stage.Bounds.Width <= 0 || Stage.Bounds.Height <= 0) return;
        var space = Stage.Bounds.Width / Stage.Bounds.Height;
        (_photoWidth, _photoHeight) = _vm.PhotoAspect > space
            ? (Stage.Bounds.Width, Stage.Bounds.Width / _vm.PhotoAspect)
            : (Stage.Bounds.Height * _vm.PhotoAspect, Stage.Bounds.Height);
        Board.Width = Whole.Width = _photoWidth;
        Board.Height = Whole.Height = _photoHeight;
        if (double.IsNaN(_drawn)) _drawn = _vm.ShownCenter;
        Draw();
    }

    /// <summary>Puts the window (and the hole in the dimming) where <see cref="_drawn"/> says.</summary>
    void Draw()
    {
        if (_vm.Window is not { } window || _photoWidth <= 0) return;
        // The view model's window is in a photo 1 tall; here the photo is _photoHeight tall.
        var scale = _photoHeight;
        var (x, y) = window.Place(_vm.PhotoAspect, 1, _drawn);
        var frame = new Rect(x * scale, y * scale, window.Width * scale, window.Height * scale);
        Canvas.SetLeft(WindowFrame, frame.X);
        Canvas.SetTop(WindowFrame, frame.Y);
        WindowFrame.Width = frame.Width;
        WindowFrame.Height = frame.Height;
        Dim.Data = new GeometryGroup
        {
            FillRule = FillRule.EvenOdd,
            Children = { new RectangleGeometry(new Rect(0, 0, _photoWidth, _photoHeight)), new RectangleGeometry(frame) },
        };
    }

    /// <summary>Toward the picked place, a little closer every frame: a soft glide that settles, and a drag follows at once.</summary>
    void Glide()
    {
        if (_dragging || double.IsNaN(_drawn))
        {
            _drawn = _vm.ShownCenter;
            Draw();
            return;
        }
        if (_gliding) return;
        _gliding = true;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Step);
    }

    void Step(TimeSpan _)
    {
        var target = _vm.ShownCenter;
        _drawn += (target - _drawn) * 0.2;
        var settled = Math.Abs(target - _drawn) < 0.0005;
        if (settled) _drawn = target;
        Draw();
        if (settled) _gliding = false;
        else TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Step);
    }

    void Board_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm.IsBusy || _vm.Window is null) return;
        _dragging = true;
        e.Pointer.Capture(Board);
        Thirds.Opacity = 1;
        PlaceAt(e);
    }

    void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        Thirds.Opacity = 0;
    }

    void PlaceAt(PointerEventArgs e)
    {
        if (_vm.Window is not { } window) return;
        var at = e.GetPosition(Board);
        var along = window.Across ? at.X / _photoHeight : at.Y / _photoHeight;
        _vm.PlaceAt(window.CenterAt(_vm.PhotoAspect, 1, along));
    }
}
