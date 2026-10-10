using Oriel.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Oriel.UI.Controls;

public sealed partial class SharedPhotoView : UserControl
{
    public SharedPhotoViewModel ViewModel { get; } = App.Get<SharedPhotoViewModel>();

    public SharedPhotoView() => InitializeComponent();
}
