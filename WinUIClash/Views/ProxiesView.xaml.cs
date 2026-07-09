using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    }

    private void RefreshSelectionHighlights()
    {
        // 触发 GridView 重新渲染所有项
        var itemsSource = ProxyGrid.ItemsSource;
        ProxyGrid.ItemsSource = null;
        ProxyGrid.ItemsSource = itemsSource;
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

        var copyItem = new MenuFlyoutItem { Text = LocalizationHelper.GetString("ProxyCtxCopyName.Text") };
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
