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
        ["Logs"]        = typeof(Views.LogsView),
        ["Tools"]       = typeof(Views.ToolsView),
    };

    private TrayIconController? _tray;
    private bool _isExiting;
    private bool _cleanedUp;
    private readonly DispatcherQueue _dispatcher;

    // 状态栏引用的服务
    private IClashService? _clash;
    private AppSettings? _appSettings;
    private ViewModels.DashboardViewModel? _dashboardVm;
    private StringResources? _stringResources;

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

        // 初始化系统托盘（逻辑已抽离至 TrayIconController）
        _tray = new TrayIconController(ShowWindow, ExitApp, _dispatcher);

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

            // 恢复窗口大小（仅当存在已保存的尺寸时才应用，
            // 否则使用系统默认尺寸，而不是强制一个固定默认值）
            if (settings.WindowWidth > 0 && settings.WindowHeight > 0)
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(
                    settings.WindowWidth, settings.WindowHeight));
            }

            // 恢复窗口位置（如果之前保存过）
            if (settings.WindowX != 0 || settings.WindowY != 0)
            {
                AppWindow.Move(new Windows.Graphics.PointInt32(
                    settings.WindowX, settings.WindowY));
            }

            // 恢复侧边栏状态
            RootNavigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
            RootNavigation.IsPaneOpen = !settings.IsSidebarCompact;

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

            settings.IsSidebarCompact = !RootNavigation.IsPaneOpen;

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

    /// <summary>
    /// 按 Tag 在菜单项中查找导航项（工具页已回归主菜单，与 FlClash 1:1 一致）。
    /// </summary>
    private NavigationViewItem? FindNavItem(string tag)
    {
        var inMenu = RootNavigation.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(m => m.Tag as string == tag);
        if (inMenu != null) return inMenu;

        var footer = RootNavigation.FooterMenuItems;
        return footer?.OfType<NavigationViewItem>()
                .FirstOrDefault(m => m.Tag as string == tag);
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
                    // 同步选中侧边栏：按 Tag 查找对应项（含底部菜单），避免捕获循环变量 i
                    var item = FindNavItem(tag);
                    if (item != null)
                        RootNavigation.SelectedItem = item;
                };
            RootGrid.KeyboardAccelerators.Add(accel);
        }

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
        sidebarAccel.Invoked += (_, _) => ToggleSidebar();
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

        // F1 快捷键帮助
        var helpAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.F1,
        };
        helpAccel.Invoked += (_, _) => OpenShortcuts();
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

        // Escape 关闭通知栏
        var escAccel = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.Escape,
        };
        escAccel.Invoked += (_, _) =>
        {
            if (NotificationBar.IsOpen)
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
                // Navigate to Tools (settings hub) — now in footer
                var toolsItem = FindNavItem("tools");
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

    /// <summary>
    /// 打开「工具 → 快捷键」子页面，作为集中展示所有快捷键的入口（F1 触发）。
    /// </summary>
    private void OpenShortcuts()
    {
        NavigateTo("Tools");
        var toolsItem = FindNavItem("Tools");
        if (toolsItem != null)
            RootNavigation.SelectedItem = toolsItem;

        _dispatcher.TryEnqueue(() =>
        {
            try
            {
                var toolsVm = ServiceLocator.Get<ViewModels.ToolsViewModel>();
                toolsVm.OpenSettingCommand.Execute("Shortcuts");
                if (ContentFrame.Content is Views.ToolsView toolsView)
                    toolsView.SyncSubPage(toolsVm.CurrentPage);
            }
            catch { }
        });
    }

    private void RefreshCurrentPage()
    {
        if (ContentFrame.CurrentSourcePageType is { } pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void ToggleSidebar()
    {
        RootNavigation.IsPaneOpen = !RootNavigation.IsPaneOpen;
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        ToggleSidebar();
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
            _dashboardVm = ServiceLocator.Get<ViewModels.DashboardViewModel>();

            // 订阅核心状态变化
            _clash.CoreStateChanged += OnCoreStateChanged;
            UpdateCoreStatusUI(_clash.CoreState);

            // 订阅实时流量
            _clash.TrafficUpdated += OnTrafficUpdated;

            // 订阅系统代理设置变化（保留用于更新托盘提示）
            _appSettings.PropertyChanged += OnSettingsPropertyChanged;

            // 订阅代理激活状态变化（由 DashboardViewModel.IsRunning 控制）
            _dashboardVm.PropertyChanged += OnDashboardPropertyChanged;
            UpdateProxyStatusUI(_dashboardVm.IsRunning);

            // 订阅出站模式变化：单一来源 = AppSettings.OutboundMode（用户设置的状态）。
            // 由 _appSettings.PropertyChanged（OnSettingsPropertyChanged）统一处理，不依赖核心运行状态。
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
                }
                catch
                {
                    _lastConnectionCount = 0;
                    ConnectionCountText.Text = "0";
                }
            };
            _statusBarConnTimer.Start();

            // 内存更新事件订阅
            _clash.MemoryUpdated += OnMemoryUpdated;

            // 订阅语言切换：状态栏三处文本由命令式赋值设置，
            // 绑定不会自动刷新，需在语言变更时重新调用各 Update 方法。
            _stringResources = ServiceLocator.Get<StringResources>();
            _stringResources.PropertyChanged += OnStringResourcesChanged;
        }
        catch { /* ServiceLocator 未初始化时忽略 */ }
    }

    private void OnStringResourcesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName)) return;
        _dispatcher.TryEnqueue(() =>
        {
            UpdateCoreStatusUI(_clash?.CoreState ?? CoreState.Stopped);
            UpdateOutboundModeUI(_clash?.GetOutboundMode() ?? OutboundMode.Rule);
            UpdateProxyStatusUI(_dashboardVm?.IsRunning ?? false);
        });
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
            CoreState.Running  => Services.LocalizationHelper.GetString("StatusCoreRunning.Text"),
            CoreState.Starting => Services.LocalizationHelper.GetString("DashStarting.Text"),
            CoreState.Stopping => Services.LocalizationHelper.GetString("DashStopping.Text"),
            _                  => Services.LocalizationHelper.GetString("StatusCoreStopped.Text"),
        };

        StatusText.Opacity = state == CoreState.Running ? 0.8 : 0.6;

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

    private void OnMemoryUpdated(long memory)
    {
        _dispatcher.TryEnqueue(() =>
        {
            MemoryUsageText.Text = Converters.ByteFormatter.Format(memory);
        });
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
            UpdateTrayTooltip();
        }
        // 出站模式（单一来源）变化 → 刷新状态栏文本
        else if (e.PropertyName == nameof(AppSettings.OutboundMode))
        {
            _dispatcher.TryEnqueue(() => UpdateOutboundModeUI(_clash?.GetOutboundMode() ?? OutboundMode.Rule));
        }
    }

    private void OnDashboardPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.DashboardViewModel.IsRunning))
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (_dashboardVm != null)
                    UpdateProxyStatusUI(_dashboardVm.IsRunning);
            });
        }
    }

    private void UpdateProxyStatusUI(bool isActive)
    {
        ProxyIcon.Opacity = isActive ? 1.0 : 0.3;
        ProxyIcon.Foreground = isActive
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
        ProxyText.Opacity = isActive ? 0.8 : 0.3;
        ProxyText.Text = isActive
            ? Services.LocalizationHelper.GetString("StatusProxyActive.Text")
            : Services.LocalizationHelper.GetString("ProxyLabel.Text");

        UpdateTrayTooltip();
    }

    private void UpdateTrayTooltip()
    {
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

        var tip = $"WinUIClash\n{stateText}\n↑{up}  ↓{down}\n{LocalizationHelper.GetString("ConnCountSuffix.Text").Trim()}: {_lastConnectionCount}\n{LocalizationHelper.GetString("ProxyLabel.Text")}: {proxy}\nTUN: {tun}";

        // Append active profile name if available
        try
        {
            var profilesVm = ServiceLocator.Get<ViewModels.ProfilesViewModel>();
            if (profilesVm.ActiveProfile != null)
                tip += $"\n{LocalizationHelper.GetString("NavProfiles.Content")}: {profilesVm.ActiveProfile.Label}";
        }
        catch { }

        if (_tray != null)
        {
            _tray.ToolTipText = tip;
            _tray.UpdateIcon(state);
        }
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

        _tray?.Dispose();

        // 先触发窗口关闭流程，再强制退出应用（Close 后如果进程仍在，Exit 兜底）
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

        _tray?.Dispose();
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
            }
            if (_appSettings != null)
            {
                _appSettings.PropertyChanged -= OnSettingsPropertyChanged;
            }
            if (_dashboardVm != null)
            {
                _dashboardVm.PropertyChanged -= OnDashboardPropertyChanged;
            }
            if (_stringResources != null)
            {
                _stringResources.PropertyChanged -= OnStringResourcesChanged;
            }

            // 退出时彻底终止常驻核心进程（仅 App 退出才杀进程，符合 REFACTOR_GUIDE T3）
            var clash = ServiceLocator.Get<IClashService>();
            await clash.ShutdownAsync();
            try { ServiceLocator.Get<Services.CoreProcessService>().Dispose(); } catch { }

            // Disable system proxy and stop guard
            ServiceLocator.Get<Services.SystemProxyService>().EnsureDisabledOnExit();

            // Save settings
            ServiceLocator.Get<Services.SettingsService>().SaveImmediate();
        }
        catch { }
    }


    // ── Win32 辅助 ────────────────────────────────────────────────────────────

    private static class PInvoke
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(HandleRef hWnd);
    }
}
