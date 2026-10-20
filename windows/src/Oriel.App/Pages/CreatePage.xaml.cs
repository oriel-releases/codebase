using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace Oriel.UI.Pages;

public sealed partial class CreatePage : Page
{
    public CreateViewModel ViewModel { get; } = App.Get<CreateViewModel>();

    public CreatePage()
    {
        InitializeComponent();
        // The ring's gradient lives in the resources, where a name can't be found: the animation is pointed at it here.
        Storyboard.SetTarget(Turn, ((LinearGradientBrush)Resources["AiGradient"]).RelativeTransform);
        // The ring turns and the bubbles drift only while the page is on screen.
        Loaded += (_, _) => { Spin.Begin(); Float.Begin(); Working.Begin(); };
        Unloaded += (_, _) => { Spin.Stop(); Float.Stop(); Working.Stop(); };
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.ShuffleIdeas();
    }

    void Idea_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not string idea) return;
        ViewModel.UseIdeaCommand.Execute(idea);
        // Ready to change a word or press Enter.
        PromptBox.Focus(FocusState.Programmatic);
        PromptBox.SelectionStart = PromptBox.Text.Length;
    }

    void Prompt_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !ViewModel.CreateCommand.CanExecute(null)) return;
        ViewModel.CreateCommand.Execute(null);
        e.Handled = true;
    }
}
