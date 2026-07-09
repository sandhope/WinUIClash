using System.Drawing;
using System.Drawing.Drawing2D;
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

    // 状态栏连接数轮询定时器
    private DispatcherTimer? _statusBarConnTimer;
    private Traffic _lastTraffic = new();
    private int _lastConnectionCount;

    public MainWindow()
    {
        InitializeComponent();

        _dispatcher = DispatcherQueue.GetForCurrentThread()!;

        // 窗口基础配置
        Title = "WinUIClash";

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
        }
        catch { /* ServiceLocator 未初始化时忽略 */ }
    }

    private void SaveWindowState()
    {
        try
        {
            var settings = ServiceLocator.Get<AppSettings>();
            var presenter = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;

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

    private void NavigateTo(string? tag)
    {
        if (tag is null) return;
        if (PageMap.TryGetValue(tag, out var pageType))
        {
            ContentFrame.Navigate(pageType);
        }
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
                    var item = toolsVm.SettingsItems.Concat(toolsVm.OtherItems)
                        .FirstOrDefault(i => i.Title == title);
                    if (item != null)
                    {
                        toolsVm.OpenSettingCommand.Execute(item);
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
                }
                catch
                {
                    _lastConnectionCount = 0;
                    ConnectionCountText.Text = "0";
                }
            };
            _statusBarConnTimer.Start();
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

        UpdateTrayTooltip();
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

        _trayIcon.ToolTipText = $"WinUIClash\n{stateText}\n↑{up}  ↓{down}\n{LocalizationHelper.GetString("ConnCountSuffix.Text").Trim()}: {_lastConnectionCount}\n{LocalizationHelper.GetString("ProxyLabel.Text")}: {proxy}";
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

    // ── 系统托盘 ──────────────────────────────────────────────────────────────

    private void InitTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "WinUIClash",
            Icon = CreateTrayIcon(),
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
            catch { }
        };
        menu.Items.Add(_trayRunItem);

        // ── 系统代理 ──
        _trayProxyItem = new ToggleMenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TraySystemProxy.Text") };
        try
        {
            var settings = ServiceLocator.Get<AppSettings>();
            _trayProxyItem.IsChecked = settings.SystemProxy;
        }
        catch { }
        _trayProxyItem.Click += (_, _) =>
        {
            try
            {
                var settings = ServiceLocator.Get<AppSettings>();
                settings.SystemProxy = _trayProxyItem.IsChecked;
            }
            catch { }
        };
        menu.Items.Add(_trayProxyItem);

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
            try { await ServiceLocator.Get<IClashService>().SetOutboundModeAsync(OutboundMode.Rule); } catch { }
        };
        modeGlobal.Click += async (_, _) =>
        {
            ClearModeChecks();
            modeGlobal.IsChecked = true;
            try { await ServiceLocator.Get<IClashService>().SetOutboundModeAsync(OutboundMode.Global); } catch { }
        };
        modeDirect.Click += async (_, _) =>
        {
            ClearModeChecks();
            modeDirect.IsChecked = true;
            try { await ServiceLocator.Get<IClashService>().SetOutboundModeAsync(OutboundMode.Direct); } catch { }
        };

        modeItem.Items.Add(modeRule);
        modeItem.Items.Add(modeGlobal);
        modeItem.Items.Add(modeDirect);
        menu.Items.Add(modeItem);

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
    }

    /// <summary>
    /// Shared cleanup logic called by both ExitApp and OnWindowClosed.
    /// Stops timers, core process, disables system proxy, and saves settings.
    /// </summary>
    private async Task PerformCleanupAsync()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;

        try
        {
            // Stop timers
            _statusBarConnTimer?.Stop();
            _statusBarConnTimer = null;

            // Unsubscribe from events
            if (_clash != null)
            {
                _clash.CoreStateChanged -= OnCoreStateChanged;
                _clash.TrafficUpdated -= OnTrafficUpdated;
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

    private static System.Drawing.Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(33, 150, 243));
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 15f, FontStyle.Bold);
            var size = g.MeasureString("W", font);
            g.DrawString("W", font, Brushes.White,
                (32 - size.Width) / 2, (32 - size.Height) / 2);
        }

        var hIcon = bmp.GetHicon();
        var icon = System.Drawing.Icon.FromHandle(hIcon);
        var clone = (System.Drawing.Icon)icon.Clone();
        DestroyIcon(hIcon);
        return clone;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    // ── Win32 辅助 ────────────────────────────────────────────────────────────

    private static class PInvoke
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(HandleRef hWnd);
    }
}
