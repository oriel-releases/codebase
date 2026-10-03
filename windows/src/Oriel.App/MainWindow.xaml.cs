using Oriel.UI.Pages;
using Oriel.UI.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace Oriel.UI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = 560;
            p.PreferredMinimumHeight = 480;
        }
        ResizeAndCenter(1100, 780);

        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
                _ = App.Get<HomeViewModel>().RefreshAsync();
        };
        ContentFrame.Navigate(typeof(HomePage));
    }

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        // Fires while InitializeComponent applies IsChecked, before the frame exists.
        if (ContentFrame is null) return;
        var page = ((FrameworkElement)sender).Tag switch
        {
            "collections" => typeof(CollectionsPage),
            "history" => typeof(HistoryPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage),
        };
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }

    void ResizeAndCenter(int width, int height)
    {
        var scale = (Content?.XamlRoot?.RasterizationScale) ?? GetDpiScale();
        var w = (int)(width * scale);
        var h = (int)(height * scale);
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        w = Math.Min(w, area.Width);
        h = Math.Min(h, area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
    }

    double GetDpiScale() => GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetDpiForWindow(nint hwnd);
}
