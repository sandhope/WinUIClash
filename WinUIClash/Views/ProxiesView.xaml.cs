using Microsoft.UI.Xaml.Controls;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class ProxiesView : Page
{
    public ProxiesViewModel ViewModel { get; }

    public ProxiesView()
    {
        ViewModel = ServiceLocator.Get<ProxiesViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }
}
