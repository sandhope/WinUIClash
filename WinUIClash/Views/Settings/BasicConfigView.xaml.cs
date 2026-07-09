using Microsoft.UI.Xaml.Controls;
using WinUIClash.ViewModels.Settings;

namespace WinUIClash.Views.Settings;

public sealed partial class BasicConfigView : UserControl
{
    public BasicConfigViewModel ViewModel { get; }

    public BasicConfigView()
    {
        ViewModel = ServiceLocator.Get<BasicConfigViewModel>();
        InitializeComponent();
    }
}
