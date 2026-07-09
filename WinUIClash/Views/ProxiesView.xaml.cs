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

    private async void ProxyCard_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Proxy proxy } border) return;

        var menu = new MenuFlyout();

        var testItem = new MenuFlyoutItem { Text = "测试延迟" };
        testItem.Click += async (_, _) =>
            await ViewModel.TestDelayCommand.ExecuteAsync(proxy);
        menu.Items.Add(testItem);

        var selectItem = new MenuFlyoutItem { Text = "选择此节点" };
        selectItem.Click += async (_, _) =>
            await ViewModel.SelectProxyCommand.ExecuteAsync(proxy);
        menu.Items.Add(selectItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var copyItem = new MenuFlyoutItem { Text = "复制名称" };
        copyItem.Click += (_, _) =>
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(proxy.Name);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        };
        menu.Items.Add(copyItem);

        menu.ShowAt(border, e.GetPosition(border));
    }
}
