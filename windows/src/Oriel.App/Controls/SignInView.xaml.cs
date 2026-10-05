using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Oriel.UI.Controls;

public sealed partial class SignInView : UserControl
{
    public SignInViewModel ViewModel { get; } = App.Get<SignInViewModel>();

    public SignInView() => InitializeComponent();
}
