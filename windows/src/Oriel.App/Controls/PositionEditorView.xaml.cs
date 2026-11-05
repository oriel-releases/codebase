using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Oriel.UI.ViewModels;
using Windows.Foundation;

namespace Oriel.UI.Controls;

/// <summary>
/// The position editor over the whole window (opened from Home's Position button): the photo fitted into the space,
/// the window in this screen's shape over it, the rest dimmed. Dragging or clicking moves the window along the photo;
/// Smart, Center and Custom glide it there. The choices and Set are the view model's.
/// </summary>
public sealed partial class PositionEditorView : UserControl
{
    /// <summary>The window's editor; set once by the main window.</summary>
    internal static PositionEditorView? Current { get; set; }

    public PositionEditorViewModel ViewModel { get; } = App.Get<PositionEditorViewModel>();

    /// <summary>The window's middle as drawn (0..1 along): it glides toward the picked one instead of jumping.</summary>
    double _drawn = double.NaN;
    bool _dragging;
    bool _gliding;
    double _photoWidth;
    double _photoHeight;

    public PositionEditorView()
    {
        InitializeComponent();
        _ = new TabHighlight(ModeStrip, ModeIndicator);
        ViewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(PositionEditorViewModel.IsReady):
                case nameof(PositionEditorViewModel.Error):
                    ShowState();
                    Layout();
                    break;
                case nameof(PositionEditorViewModel.ShownCenter):
                    Glide();
                    break;
            }
        };
    }

    public async void Open()
    {
        if (!ViewModel.CanEdit) return;
        _drawn = double.NaN;
        Visibility = Visibility.Visible;
        ShowState();
        Motion.PageIn(Root);
        Root.Focus(FocusState.Programmatic);
        await ViewModel.OpenAsync();
    }

    public void Close()
    {
        if (ViewModel.IsBusy) return;
        ViewModel.Close();
        Visibility = Visibility.Collapsed;
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        e.Handled = true;
        Close();
    }

    async void Set_Click(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.SetAsync()) Close();
    }

    void ShowState()
    {
        Board.Visibility = ViewModel.IsReady ? Visibility.Visible : Visibility.Collapsed;
        Loading.Visibility = ViewModel.IsReady || ViewModel.Error is not null ? Visibility.Collapsed : Visibility.Visible;
        ErrorText.Visibility = ViewModel.Error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    void Stage_SizeChanged(object sender, SizeChangedEventArgs e) => Layout();

    /// <summary>The photo as big as the space allows, keeping its shape.</summary>
    void Layout()
    {
        if (!ViewModel.IsReady || ViewModel.PhotoAspect <= 0 || Stage.ActualWidth <= 0 || Stage.ActualHeight <= 0) return;
        var space = Stage.ActualWidth / Stage.ActualHeight;
        (_photoWidth, _photoHeight) = ViewModel.PhotoAspect > space
            ? (Stage.ActualWidth, Stage.ActualWidth / ViewModel.PhotoAspect)
            : (Stage.ActualHeight * ViewModel.PhotoAspect, Stage.ActualHeight);
        Board.Width = Whole.Width = _photoWidth;
        Board.Height = Whole.Height = _photoHeight;
        DimAll.Rect = new Rect(0, 0, _photoWidth, _photoHeight);
        if (double.IsNaN(_drawn)) _drawn = ViewModel.ShownCenter;
        Draw();
    }

    /// <summary>Puts the window (and the hole in the dimming) where <see cref="_drawn"/> says.</summary>
    void Draw()
    {
        if (ViewModel.Window is not { } window || _photoWidth <= 0) return;
        // The view model's window is in a photo 1 tall; here the photo is _photoHeight tall.
        var scale = _photoHeight;
        var (x, y) = window.Place(ViewModel.PhotoAspect, 1, _drawn);
        var frame = new Rect(x * scale, y * scale, window.Width * scale, window.Height * scale);
        Canvas.SetLeft(WindowFrame, frame.X);
        Canvas.SetTop(WindowFrame, frame.Y);
        WindowFrame.Width = frame.Width;
        WindowFrame.Height = frame.Height;
        DimHole.Rect = frame;
    }

    /// <summary>Toward the picked place, a little closer every frame: a soft glide that settles, and a drag follows at once.</summary>
    void Glide()
    {
        if (_dragging || double.IsNaN(_drawn))
        {
            _drawn = ViewModel.ShownCenter;
            Draw();
            return;
        }
        if (_gliding) return;
        _gliding = true;
        CompositionTarget.Rendering += Step;
    }

    void Step(object? sender, object e)
    {
        var target = ViewModel.ShownCenter;
        _drawn += (target - _drawn) * 0.2;
        if (Math.Abs(target - _drawn) < 0.0005)
        {
            _drawn = target;
            CompositionTarget.Rendering -= Step;
            _gliding = false;
        }
        Draw();
    }

    void Board_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.IsBusy || ViewModel.Window is null) return;
        _dragging = true;
        Board.CapturePointer(e.Pointer);
        Thirds.Opacity = 1;
        PlaceAt(e);
    }

    void Board_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging) PlaceAt(e);
    }

    void Board_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Board.ReleasePointerCapture(e.Pointer);
        Thirds.Opacity = 0;
    }

    void PlaceAt(PointerRoutedEventArgs e)
    {
        if (ViewModel.Window is not { } window) return;
        var at = e.GetCurrentPoint(Board).Position;
        var along = window.Across ? at.X / _photoHeight : at.Y / _photoHeight;
        ViewModel.PlaceAt(window.CenterAt(ViewModel.PhotoAspect, 1, along));
    }
}
