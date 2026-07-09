using Microsoft.UI.Xaml.Controls;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class ConnectionsView : Page
{
    public ConnectionsViewModel ViewModel { get; }

    public ConnectionsView()
    {
        ViewModel = ServiceLocator.Get<ConnectionsViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }
}
