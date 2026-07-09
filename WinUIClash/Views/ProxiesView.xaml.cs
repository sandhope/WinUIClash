using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WinUIClash.Models;
using WinUIClash.ViewModels;

using WinUIClash.Services;

namespace WinUIClash.Views;

public sealed partial class ProxiesView : Page
{
    public ProxiesViewModel ViewModel { get; }

    public ProxiesView()
    {
        ViewModel = ServiceLocator.Get<ProxiesViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();

        // 当选中组变化时刷新高亮
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProxiesViewModel.SelectedGroup))
                RefreshSelectionHighlights();
        };
    }

    private void GroupTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProxyGroup group)
            ViewModel.SelectGroupCommand.Execute(group);
    }

    private void GroupTabsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not Button btn) return;
        if (btn.Tag is not ProxyGroup group) return;

        var isSelected = ViewModel.SelectedGroup?.Name == group.Name;
        if (isSelected)
        {
            btn.Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            btn.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        }
        else
        {
            btn.Background = (Brush)Application.Current.Resources["SubtleFillColorSecondary"];
            btn.Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        }

        // Inject group type icon if not already present
        if (btn.Content is StackPanel panel && panel.Children.Count > 0)
        {
            if (panel.Children[0] is not FluentIcons.WinUI.SymbolIcon)
            {
                var symbol = group.Type switch
                {
                    ProxyGroupType.Selector => FluentIcons.Common.Symbol.TargetArrow,
                    ProxyGroupType.URLTest => FluentIcons.Common.Symbol.Gauge,
                    ProxyGroupType.Fallback => FluentIcons.Common.Symbol.ArrowReset,
                    ProxyGroupType.LoadBalance => FluentIcons.Common.Symbol.ArrowBidirectionalUpDown,
                    ProxyGroupType.Relay => FluentIcons.Common.Symbol.ArrowForward,
                    _ => FluentIcons.Common.Symbol.List,
                };
                var icon = new FluentIcons.WinUI.SymbolIcon { Symbol = symbol, FontSize = 12, Opacity = 0.6 };
                panel.Children.Insert(0, icon);
            }
        }
    }

    private void ProxyGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Proxy proxy)
            ViewModel.SelectProxyCommand.Execute(proxy);
    }

    private void ProxyGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer.ContentTemplateRoot is not Border cardBorder) return;
        if (args.Item is not Proxy proxy) return;

        var isSelected = ViewModel.SelectedGroup?.Now == proxy.Name;

        // 更新边框颜色
        if (isSelected)
        {
            cardBorder.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            cardBorder.BorderThickness = new Thickness(2);
        }
        else
        {
            cardBorder.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
            cardBorder.BorderThickness = new Thickness(1);
        }

        // 更新选中标记图标
        if (cardBorder.FindName("SelectedIcon") is FluentIcons.WinUI.SymbolIcon selectedIcon)
        {
            selectedIcon.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        // Color the type dot based on protocol
        if (cardBorder.FindName("TypeDot") is Ellipse typeDot)
        {
            typeDot.Fill = GetProtocolBrush(proxy.Type);
        }
    }

    private static Brush GetProtocolBrush(string type)
    {
        var t = type.ToLowerInvariant();
        return t switch
        {
            "direct" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80)),    // Green
            "reject" or "reject-drop" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 67, 54)), // Red
            "vmess" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 33, 150, 243)),    // Blue
            "trojan" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 39, 176)),   // Purple
            "shadowsocks" or "ss" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 152, 0)), // Orange
            "hysteria" or "hysteria2" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 188, 212)), // Cyan
            "vless" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 63, 81, 181)),     // Indigo
            "tuic" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 233, 30, 99)),      // Pink
            "wireguard" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 139, 195, 74)), // Light green
            _ => (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
        };
    }

    private void RefreshSelectionHighlights()
    {
        // 触发 GridView 重新渲染所有项
        var itemsSource = ProxyGrid.ItemsSource;
        ProxyGrid.ItemsSource = null;
        ProxyGrid.ItemsSource = itemsSource;

        // 刷新代理组 Tab 高亮
        var tabSource = GroupTabsRepeater.ItemsSource;
        GroupTabsRepeater.ItemsSource = null;
        GroupTabsRepeater.ItemsSource = tabSource;
    }

    private async void ProxyCard_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Proxy proxy } border) return;

        var menu = new MenuFlyout();

        var testItem = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProxyCtxTestDelay.Text") };
        testItem.Click += async (_, _) =>
        {
            await ViewModel.TestDelayCommand.ExecuteAsync(proxy);
            RefreshSelectionHighlights();
        };
        menu.Items.Add(testItem);

        var selectItem = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProxyCtxSelect.Text") };
        selectItem.Click += async (_, _) =>
        {
            await ViewModel.SelectProxyCommand.ExecuteAsync(proxy);
            RefreshSelectionHighlights();
        };
        menu.Items.Add(selectItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        // Proxy info header (non-clickable)
        var delayText = proxy.Delay > 0 ? $"{proxy.Delay}ms" : proxy.Delay == 0 ? "N/A" : "—";
        var infoItem = new MenuFlyoutItem
        {
            Text = $"{proxy.Name}  |  {proxy.Type}  |  {delayText}",
            IsEnabled = false,
        };
        menu.Items.Add(infoItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var copyNameItem = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProxyCtxCopyName.Text") };
        copyNameItem.Click += (_, _) => CopyToClipboard(proxy.Name);
        menu.Items.Add(copyNameItem);

        var copyTypeItem = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProxyCtxCopyType.Text") };
        copyTypeItem.Click += (_, _) => CopyToClipboard(proxy.Type);
        menu.Items.Add(copyTypeItem);

        var copyAllItem = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProxyCtxCopyAll.Text") };
        copyAllItem.Click += (_, _) => CopyToClipboard($"{proxy.Name}\n{proxy.Type}\n{delayText}");
        menu.Items.Add(copyAllItem);

        menu.ShowAt(border, e.GetPosition(border));
    }

    private static void CopyToClipboard(string text)
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }
}
