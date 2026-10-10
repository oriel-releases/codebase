using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Oriel.UI.Controls;

/// <summary>The first-run tour. Covers the app after the first sign-in, like the sign-in screen does.</summary>
public sealed partial class OnboardingView : UserControl
{
    public OnboardingViewModel ViewModel { get; } = App.Get<OnboardingViewModel>();

    public OnboardingView()
    {
        InitializeComponent();
        DailyTimePicker.SelectedTime = ViewModel.DailyTime;
        DailyTimePicker.SelectedTimeChanged += (_, e) =>
        {
            if (e.NewTime is { } time) ViewModel.DailyTime = time;
        };
    }
}
