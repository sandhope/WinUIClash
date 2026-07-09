using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Models;
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

    private void GroupTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProxyGroup group)
            ViewModel.SelectGroupCommand.Execute(group);
    }

    private void ProxyGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Proxy proxy)
            ViewModel.SelectProxyCommand.Execute(proxy);
    }
}
