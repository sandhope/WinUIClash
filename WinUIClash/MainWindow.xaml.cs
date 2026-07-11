using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash;

/// <summary>
/// 主窗口：侧边栏导航 + 页面内容区域 + 系统托盘
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Tag 值 → 页面类型映射，供导航回调使用
    /// </summary>
    private static readonly Dictionary<string, Type> PageMap = new()
    {
        ["Dashboard"]   = typeof(Views.DashboardView),
        ["Proxies"]     = typeof(Views.ProxiesView),
        ["Profiles"]    = typeof(Views.ProfilesView),
        ["Requests"]    = typeof(Views.RequestsView),
        ["Connections"] = typeof(Views.ConnectionsView),
        ["Resources"]   = typeof(Views.ResourcesView),
        ["Rules"]       = typeof(Views.RulesView),
        ["Logs"]        = typeof(Views.LogsView),
        ["Tools"]       = typeof(Views.ToolsView),
    };

    private TaskbarIcon? _trayIcon;
    private bool _isExiting;
    private bool _cleanedUp;
    private readonly DispatcherQueue _dispatcher;

    // 状态栏引用的服务
    private IClashService? _clash;
    private AppSettings? _appSettings;

    // 托盘菜单项（需要动态更新状态）
    private ToggleMenuFlyoutItem? _trayProxyItem;
    private ToggleMenuFlyoutItem? _trayRunItem;
    private MenuFlyoutSubItem? _trayProfileMenu;

    // 状态栏连接数轮询定时器
    private DispatcherTimer? _statusBarConnTimer;
    private DispatcherTimer? _runtimeTimer;
    private DateTime? _coreStartTime;
    private Traffic _lastTraffic = new();
    private int _lastConnectionCount;

    public MainWindow()
    {
        InitializeComponent();

        _dispatcher = DispatcherQueue.GetForCurrentThread()!;

        // 窗口基础配置
        Title = "WinUIClash";
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // 接管标题栏：将内容延伸到非客户区并指定自定义标题栏，
        // 使其跟随应用明暗主题（修复深色模式下标题栏仍为浅色的问题），
        // 同时与左侧导航栏在视觉上融为一体。
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // 从设置恢复窗口状态
        RestoreWindowState();

        // NavigationView 加载完成后默认选中「仪表盘」
        RootNavigation.Loaded += (_, _) =>
        {
            if (RootNavigation.MenuItems.FirstOrDefault() is NavigationViewItem first)
            {
                RootNavigation.SelectedItem = first;
            }
        };

        // 初始化系统托盘
        InitTrayIcon();

        // 订阅状态栏数据源
        InitStatusBar();

        // 键盘快捷键
        InitKeyboardShortcuts();

        // 活跃配置名显示在标题栏
        try
        {
            var profilesVm = ServiceLocator.Get<ViewModels.ProfilesViewModel>();
            profilesVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ViewModels.ProfilesViewModel.ActiveProfile))
                    UpdateWindowTitle();
            };
        }
        catch { }

        // 注册通知服务
        try
        {
            ServiceLocator.Get<Services.NotificationService>().Register(NotificationBar);
        }
        catch { }

        // 拦截窗口关闭事件
        Closed += OnWindowClosed;
    }

    private void RestoreWindowState()
    {
        try
        {
            var settings = ServiceLocator.Get<AppSettings>();

            // 恢复窗口大小
            AppWindow.Resize(new Windows.Graphics.SizeInt32(
                settings.WindowWidth, settings.WindowHeight));

            // 恢复窗口位置（如果之前保存过）
            if (settings.WindowX != 0 || settings.WindowY != 0)
            {
                AppWindow.Move(new Windows.Graphics.PointInt32(
                    settings.WindowX, settings.WindowY));
            }

            // 恢复侧边栏状态
            if (settings.IsSidebarCompact)
            {
                RootNavigation.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
            }

            // 恢复最大化状态
            if (settings.IsMaximized && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }
        catch { /* ServiceLocator 未初始化时忽略 */ }
    }

    private void SaveWindowState()
    {
        try
        {
            var settings = ServiceLocator.Get<AppSettings>();
            var presenter = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;

            // 保存最大化状态
            settings.IsMaximized = presenter?.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;

            // 只在 Normal 状态下保存尺寸和位置
            if (presenter?.State == Microsoft.UI.Windowing.OverlappedPresenterState.Restored)
            {
                settings.WindowWidth = AppWindow.Size.Width;
                settings.WindowHeight = AppWindow.Size.Height;
                settings.WindowX = AppWindow.Position.X;
                settings.WindowY = AppWindow.Position.Y;
            }

            settings.IsSidebarCompact =
                RootNavigation.PaneDisplayMode == NavigationViewPaneDisplayMode.LeftCompact;

            // 立即保存到磁盘
            var settingsService = ServiceLocator.Get<Services.SettingsService>();
            settingsService.SaveImmediate();
        }
        catch { /* 保存失败时静默 */ }
    }

    // ── 导航事件 ──────────────────────────────────────────────────────────────

    private void RootNavigation_SelectionChanged(
        NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            NavigateTo(item.Tag as string);
        }
    }

    public void NavigateTo(string? tag)
    {
        if (tag is null) return;
        if (PageMap.TryGetValue(tag, out var pageType))
        {
            ContentFrame.Navigate(pageType);
        }
    }

    public string? GetCurrentNavigationTag()
    {
        return RootNavigation.SelectedItem is NavigationViewItem item ? item.Tag as string : null;
    }

    // ── 侧边栏紧凑/展开切换 ───────────────────────────────────────────────────

    private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        RootNavigation.PaneDisplayMode =
            RootNavigation.PaneDisplayMode == NavigationViewPaneDisplayMode.Left
                ? NavigationViewPaneDisplayMode.LeftCompact
                : NavigationViewPaneDisplayMode.Left;
    }

    // ── 键盘快捷键 ──────────────────────────────────────────────────────────────

    private void InitKeyboardShortcuts()
    {
        // Ctrl+1~9 导航到对应页面
        var pages = new[] { "Dashboard", "Proxies", "Profiles", "Requests",
                            "Connections", "Resources", "Rules", "Logs", "Tools" };

        for (int i = 0; i < pages.Length && i < 9; i++)
        {
            var tag = pages[i];
            var accel = new KeyboardAccelerator
            {
                Key = (Windows.System.VirtualKey)((int)Windows.System.VirtualKey.Number1 + i),
                Modifiers = Windows.System.VirtualKeyModifiers.Control,
            };
            accel.Invoked += (_, _) =>
            {
                NavigateTo(tag);
                // 同步选中侧边栏
                if (i < RootNavigation.MenuItems.Count)
                    RootNavigation.SelectedItem = RootNavigation.MenuItems[i];
            };
            RootGrid.KeyboardAccelerators.Add(accel);
        }

        // Ctrl+F 聚焦搜索框
        var searchAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.F,
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        };
        searchAccel.Invoked += (_, _) =>
        {
            SearchBox.Focus(FocusState.Keyboard);
        };
        RootGrid.KeyboardAccelerators.Add(searchAccel);

        // Ctrl+Q 退出
        var quitAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.Q,
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        };
        quitAccel.Invoked += (_, _) => ExitApp();
        RootGrid.KeyboardAccelerators.Add(quitAccel);

        // Ctrl+B 切换侧边栏
        var sidebarAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.B,
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        };
        sidebarAccel.Invoked += (_, _) =>
        {
            RootNavigation.PaneDisplayMode =
                RootNavigation.PaneDisplayMode == NavigationViewPaneDisplayMode.Left
                    ? NavigationViewPaneDisplayMode.LeftCompact
                    : NavigationViewPaneDisplayMode.Left;
        };
        RootGrid.KeyboardAccelerators.Add(sidebarAccel);

        // F5 刷新当前页面
        var refreshAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.F5,
        };
        refreshAccel.Invoked += (_, _) => RefreshCurrentPage();
        RootGrid.KeyboardAccelerators.Add(refreshAccel);

        // Ctrl+R 刷新当前页面
        var refreshCtrlAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.R,
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        };
        refreshCtrlAccel.Invoked += (_, _) => RefreshCurrentPage();
        RootGrid.KeyboardAccelerators.Add(refreshCtrlAccel);

        // Ctrl+W 最小化到托盘
        var minimizeAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.W,
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        };
        minimizeAccel.Invoked += (_, _) =>
        {
            SaveWindowState();
            this.Hide();
        };
        RootGrid.KeyboardAccelerators.Add(minimizeAccel);

        // Ctrl+Shift+S 切换系统代理
        var proxyToggleAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.S,
            Modifiers = Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift,
        };
        proxyToggleAccel.Invoked += (_, _) =>
        {
            if (_appSettings != null)
                _appSettings.SystemProxy = !_appSettings.SystemProxy;
        };
        RootGrid.KeyboardAccelerators.Add(proxyToggleAccel);

        // Ctrl+P 切换核心启动/停止
        var coreToggleAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.P,
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        };
        coreToggleAccel.Invoked += async (_, _) =>
        {
            if (_clash == null) return;
            if (_clash.CoreState == CoreState.Running)
                await _clash.StopAsync();
            else if (_clash.CoreState == CoreState.Stopped)
                await _clash.StartAsync();
        };
        RootGrid.KeyboardAccelerators.Add(coreToggleAccel);

        // F1 快捷键帮助
        var helpAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.F1,
        };
        helpAccel.Invoked += async (_, _) => await ShowKeyboardHelpAsync();
        RootGrid.KeyboardAccelerators.Add(helpAccel);

        // Ctrl+Tab 下一个页面
        var nextAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.Tab,
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        };
        nextAccel.Invoked += (_, _) => CyclePage(1);
        RootGrid.KeyboardAccelerators.Add(nextAccel);

        // Ctrl+Shift+Tab 上一个页面
        var prevAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.Tab,
            Modifiers = Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift,
        };
        prevAccel.Invoked += (_, _) => CyclePage(-1);
        RootGrid.KeyboardAccelerators.Add(prevAccel);

        // Escape 清除搜索框或关闭通知栏
        var escAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.Escape,
        };
        escAccel.Invoked += (_, _) =>
        {
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = "";
            }
            else if (NotificationBar.IsOpen)
            {
                NotificationBar.IsOpen = false;
            }
        };
        RootGrid.KeyboardAccelerators.Add(escAccel);

        // Ctrl+Shift+T 切换明暗主题
        var themeAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.T,
            Modifiers = Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift,
        };
        themeAccel.Invoked += (_, _) =>
        {
            try
            {
                var themeVm = ServiceLocator.Get<ViewModels.Settings.ThemeSettingsViewModel>();
                // Toggle between Light and Dark (skip System)
                var newMode = RootGrid.ActualTheme == ElementTheme.Dark ? "Light" : "Dark";
                themeVm.ThemeMode = newMode;
            }
            catch { }
        };
        RootGrid.KeyboardAccelerators.Add(themeAccel);

        // Ctrl+Shift+D 关闭所有连接
        var closeAllAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.D,
            Modifiers = Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift,
        };
        closeAllAccel.Invoked += async (_, _) =>
        {
            try
            {
                var clash = ServiceLocator.Get<IClashService>();
                if (clash.CoreState == CoreState.Running)
                    await clash.CloseAllConnectionsAsync();
            }
            catch { }
        };
        RootGrid.KeyboardAccelerators.Add(closeAllAccel);

        // Ctrl+E 导出当前页面数据
        var exportAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.E,
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        };
        exportAccel.Invoked += async (_, _) =>
        {
            try
            {
                var currentPage = RootNavigation.SelectedItem is NavigationViewItem item ? item.Tag as string : null;
                switch (currentPage)
                {
                    case "connections":
                        var connVm = ServiceLocator.Get<ViewModels.ConnectionsViewModel>();
                        await connVm.ExportCommand.ExecuteAsync(null);
                        break;
                    case "requests":
                        var reqVm = ServiceLocator.Get<ViewModels.RequestsViewModel>();
                        await reqVm.ExportCommand.ExecuteAsync(null);
                        break;
                    case "logs":
                        var logVm = ServiceLocator.Get<ViewModels.LogsViewModel>();
                        await logVm.ExportCommand.ExecuteAsync(null);
                        break;
                }
            }
            catch { }
        };
        RootGrid.KeyboardAccelerators.Add(exportAccel);

        // Ctrl+, 打开设置
        var settingsAccel = new KeyboardAccelerator
        {
            Key = (Windows.System.VirtualKey)188, // VK_OEM_COMMA
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        };
        settingsAccel.Invoked += (_, _) =>
        {
            try
            {
                // Navigate to Tools page
                var toolsItem = RootNavigation.MenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(i => i.Tag is string t && t == "tools");
                if (toolsItem != null)
                {
                    RootNavigation.SelectedItem = toolsItem;
                    NavigateTo("tools");
                }
            }
            catch { }
        };
        RootGrid.KeyboardAccelerators.Add(settingsAccel);
    }

    private void CyclePage(int direction)
    {
        var items = RootNavigation.MenuItems;
        if (items.Count == 0) return;
        var currentIdx = RootNavigation.SelectedItem is NavigationViewItem current
            ? items.IndexOf(current)
            : -1;
        var nextIdx = (currentIdx + direction + items.Count) % items.Count;
        RootNavigation.SelectedItem = items[nextIdx];
        if (items[nextIdx] is NavigationViewItem nextItem && nextItem.Tag is string tag)
            NavigateTo(tag);
    }

    private async Task ShowKeyboardHelpAsync()
    {
        var shortcuts = new (string Key, string Desc)[]
        {
            ("Ctrl+1~9", LocalizationHelper.GetString("HelpNav.Text")),
            ("Ctrl+Tab", LocalizationHelper.GetString("HelpCyclePage.Text")),
            ("Ctrl+F", LocalizationHelper.GetString("HelpSearch.Text")),
            ("F5 / Ctrl+R", LocalizationHelper.GetString("HelpRefresh.Text")),
            ("Ctrl+B", LocalizationHelper.GetString("HelpSidebar.Text")),
            ("Ctrl+W", LocalizationHelper.GetString("HelpMinimize.Text")),
            ("Ctrl+P", LocalizationHelper.GetString("HelpCoreToggle.Text")),
            ("Ctrl+Shift+S", LocalizationHelper.GetString("HelpProxyToggle.Text")),
            ("Ctrl+Shift+T", LocalizationHelper.GetString("HelpThemeToggle.Text")),
            ("Ctrl+Shift+D", LocalizationHelper.GetString("HelpCloseAllConns.Text")),
            ("Ctrl+E", LocalizationHelper.GetString("HelpExport.Text")),
            ("Ctrl+,", LocalizationHelper.GetString("HelpSettings.Text")),
            ("Ctrl+Q", LocalizationHelper.GetString("HelpQuit.Text")),
            ("Escape", LocalizationHelper.GetString("HelpEscape.Text")),
            ("F1", LocalizationHelper.GetString("HelpShowHelp.Text")),
        };

        var panel = new StackPanel { Spacing = 6, MinWidth = 360 };
        foreach (var (key, desc) in shortcuts)
        {
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var keyText = new TextBlock
            {
                Text = key,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(keyText, 0);

            var descText = new TextBlock
            {
                Text = desc,
                FontSize = 12,
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(descText, 1);

            row.Children.Add(keyText);
            row.Children.Add(descText);
            panel.Children.Add(row);
        }

        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.GetString("HelpTitle.Text"),
            XamlRoot = (this.Content as FrameworkElement)?.XamlRoot,
            CloseButtonText = LocalizationHelper.GetString("CommonClose.Content"),
            Content = panel,
        };

        await dialog.ShowAsync();
    }

    private void RefreshCurrentPage()
    {
        if (ContentFrame.CurrentSourcePageType is { } pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    // ── 搜索 ──────────────────────────────────────────────────────────────────

    private record SearchSuggestion(string Title, string Tag, string Category);

    private SearchSuggestion[]? _allSuggestions;
    private SearchSuggestion[] AllSuggestions => _allSuggestions ??=
    [
        new(LocalizationHelper.GetString("NavDashboard.Content"), "Dashboard", LocalizationHelper.GetString("SearchCategoryPage.Text")),
        new(LocalizationHelper.GetString("NavProxies.Content"), "Proxies", LocalizationHelper.GetString("SearchCategoryPage.Text")),
        new(LocalizationHelper.GetString("NavProfiles.Content"), "Profiles", LocalizationHelper.GetString("SearchCategoryPage.Text")),
        new(LocalizationHelper.GetString("NavRequests.Content"), "Requests", LocalizationHelper.GetString("SearchCategoryPage.Text")),
        new(LocalizationHelper.GetString("NavConnections.Content"), "Connections", LocalizationHelper.GetString("SearchCategoryPage.Text")),
        new(LocalizationHelper.GetString("NavResources.Content"), "Resources", LocalizationHelper.GetString("SearchCategoryPage.Text")),
        new(LocalizationHelper.GetString("NavRules.Content"), "Rules", LocalizationHelper.GetString("SearchCategoryPage.Text")),
        new(LocalizationHelper.GetString("NavLogs.Content"), "Logs", LocalizationHelper.GetString("SearchCategoryPage.Text")),
        new(LocalizationHelper.GetString("NavTools.Content"), "Tools", LocalizationHelper.GetString("SearchCategoryPage.Text")),
        new(LocalizationHelper.GetString("SettingsLanguage.Text"), "Tools|LanguageSettings", LocalizationHelper.GetString("SearchCategorySettings.Text")),
        new(LocalizationHelper.GetString("SettingsBasicConfig.Text"), "Tools|BasicConfig", LocalizationHelper.GetString("SearchCategorySettings.Text")),
        new(LocalizationHelper.GetString("SettingsTheme.Text"), "Tools|ThemeSettings", LocalizationHelper.GetString("SearchCategorySettings.Text")),
        new(LocalizationHelper.GetString("SettingsApp.Text"), "Tools|AppSettings", LocalizationHelper.GetString("SearchCategorySettings.Text")),
        new(LocalizationHelper.GetString("SettingsAbout.Text"), "Tools|About", LocalizationHelper.GetString("SearchCategorySettings.Text")),
    ];

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        var query = sender.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            sender.ItemsSource = null;
            return;
        }

        var matches = AllSuggestions
            .Where(s => s.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || s.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(s => $"{s.Category} › {s.Title}")
            .ToList();

        sender.ItemsSource = matches;
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        var chosen = args.SelectedItem?.ToString() ?? "";
        // 解析 "类别 › 名称" 格式
        var parts = chosen.Split(" › ", 2);
        if (parts.Length < 2) return;

        var category = parts[0];
        var title = parts[1];

        var match = AllSuggestions.FirstOrDefault(s => s.Title == title);
        if (match is null) return;

        if (match.Tag.Contains('|'))
        {
            // 设置子页面: 先导航到 Tools，再打开子页面
            var segments = match.Tag.Split('|');
            NavigateTo(segments[0]);
            // 同步侧边栏
            var toolIndex = PageMap.Keys.ToList().IndexOf(segments[0]);
            if (toolIndex >= 0 && toolIndex < RootNavigation.MenuItems.Count)
                RootNavigation.SelectedItem = RootNavigation.MenuItems[toolIndex];

            // 延迟发送子页面导航消息
            _dispatcher.TryEnqueue(() =>
            {
                try
                {
                    var toolsVm = ServiceLocator.Get<ViewModels.ToolsViewModel>();
                    var pageKey = segments[1]; // "BasicConfig", "ThemeSettings" 等
                    if (!string.IsNullOrEmpty(pageKey))
                    {
                        toolsVm.OpenSettingCommand.Execute(pageKey);
                        // ToolsView 需要在 code-behind 中同步 Content
                        if (ContentFrame.Content is Views.ToolsView toolsView)
                        {
                            toolsView.SyncSubPage(toolsVm.CurrentPage);
                        }
                    }
                }
                catch { }
            });
        }
        else
        {
            NavigateTo(match.Tag);
            var index = PageMap.Keys.ToList().IndexOf(match.Tag);
            if (index >= 0 && index < RootNavigation.MenuItems.Count)
                RootNavigation.SelectedItem = RootNavigation.MenuItems[index];
        }

        sender.Text = "";
    }

    // ── 窗口标题 ────────────────────────────────────────────────────────────

    private void UpdateWindowTitle()
    {
        try
        {
            var profilesVm = ServiceLocator.Get<ViewModels.ProfilesViewModel>();
            var label = profilesVm.ActiveProfile?.Label;
            var text = string.IsNullOrEmpty(label) ? "WinUIClash" : $"WinUIClash — {label}";
            AppTitleBar.Title = text;
            Title = text;
        }
        catch
        {
            AppTitleBar.Title = "WinUIClash";
            Title = "WinUIClash";
        }
    }

    // ── 状态栏 ──────────────────────────────────────────────────────────────

    private void InitStatusBar()
    {
        try
        {
            _clash = ServiceLocator.Get<IClashService>();
            _appSettings = ServiceLocator.Get<AppSettings>();

            // 订阅核心状态变化
            _clash.CoreStateChanged += OnCoreStateChanged;
            UpdateCoreStatusUI(_clash.CoreState);

            // 订阅实时流量
            _clash.TrafficUpdated += OnTrafficUpdated;

            // 订阅系统代理设置变化
            _appSettings.PropertyChanged += OnSettingsPropertyChanged;
            UpdateProxyStatusUI(_appSettings.SystemProxy);

            // 订阅出站模式变化
            _clash.OutboundModeChanged += OnOutboundModeChanged;
            UpdateOutboundModeUI(_clash.GetOutboundMode());

            // 连接数轮询（每5秒）
            _statusBarConnTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5),
            };
            _statusBarConnTimer.Tick += async (_, _) =>
            {
                try
                {
                    var connections = await _clash.GetConnectionsAsync();
                    _lastConnectionCount = connections.Count;
                    ConnectionCountText.Text = connections.Count.ToString();
                    UpdateTrayTooltip();

                    // Update navigation badge for connections
                    if (connections.Count > 0)
                    {
                        ConnectionsBadge.Value = connections.Count;
                        ConnectionsBadge.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        ConnectionsBadge.Visibility = Visibility.Collapsed;
                    }
                }
                catch
                {
                    _lastConnectionCount = 0;
                    ConnectionCountText.Text = "0";
                    ConnectionsBadge.Visibility = Visibility.Collapsed;
                }

                // Update memory usage
                try
                {
                    var memBytes = await _clash.GetCoreMemoryAsync();
                    MemoryUsageText.Text = Converters.ByteFormatter.Format(memBytes);
                }
                catch
                {
                    MemoryUsageText.Text = "--";
                }
            };
            _statusBarConnTimer.Start();

            // Subscribe to request count changes for the requests badge
            try
            {
                var requestsVm = ServiceLocator.Get<ViewModels.RequestsViewModel>();
                requestsVm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ViewModels.RequestsViewModel.RequestCount))
                    {
                        _dispatcher.TryEnqueue(() =>
                        {
                            var count = requestsVm.RequestCount;
                            if (count > 0)
                            {
                                RequestsBadge.Value = count;
                                RequestsBadge.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                RequestsBadge.Visibility = Visibility.Collapsed;
                            }
                        });
                    }
                };
            }
            catch { }
        }
        catch { /* ServiceLocator 未初始化时忽略 */ }
    }

    private void OnCoreStateChanged(CoreState state)
    {
        _dispatcher.TryEnqueue(() => UpdateCoreStatusUI(state));
    }

    private void UpdateCoreStatusUI(CoreState state)
    {
        StatusDot.Fill = state switch
        {
            CoreState.Running  => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80)),
            CoreState.Starting => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 193, 7)),
            CoreState.Stopping => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 152, 0)),
            _                  => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 107, 107)),
        };

        StatusText.Text = state switch
        {
            CoreState.Running  => Services.LocalizationHelper.GetString("DashRunning.Text"),
            CoreState.Starting => Services.LocalizationHelper.GetString("DashStarting.Text"),
            CoreState.Stopping => Services.LocalizationHelper.GetString("DashStopping.Text"),
            _                  => Services.LocalizationHelper.GetString("DashStopped.Text"),
        };

        StatusText.Opacity = state == CoreState.Running ? 0.8 : 0.6;

        // 同步托盘菜单的核心开关状态
        if (_trayRunItem != null)
            _trayRunItem.IsChecked = state == CoreState.Running;

        // 运行时长计时器
        if (state == CoreState.Running)
        {
            _coreStartTime = DateTime.Now;
            StartRuntimeTimer();
        }
        else
        {
            _coreStartTime = null;
            StopRuntimeTimer();
            RuntimeText.Text = "";
        }

        UpdateTrayTooltip();
    }

    private void StartRuntimeTimer()
    {
        _runtimeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _runtimeTimer.Tick -= UpdateRuntimeDisplay;
        _runtimeTimer.Tick += UpdateRuntimeDisplay;
        _runtimeTimer.Start();
        UpdateRuntimeDisplay(null, null);
    }

    private void StopRuntimeTimer()
    {
        _runtimeTimer?.Stop();
    }

    private void UpdateRuntimeDisplay(object? sender, object? e)
    {
        if (_coreStartTime == null) return;
        var elapsed = DateTime.Now - _coreStartTime.Value;
        RuntimeText.Text = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:D2}";
    }

    private void OnTrafficUpdated(Traffic t)
    {
        _dispatcher.TryEnqueue(() =>
        {
            _lastTraffic = t;
            UploadSpeedText.Text = Converters.ByteFormatter.FormatSpeed(t.Up);
            DownloadSpeedText.Text = Converters.ByteFormatter.FormatSpeed(t.Down);
            UpdateTrayTooltip();
        });
    }

    private void OnOutboundModeChanged(OutboundMode mode)
    {
        _dispatcher.TryEnqueue(() => UpdateOutboundModeUI(mode));
    }

    private void UpdateOutboundModeUI(OutboundMode mode)
    {
        OutboundModeText.Text = mode switch
        {
            OutboundMode.Rule   => LocalizationHelper.GetString("StatusModeRule.Text"),
            OutboundMode.Global => LocalizationHelper.GetString("StatusModeGlobal.Text"),
            OutboundMode.Direct => LocalizationHelper.GetString("StatusModeDirect.Text"),
            _ => "Rule",
        };
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.SystemProxy))
        {
            try
            {
                var settings = ServiceLocator.Get<AppSettings>();
                _dispatcher.TryEnqueue(() =>
                {
                    UpdateProxyStatusUI(settings.SystemProxy);
                });
            }
            catch { }
        }
    }

    private void UpdateProxyStatusUI(bool isProxyOn)
    {
        ProxyIcon.Opacity = isProxyOn ? 1.0 : 0.3;
        ProxyText.Opacity = isProxyOn ? 0.8 : 0.3;
        ProxyText.Text = isProxyOn
            ? Services.LocalizationHelper.GetString("StatusRunning.Text")
            : Services.LocalizationHelper.GetString("ProxyLabel.Text");

        // 同步托盘菜单
        if (_trayProxyItem != null)
            _trayProxyItem.IsChecked = isProxyOn;

        UpdateTrayTooltip();
    }

    private System.Drawing.Icon? _currentTrayIcon;
    private CoreState _lastTrayIconState = CoreState.Stopped;

    private void UpdateTrayTooltip()
    {
        if (_trayIcon == null) return;

        var state = _clash?.CoreState ?? CoreState.Stopped;
        var stateText = state switch
        {
            CoreState.Running => LocalizationHelper.GetString("DashRunning.Text"),
            CoreState.Starting => LocalizationHelper.GetString("DashStarting.Text"),
            CoreState.Stopping => LocalizationHelper.GetString("DashStopping.Text"),
            _ => LocalizationHelper.GetString("DashStopped.Text"),
        };

        var up = Converters.ByteFormatter.FormatSpeed(_lastTraffic.Up);
        var down = Converters.ByteFormatter.FormatSpeed(_lastTraffic.Down);
        var proxy = _appSettings?.SystemProxy == true ? "ON" : "OFF";
        var tun = _appSettings?.TunMode == true ? "ON" : "OFF";

        _trayIcon.ToolTipText = $"WinUIClash\n{stateText}\n↑{up}  ↓{down}\n{LocalizationHelper.GetString("ConnCountSuffix.Text").Trim()}: {_lastConnectionCount}\n{LocalizationHelper.GetString("ProxyLabel.Text")}: {proxy}\nTUN: {tun}";

        // Append active profile name if available
        try
        {
            var profilesVm = ServiceLocator.Get<ViewModels.ProfilesViewModel>();
            if (profilesVm.ActiveProfile != null)
                _trayIcon.ToolTipText += $"\n{LocalizationHelper.GetString("NavProfiles.Content")}: {profilesVm.ActiveProfile.Label}";
        }
        catch { }

        // Update tray icon color based on core state
        if (state != _lastTrayIconState)
        {
            _lastTrayIconState = state;
            var iconColor = state switch
            {
                CoreState.Running => System.Drawing.Color.FromArgb(76, 175, 80),   // Green
                CoreState.Starting => System.Drawing.Color.FromArgb(255, 193, 7),  // Yellow
                CoreState.Stopping => System.Drawing.Color.FromArgb(255, 152, 0),  // Orange
                _ => System.Drawing.Color.FromArgb(255, 107, 107),                 // Red
            };
            var oldIcon = _currentTrayIcon;
            _currentTrayIcon = CreateTrayIcon(iconColor);
            _trayIcon.Icon = _currentTrayIcon;
            oldIcon?.Dispose();
        }
    }

    private async void StatusDot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var clash = ServiceLocator.Get<IClashService>();
            if (clash.CoreState == CoreState.Running)
                await clash.StopAsync();
            else if (clash.CoreState == CoreState.Stopped)
                await clash.StartAsync();
        }
        catch (Exception ex)
        {
            try
            {
                ServiceLocator.Get<Services.NotificationService>().Error(
                    Services.LocalizationHelper.GetString("AppErrorTitle.Text"),
                    ex.Message);
            }
            catch { }
        }
    }

    private void StatusProxy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = ServiceLocator.Get<AppSettings>();
            settings.SystemProxy = !settings.SystemProxy;
        }
        catch (Exception ex) { Debug.WriteLine($"Status proxy toggle error: {ex.Message}"); }
    }

    // ── 系统托盘 ──────────────────────────────────────────────────────────────

    private void InitTrayIcon()
    {
        // Start with red (stopped) icon
        _currentTrayIcon = CreateTrayIcon(System.Drawing.Color.FromArgb(255, 107, 107));
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "WinUIClash",
            Icon = _currentTrayIcon,
        };

        // 双击托盘图标 → 显示窗口
        _trayIcon.DoubleClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ShowWindow);

        // 右键菜单
        var menu = new MenuFlyout();

        // ── 显示主窗口 ──
        var showItem = new MenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TrayShow.Text") };
        showItem.Click += (_, _) => ShowWindow();
        menu.Items.Add(showItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        // ── 核心开关 ──
        _trayRunItem = new ToggleMenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TrayRun.Text") };
        _trayRunItem.Click += async (_, _) =>
        {
            try
            {
                var clash = ServiceLocator.Get<IClashService>();
                if (_trayRunItem.IsChecked)
                    await clash.StartAsync();
                else
                    await clash.StopAsync();
            }
            catch (Exception ex) { Debug.WriteLine($"Tray core toggle error: {ex.Message}"); }
        };
        menu.Items.Add(_trayRunItem);

        // ── 系统代理 ──
        _trayProxyItem = new ToggleMenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TraySystemProxy.Text") };
        try
        {
            var settings = ServiceLocator.Get<AppSettings>();
            _trayProxyItem.IsChecked = settings.SystemProxy;
        }
        catch (Exception ex) { Debug.WriteLine($"Tray proxy init error: {ex.Message}"); }
        _trayProxyItem.Click += (_, _) =>
        {
            try
            {
                var settings = ServiceLocator.Get<AppSettings>();
                settings.SystemProxy = _trayProxyItem.IsChecked;
            }
            catch (Exception ex) { Debug.WriteLine($"Tray proxy toggle error: {ex.Message}"); }
        };
        menu.Items.Add(_trayProxyItem);

        // ── TUN 模式 ──
        var trayTunItem = new ToggleMenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TrayTunMode.Text") };
        try
        {
            var tunSettings = ServiceLocator.Get<AppSettings>();
            trayTunItem.IsChecked = tunSettings.TunMode;
        }
        catch (Exception ex) { Debug.WriteLine($"Tray TUN init error: {ex.Message}"); }
        trayTunItem.Click += (_, _) =>
        {
            try
            {
                var tunSettings = ServiceLocator.Get<AppSettings>();
                tunSettings.TunMode = trayTunItem.IsChecked;
            }
            catch (Exception ex) { Debug.WriteLine($"Tray TUN toggle error: {ex.Message}"); }
        };
        // Sync TUN state from settings changes
        try
        {
            var tunAppSettings = ServiceLocator.Get<AppSettings>();
            tunAppSettings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.TunMode))
                    _dispatcher.TryEnqueue(() => trayTunItem.IsChecked = tunAppSettings.TunMode);
            };
        }
        catch (Exception ex) { Debug.WriteLine($"Tray TUN PropertyChanged subscription error: {ex.Message}"); }
        menu.Items.Add(trayTunItem);

        // ── 强制 GC ──
        var gcItem = new MenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TrayForceGc.Text") };
        gcItem.Click += async (_, _) =>
        {
            try
            {
                var clash = ServiceLocator.Get<IClashService>();
                if (clash.CoreState == CoreState.Running)
                    await clash.ForceGcAsync();
            }
            catch (Exception ex) { Debug.WriteLine($"Tray GC error: {ex.Message}"); }
        };
        menu.Items.Add(gcItem);

        // ── 重启核心 ──
        var restartItem = new MenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TrayRestartCore.Text") };
        restartItem.Click += async (_, _) =>
        {
            try
            {
                var clash = ServiceLocator.Get<IClashService>();
                if (clash.CoreState == CoreState.Running)
                {
                    await clash.StopAsync();
                    await Task.Delay(500);
                    await clash.StartAsync();
                }
            }
            catch (Exception ex) { Debug.WriteLine($"Tray restart error: {ex.Message}"); }
        };
        menu.Items.Add(restartItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        // ── 出站模式子菜单 ──
        var modeItem = new MenuFlyoutSubItem { Text = Services.LocalizationHelper.GetString("TrayOutboundMode.Text") };

        var modeRule = new ToggleMenuFlyoutItem { Text = Services.LocalizationHelper.GetString("DashModeRule.Content"), IsChecked = true };
        var modeGlobal = new ToggleMenuFlyoutItem { Text = Services.LocalizationHelper.GetString("DashModeGlobal.Content") };
        var modeDirect = new ToggleMenuFlyoutItem { Text = Services.LocalizationHelper.GetString("DashModeDirect.Content") };

        void ClearModeChecks()
        {
            modeRule.IsChecked = false;
            modeGlobal.IsChecked = false;
            modeDirect.IsChecked = false;
        }

        modeRule.Click += async (_, _) =>
        {
            ClearModeChecks();
            modeRule.IsChecked = true;
            try { await ServiceLocator.Get<IClashService>().SetOutboundModeAsync(OutboundMode.Rule); }
            catch (Exception ex) { Debug.WriteLine($"Tray mode Rule error: {ex.Message}"); }
        };
        modeGlobal.Click += async (_, _) =>
        {
            ClearModeChecks();
            modeGlobal.IsChecked = true;
            try { await ServiceLocator.Get<IClashService>().SetOutboundModeAsync(OutboundMode.Global); }
            catch (Exception ex) { Debug.WriteLine($"Tray mode Global error: {ex.Message}"); }
        };
        modeDirect.Click += async (_, _) =>
        {
            ClearModeChecks();
            modeDirect.IsChecked = true;
            try { await ServiceLocator.Get<IClashService>().SetOutboundModeAsync(OutboundMode.Direct); }
            catch (Exception ex) { Debug.WriteLine($"Tray mode Direct error: {ex.Message}"); }
        };

        modeItem.Items.Add(modeRule);
        modeItem.Items.Add(modeGlobal);
        modeItem.Items.Add(modeDirect);
        menu.Items.Add(modeItem);

        // ── 配置切换子菜单 ──
        var profileItem = new MenuFlyoutSubItem { Text = Services.LocalizationHelper.GetString("NavProfiles.Content") };
        _trayProfileMenu = profileItem;
        menu.Items.Add(profileItem);

        // Populate profiles asynchronously
        _ = PopulateTrayProfilesAsync();

        menu.Items.Add(new MenuFlyoutSeparator());

        // ── 打开数据目录 ──
        var openDataItem = new MenuFlyoutItem { Text = Services.LocalizationHelper.GetString("AboutOpenDataFolder.Content") };
        openDataItem.Click += (_, _) =>
        {
            try
            {
                var dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WinUIClash");
                Directory.CreateDirectory(dataDir);
                Process.Start(new ProcessStartInfo
                {
                    FileName = dataDir,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) { Debug.WriteLine($"Open data dir error: {ex.Message}"); }
        };
        menu.Items.Add(openDataItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        // ── 退出 ──
        var exitItem = new MenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TrayExit.Text") };
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        _trayIcon.ContextFlyout = menu;

        // 强制创建托盘图标（确保立即显示）
        _trayIcon.ForceCreate();

        // 初始化核心运行状态
        try
        {
            var clash = ServiceLocator.Get<IClashService>();
            _trayRunItem.IsChecked = clash.CoreState == CoreState.Running;
        }
        catch { }
    }

    private async Task PopulateTrayProfilesAsync()
    {
        if (_trayProfileMenu == null) return;

        try
        {
            var storage = new Services.ProfileStorageService();
            var profiles = await storage.LoadProfileListAsync();
            if (profiles.Count == 0) return;

            // Get active profile from the clash service
            var clash = ServiceLocator.Get<IClashService>();
            var apiProfiles = await clash.GetProfilesAsync();
            var activeId = apiProfiles.FirstOrDefault(p => p.IsActive)?.Id ?? "";

            _dispatcher.TryEnqueue(() =>
            {
                _trayProfileMenu.Items.Clear();
                foreach (var profile in profiles)
                {
                    var toggleItem = new ToggleMenuFlyoutItem
                    {
                        Text = string.IsNullOrWhiteSpace(profile.Label) ? profile.Id : profile.Label,
                        IsChecked = profile.Id == activeId,
                    };
                    var capturedProfile = profile;
                    toggleItem.Click += async (_, _) =>
                    {
                        try
                        {
                            var c = ServiceLocator.Get<IClashService>();
                            var configPath = storage.GetConfigPath(capturedProfile.Id);
                            await c.SwitchProfileAsync(capturedProfile.Id, configPath);

                            // Update checkmarks
                            foreach (var item in _trayProfileMenu.Items.OfType<ToggleMenuFlyoutItem>())
                                item.IsChecked = false;
                            toggleItem.IsChecked = true;
                        }
                        catch { }
                    };
                    _trayProfileMenu.Items.Add(toggleItem);
                }
            });
        }
        catch { }
    }

    private void ShowWindow()
    {
        this.Show();
        AppWindow.Show();
        // 确保窗口在前台
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        PInvoke.SetForegroundWindow(new HandleRef(null, hwnd));
    }

    private async void ExitApp()
    {
        _isExiting = true;
        await PerformCleanupAsync();

        _trayIcon?.Dispose();
        _trayIcon = null;
        _currentTrayIcon?.Dispose();
        _currentTrayIcon = null;
        Close();
        Application.Current.Exit();
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_isExiting) return;

        // 保存窗口状态
        SaveWindowState();

        // 读取设置：关闭窗口时是否最小化到托盘
        try
        {
            var settings = ServiceLocator.Get<AppSettings>();
            if (settings.MinimizeOnExit)
            {
                args.Handled = true;
                this.Hide();
                return;
            }
        }
        catch { /* ServiceLocator 未初始化时忽略 */ }

        // 正常关闭 — 执行共享清理
        await PerformCleanupAsync();

        _trayIcon?.Dispose();
        _trayIcon = null;
        _currentTrayIcon?.Dispose();
        _currentTrayIcon = null;
    }

    /// <summary>
    /// Shared cleanup logic called by both ExitApp and OnWindowClosed.
    /// Stops timers, core process, disables system proxy, and saves settings.
    /// </summary>
    public async Task PerformCleanupAsync()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;

        try
        {
            // Stop timers
            _statusBarConnTimer?.Stop();
            _statusBarConnTimer = null;
            _runtimeTimer?.Stop();
            _runtimeTimer = null;

            // Unsubscribe from events
            if (_clash != null)
            {
                _clash.CoreStateChanged -= OnCoreStateChanged;
                _clash.TrafficUpdated -= OnTrafficUpdated;
                _clash.OutboundModeChanged -= OnOutboundModeChanged;
            }
            if (_appSettings != null)
            {
                _appSettings.PropertyChanged -= OnSettingsPropertyChanged;
            }

            // Stop core process
            var core = ServiceLocator.Get<Services.CoreProcessService>();
            await core.StopAsync();
            core.Dispose();

            // Disable system proxy and stop guard
            ServiceLocator.Get<Services.SystemProxyService>().EnsureDisabledOnExit();

            // Save settings
            ServiceLocator.Get<Services.SettingsService>().SaveImmediate();
        }
        catch { }
    }

    // ── 图标生成 ──────────────────────────────────────────────────────────────

    private static System.Drawing.Icon CreateTrayIcon(System.Drawing.Color? circleColor = null)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            var hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE);
            if (hIcon != IntPtr.Zero)
            {
                try
                {
                    return System.Drawing.Icon.FromHandle(hIcon);
                }
                catch
                {
                    DestroyIcon(hIcon);
                }
            }
        }

        var color = circleColor ?? System.Drawing.Color.FromArgb(33, 150, 243);
        using var bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 15f, FontStyle.Bold);
            var size = g.MeasureString("W", font);
            g.DrawString("W", font, Brushes.White,
                (32 - size.Width) / 2, (32 - size.Height) / 2);
        }

        var fallbackIcon = bmp.GetHicon();
        try
        {
            return System.Drawing.Icon.FromHandle(fallbackIcon);
        }
        catch
        {
            DestroyIcon(fallbackIcon);
            throw;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;

    // ── Win32 辅助 ────────────────────────────────────────────────────────────

    private static class PInvoke
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(HandleRef hWnd);
    }
}
