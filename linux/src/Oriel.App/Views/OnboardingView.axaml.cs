using Avalonia.Controls;
using Oriel.UI.ViewModels;

namespace Oriel.UI.Views;

/// <summary>The first-run tour. Covers the app after the first sign-in, like the sign-in screen does.</summary>
public partial class OnboardingView : UserControl
{
    public OnboardingView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not OnboardingViewModel vm) return;
            DailyTimePicker.SelectedTime = vm.DailyTime;
            DailyTimePicker.SelectedTimeChanged += (_, e) =>
            {
                if (e.NewTime is { } time) vm.DailyTime = time;
            };
        };
    }
}
