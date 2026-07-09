using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinUIClash.Models;
using WinUIClash.Services;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class RequestsView : Page
{
    public RequestsViewModel ViewModel { get; }

    public RequestsView()
    {
        ViewModel = ServiceLocator.Get<RequestsViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private void RequestItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConnectionInfo conn } element) return;

        var menu = new MenuFlyout();

        var copyHost = new MenuFlyoutItem
        {
            Text = LocalizationHelper.GetString("RequestsCopyHost.Text")
        };
        copyHost.Click += (_, _) => CopyToClipboard(conn.Metadata.Host);
        menu.Items.Add(copyHost);

        var copyRule = new MenuFlyoutItem
        {
            Text = LocalizationHelper.GetString("RequestsCopyRule.Text")
        };
        copyRule.Click += (_, _) => CopyToClipboard($"{conn.Rule} → {string.Join(", ", conn.Chains)}");
        menu.Items.Add(copyRule);

        var copyProcess = new MenuFlyoutItem
        {
            Text = LocalizationHelper.GetString("RequestsCopyProcess.Text")
        };
        copyProcess.Click += (_, _) => CopyToClipboard(conn.Metadata.Process);
        menu.Items.Add(copyProcess);

        menu.Items.Add(new MenuFlyoutSeparator());

        var copyAll = new MenuFlyoutItem
        {
            Text = LocalizationHelper.GetString("RequestsCopyAll.Text")
        };
        copyAll.Click += (_, _) => CopyToClipboard(
            $"[{conn.Start:HH:mm:ss}] {conn.Metadata.Host} | {conn.Metadata.Network} | {conn.Rule} → {string.Join(", ", conn.Chains)}");
        menu.Items.Add(copyAll);

        menu.ShowAt(element, e.GetPosition(element));
    }

    private void RequestItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ConnectionInfo conn })
            CopyToClipboard(conn.Metadata.Host);
    }

    private static void CopyToClipboard(string text)
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }
}
