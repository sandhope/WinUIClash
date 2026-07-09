using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinUIClash.Models;
using WinUIClash.Services;
using WinUIClash.ViewModels;

namespace WinUIClash.Views;

public sealed partial class RulesView : Page
{
    public RulesViewModel ViewModel { get; }

    public RulesView()
    {
        ViewModel = ServiceLocator.Get<RulesViewModel>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await ViewModel.InitializeAsync();
            PopulateProxyFilter();
        };

        // 监听代理选项变化以刷新 ComboBox
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RulesViewModel.ProxyOptions))
                PopulateProxyFilter();
        };
    }

    private void PopulateProxyFilter()
    {
        ProxyFilterCombo.Items.Clear();
        ProxyFilterCombo.Items.Add(new ComboBoxItem { Content = LocalizationHelper.GetString("RulesAllProxiesFilter.Text"), Tag = "" });
        foreach (var proxy in ViewModel.ProxyOptions)
        {
            if (string.IsNullOrEmpty(proxy)) continue;
            ProxyFilterCombo.Items.Add(new ComboBoxItem { Content = proxy, Tag = proxy });
        }
        ProxyFilterCombo.SelectedIndex = 0;
    }

    private void ProxyFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
        {
            ViewModel.SelectedProxyFilter = item.Tag as string ?? "";
        }
    }

    private void RuleItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Rule rule } element) return;

        var menu = new MenuFlyout();

        var copyType = new MenuFlyoutItem
        {
            Text = LocalizationHelper.GetString("RulesCopyType.Text")
        };
        copyType.Click += (_, _) => CopyToClipboard(rule.Type);
        menu.Items.Add(copyType);

        var copyPayload = new MenuFlyoutItem
        {
            Text = LocalizationHelper.GetString("RulesCopyPayload.Text")
        };
        copyPayload.Click += (_, _) => CopyToClipboard(rule.Payload);
        menu.Items.Add(copyPayload);

        var copyProxy = new MenuFlyoutItem
        {
            Text = LocalizationHelper.GetString("RulesCopyProxy.Text")
        };
        copyProxy.Click += (_, _) => CopyToClipboard(rule.Proxy);
        menu.Items.Add(copyProxy);

        menu.Items.Add(new MenuFlyoutSeparator());

        var copyAll = new MenuFlyoutItem
        {
            Text = LocalizationHelper.GetString("RulesCopyAll.Text")
        };
        copyAll.Click += (_, _) => CopyToClipboard($"{rule.Type}, {rule.Payload}, {rule.Proxy}");
        menu.Items.Add(copyAll);

        menu.Items.Add(new MenuFlyoutSeparator());

        var filterByType = new MenuFlyoutItem
        {
            Text = $"{LocalizationHelper.GetString("CommonFilter.Text")}: {rule.Type}"
        };
        filterByType.Click += (_, _) => ViewModel.SelectedTypeFilter = rule.Type;
        menu.Items.Add(filterByType);

        if (!string.IsNullOrEmpty(rule.Proxy))
        {
            var filterByProxy = new MenuFlyoutItem
            {
                Text = $"{LocalizationHelper.GetString("CommonFilter.Text")}: {rule.Proxy}"
            };
            filterByProxy.Click += (_, _) => ViewModel.SelectedProxyFilter = rule.Proxy;
            menu.Items.Add(filterByProxy);
        }

        menu.ShowAt(element, e.GetPosition(element));
    }

    private void RuleItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Rule rule })
            CopyToClipboard($"{rule.Type}, {rule.Payload}, {rule.Proxy}");
    }

    private static void CopyToClipboard(string text)
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }
}
