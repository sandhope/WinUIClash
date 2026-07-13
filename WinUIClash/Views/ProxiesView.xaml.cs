using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WinUIClash.Models;
using WinUIClash.ViewModels;

using WinUIClash.Services;

namespace WinUIClash.Views;

public sealed partial class ProxiesView : Page
{
    public ProxiesViewModel ViewModel { get; }

    /// <summary>上一次左键点击的代理节点（用于「点击态」中性色高亮）</summary>
    private Proxy? _lastClickedProxy;

    public ProxiesView()
    {
        ViewModel = ServiceLocator.Get<ProxiesViewModel>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try { await ViewModel.InitializeAsync(); }
            catch { /* 核心未运行或初始化出错时保持空状态，避免崩溃 */ }
        };

        var profiles = ServiceLocator.Get<ProfilesViewModel>();

        // 配置增删/切换后自动刷新代理列表（BUG-5：纯刷新，无新 UI）。
        // 无条件刷新：GetProxyGroupsAsync 走 SafeFetchAsync，核心未就绪时返回空、不抛异常、不弹错误。
        profiles.ProfilesChanged += (_, _) => _ = ViewModel.ReloadAsync();

        // 当前激活配置变化时实时刷新（例如配置页切换/删除当前配置，而代理页正打开时）
        profiles.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProfilesViewModel.ActiveProfile))
                _ = ViewModel.ReloadAsync();
        };

        // 当选中组变化时刷新高亮 + 清除上次点击态
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProxiesViewModel.SelectedGroup))
            {
                _lastClickedProxy = null;
                RefreshSelectionHighlights();
            }
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // 每次进入代理页都重新拉取当前激活配置的代理组（绕过 InitializeAsync 的 _initialized 守卫）。
        // 无条件刷新：核心未就绪时 GetProxyGroupsAsync 返回空，不会报错。
        _ = ViewModel.ReloadAsync();
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
        ApplyGroupTabState(btn, isSelected);

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

    /// <summary>对 GroupTab 按钮应用选中/未选中视觉状态。<br/>
    /// 未选中态不覆盖 Foreground/Background，让 Button 默认样式中的 {ThemeResource} 绑定
    /// 在深色/浅色主题间自动切换。<br/>
    /// 选中态同步覆盖 PointerOver/Pressed 背景资源，避免悬停跳变为白色。</summary>
    private void ApplyGroupTabState(Button btn, bool isSelected)
    {
        // 复用的透明画刷
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        if (isSelected)
        {
            btn.Background = TryGetAccentBrushAtOpacity(0.12);
            btn.Foreground = TryGetAccentBrush();
            btn.Resources["ButtonBackgroundPointerOver"] = TryGetAccentBrushAtOpacity(0.16);
            btn.Resources["ButtonBackgroundPressed"] = TryGetAccentBrushAtOpacity(0.08);
            btn.Resources["ButtonBorderBrushPointerOver"] = transparent;
            btn.Resources["ButtonBorderBrushPressed"] = transparent;
        }
        else
        {
            btn.ClearValue(Control.BackgroundProperty);
            btn.ClearValue(Control.ForegroundProperty);
            btn.Resources.Remove("ButtonBackgroundPointerOver");
            btn.Resources.Remove("ButtonBackgroundPressed");
            btn.Resources.Remove("ButtonBorderBrushPointerOver");
            btn.Resources.Remove("ButtonBorderBrushPressed");
        }
    }

    private void ProxyGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not Proxy proxy) return;

        // 左键点击仅标记选中态，不切换节点（切换由右键菜单完成）
        _lastClickedProxy = proxy;

        // 直接更新旧点击态和新点击态的容器，避免 ItemsSource 重置导致的抖动
        RefreshProxyCardVisuals();
    }

    private void ProxyGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer.ContentTemplateRoot is not Border cardBorder) return;
        if (args.Item is not Proxy proxy) return;

        var isActive = ViewModel.SelectedGroup?.Now == proxy.Name;
        var isClicked = ReferenceEquals(proxy, _lastClickedProxy);
        ApplyProxyCardState(cardBorder, proxy, isActive, isClicked);

        // Color the type dot based on protocol
        if (cardBorder.FindName("TypeDot") is Ellipse typeDot)
        {
            typeDot.Fill = GetProtocolBrush(proxy.Type);
        }
    }

    /// <summary>对指定卡片 Border 应用激活/默认视觉。<br/>
    /// Background 保留 XAML 中的 {ThemeResource} 绑定，不在此处覆盖，
    /// 确保深色/浅色主题切换时自动更新。仅调整 BorderBrush/BorderThickness 与选中图标。</summary>
    private void ApplyProxyCardState(Border cardBorder, Proxy proxy, bool isActive, bool isClicked)
    {
        if (isActive)
        {
            cardBorder.BorderBrush = TryGetAccentBrushAtOpacity(0.9);
            cardBorder.BorderThickness = new Thickness(1);
        }
        else
        {
            // 恢复 XAML 中定义的 ThemeResource 绑定，而非硬编码值
            cardBorder.ClearValue(Border.BorderBrushProperty);
            cardBorder.BorderThickness = new Thickness(1);
        }

        if (cardBorder.FindName("SelectedIcon") is FluentIcons.WinUI.SymbolIcon selectedIcon)
        {
            selectedIcon.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
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
            _ => TryGetAccentBrush(),
        };
    }

    /// <summary>
    /// 获取主题强调色画刷 — 优先从窗口级资源查找（用户自定义主题色），
    /// 找不到时回退到 Application 级（WinUI 默认蓝色）。
    /// </summary>
    private static Brush TryGetAccentBrush()
    {
        if (App.CurrentWindow?.Content is FrameworkElement root &&
            root.Resources.TryGetValue("AccentFillColorDefaultBrush", out var value) &&
            value is Brush windowBrush)
        {
            return windowBrush;
        }
        return (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
    }

    /// <summary>提取主题强调色的 Color 值。</summary>
    private static Color TryGetAccentColor()
    {
        if (TryGetAccentBrush() is SolidColorBrush solid)
            return solid.Color;
        return Microsoft.UI.Colors.Blue;
    }

    /// <summary>
    /// 获取带透明度的主题强调色画刷，用于边框 / 背景等弱化强调场景，
    /// 与侧边栏 NavigationViewItem 选中背景（12% 不透明度）保持一致。
    /// </summary>
    private static Brush TryGetAccentBrushAtOpacity(double opacity)
    {
        var c = TryGetAccentColor();
        return new SolidColorBrush(Color.FromArgb((byte)(c.A * opacity), c.R, c.G, c.B));
    }

    /// <summary>
    /// 直接迭代 GridView 已实现的容器来更新视觉状态，
    /// 避免重置 ItemsSource 造成的页面抖动。
    /// 未实现（不可见）的容器会在滚动时由 ContainerContentChanging 正确渲染。
    /// </summary>
    private void RefreshProxyCardVisuals()
    {
        for (int i = 0; i < ProxyGrid.Items.Count; i++)
        {
            var container = ProxyGrid.ContainerFromIndex(i) as GridViewItem;
            if (container?.ContentTemplateRoot is not Border cardBorder) continue;
            if (ProxyGrid.Items[i] is not Proxy proxy) continue;

            var isActive = ViewModel.SelectedGroup?.Now == proxy.Name;
            var isClicked = ReferenceEquals(proxy, _lastClickedProxy);
            ApplyProxyCardState(cardBorder, proxy, isActive, isClicked);
        }
    }

    /// <summary>
    /// 直接迭代 ItemsRepeater 已实现的元素来更新 Tab 高亮，
    /// 避免重置 ItemsSource 造成的抖动。
    /// </summary>
    private void RefreshGroupTabVisuals()
    {
        var repeater = GroupTabsRepeater;
        // ItemsSourceView.Count 可能不可用，用 ViewModel.Groups 兜底
        var count = ViewModel.Groups.Count;
        for (int i = 0; i < count; i++)
        {
            var element = repeater.TryGetElement(i);
            if (element is not Button btn) continue;

            var group = ViewModel.Groups[i];
            var isSelected = ViewModel.SelectedGroup?.Name == group.Name;
            ApplyGroupTabState(btn, isSelected);
        }
    }

    /// <summary>
    /// 刷新全部高亮（代理卡片 + Tab 栏），用于代理切换完成后的更新。
    /// 使用容器直更方式，不重置 ItemsSource。
    /// </summary>
    private void RefreshSelectionHighlights()
    {
        RefreshProxyCardVisuals();
        RefreshGroupTabVisuals();
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

        menu.ShowAt(border, e.GetPosition(border));
    }
}
