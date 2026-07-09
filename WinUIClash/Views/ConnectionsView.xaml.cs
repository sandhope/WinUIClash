using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Models;
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

    private void CloseConnection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ConnectionInfo conn)
            ViewModel.CloseConnectionCommand.Execute(conn);
    }
}
