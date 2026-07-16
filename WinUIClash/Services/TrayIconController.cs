using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using WinUIClash.Models;
using WinUIClash.Services;
using WinUIClash.ViewModels;

namespace WinUIClash;

/// <summary>
/// 系统托盘控制器：封装 H.NotifyIcon 的托盘图标与右键菜单的构建、状态订阅与图标管理。
/// 从 MainWindow 抽离，降低主窗口耦合。
/// 仅借鉴 ClashSharp "状态与渲染分离" 的思路，不照搬其纯 Win32 三件套实现——
/// H.NotifyIcon 的 MenuFlyout 是保留模式（retained-mode），菜单项常驻并靠
/// PropertyChanged 逐项 mutate，而非每次右键整体重建，故不引入不可变 state 记录。
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private TaskbarIcon? _trayIcon;
    private readonly DispatcherQueue _dispatcher;
    private readonly Action _showWindow;
    private readonly Action _exitApp;

    // 托盘菜单项（需要动态更新状态）
    private MenuFlyoutItem? _trayShowItem;
    private ToggleMenuFlyoutItem? _trayProxyItem;
    private ToggleMenuFlyoutItem? _trayConnectItem;
    private ToggleMenuFlyoutItem? _trayTunItem;
    private MenuFlyoutItem? _trayGcItem;
    private MenuFlyoutItem? _trayRestartItem;
    private MenuFlyoutSubItem? _trayModeItem;
    private ToggleMenuFlyoutItem? _trayModeRule;
    private ToggleMenuFlyoutItem? _trayModeGlobal;
    private ToggleMenuFlyoutItem? _trayModeDirect;
    private MenuFlyoutSubItem? _trayProfileMenu;
    private MenuFlyoutItem? _trayOpenDataItem;
    private MenuFlyoutItem? _trayExitItem;

    private System.Drawing.Icon? _currentTrayIcon;
    private CoreState _lastTrayIconState = CoreState.Stopped;

    public TrayIconController(Action showWindow, Action exitApp, DispatcherQueue dispatcher)
    {
        _showWindow = showWindow;
        _exitApp = exitApp;
        _dispatcher = dispatcher;
        InitTrayIcon();

        // 订阅语言切换：托盘菜单在构建时一次性读取文本，需在语言变更后逐项刷新。
        try
        {
            var sr = ServiceLocator.Get<StringResources>();
            sr.PropertyChanged += OnStringResourcesChanged;
        }
        catch (Exception ex) { Debug.WriteLine($"Tray i18n subscription error: {ex.Message}"); }
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
            // 修复 WinUI 3 下 ContextFlyout 点击事件路由断裂（BUG-001）：
            // SecondWindow 模式在独立窗口中渲染原生菜单，点击能正确触发逻辑。
            ContextMenuMode = H.NotifyIcon.ContextMenuMode.SecondWindow,
        };

        // 双击托盘图标 → 显示窗口
        _trayIcon.DoubleClickCommand = new RelayCommand(_showWindow);

        // 右键菜单
        var menu = new MenuFlyout();

        // ── 显示主窗口 ──
        _trayShowItem = new MenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TrayShow.Text") };
        _trayShowItem.Click += (_, _) => _showWindow();
        menu.Items.Add(_trayShowItem);

        // ── 连接 / 断开代理（核心常驻，此处仅切换模式 + 系统代理）──
        _trayConnectItem = new ToggleMenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TrayConnectProxy.Text") };
        try
        {
            var dashVm = ServiceLocator.Get<ViewModels.DashboardViewModel>();
            _trayConnectItem.IsChecked = dashVm.IsRunning;
            dashVm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModels.DashboardViewModel.IsRunning))
                    _dispatcher.TryEnqueue(() =>
                    {
                        _trayConnectItem.IsChecked = dashVm.IsRunning;
                        _trayConnectItem.Text = dashVm.IsRunning
                            ? Services.LocalizationHelper.GetString("TrayDisconnectProxy.Text")
                            : Services.LocalizationHelper.GetString("TrayConnectProxy.Text");
                    });
            };
        }
        catch (Exception ex) { Debug.WriteLine($"Tray connect init error: {ex.Message}"); }
        _trayConnectItem.Click += async (_, _) =>
        {
            try
            {
                var dashVm = ServiceLocator.Get<ViewModels.DashboardViewModel>();
                await dashVm.ToggleCoreCommand.ExecuteAsync(null);
            }
            catch (Exception ex) { Debug.WriteLine($"Tray connect toggle error: {ex.Message}"); }
        };
        menu.Items.Add(_trayConnectItem);

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
                settings.SystemProxy = !settings.SystemProxy;
            }
            catch (Exception ex) { Debug.WriteLine($"Tray proxy toggle error: {ex.Message}"); }
        };
        // Sync System Proxy state from settings changes (mirror TUN sync)
        try
        {
            var proxyAppSettings = ServiceLocator.Get<AppSettings>();
            proxyAppSettings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.SystemProxy))
                    _dispatcher.TryEnqueue(() => _trayProxyItem!.IsChecked = proxyAppSettings.SystemProxy);
            };
        }
        catch (Exception ex) { Debug.WriteLine($"Tray proxy PropertyChanged subscription error: {ex.Message}"); }
        menu.Items.Add(_trayProxyItem);

        // ── TUN 模式 ──
        _trayTunItem = new ToggleMenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TrayTunMode.Text") };
        try
        {
            var tunSettings = ServiceLocator.Get<AppSettings>();
            _trayTunItem.IsChecked = tunSettings.TunMode;
        }
        catch (Exception ex) { Debug.WriteLine($"Tray TUN init error: {ex.Message}"); }
        _trayTunItem.Click += (_, _) =>
        {
            try
            {
                var tunSettings = ServiceLocator.Get<AppSettings>();
                tunSettings.TunMode = !tunSettings.TunMode;
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
                    _dispatcher.TryEnqueue(() => _trayTunItem!.IsChecked = tunAppSettings.TunMode);
            };
        }
        catch (Exception ex) { Debug.WriteLine($"Tray TUN PropertyChanged subscription error: {ex.Message}"); }
        menu.Items.Add(_trayTunItem);

        // ── 强制 GC ──
        _trayGcItem = new MenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TrayForceGc.Text") };
        _trayGcItem.Click += async (_, _) =>
        {
            try
            {
                var clash = ServiceLocator.Get<IClashService>();
                if (clash.CoreState == CoreState.Running)
                    await clash.ForceGcAsync();
            }
            catch (Exception ex) { Debug.WriteLine($"Tray GC error: {ex.Message}"); }
        };
        menu.Items.Add(_trayGcItem);

        // ── 重启核心 ──
        _trayRestartItem = new MenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TrayRestartCore.Text") };
        _trayRestartItem.Click += async (_, _) =>
        {
            try
            {
                var clash = ServiceLocator.Get<IClashService>();
                if (clash.CoreState == CoreState.Running)
                    await clash.RestartAsync();
            }
            catch (Exception ex) { Debug.WriteLine($"Tray restart error: {ex.Message}"); }
        };
        menu.Items.Add(_trayRestartItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        // ── 出站模式子菜单 ──
        _trayModeItem = new MenuFlyoutSubItem { Text = Services.LocalizationHelper.GetString("TrayOutboundMode.Text") };

        _trayModeRule = new ToggleMenuFlyoutItem { Text = Services.LocalizationHelper.GetString("DashModeRule.Content"), IsChecked = true };
        _trayModeGlobal = new ToggleMenuFlyoutItem { Text = Services.LocalizationHelper.GetString("DashModeGlobal.Content") };
        _trayModeDirect = new ToggleMenuFlyoutItem { Text = Services.LocalizationHelper.GetString("DashModeDirect.Content") };

        void ClearModeChecks()
        {
            _trayModeRule!.IsChecked = false;
            _trayModeGlobal!.IsChecked = false;
            _trayModeDirect!.IsChecked = false;
        }

        _trayModeRule.Click += async (_, _) =>
        {
            ClearModeChecks();
            _trayModeRule.IsChecked = true;
            try { await ServiceLocator.Get<IClashService>().SetOutboundModeAsync(OutboundMode.Rule); }
            catch (Exception ex) { Debug.WriteLine($"Tray mode Rule error: {ex.Message}"); }
        };
        _trayModeGlobal.Click += async (_, _) =>
        {
            ClearModeChecks();
            _trayModeGlobal.IsChecked = true;
            try { await ServiceLocator.Get<IClashService>().SetOutboundModeAsync(OutboundMode.Global); }
            catch (Exception ex) { Debug.WriteLine($"Tray mode Global error: {ex.Message}"); }
        };
        _trayModeDirect.Click += async (_, _) =>
        {
            ClearModeChecks();
            _trayModeDirect.IsChecked = true;
            try { await ServiceLocator.Get<IClashService>().SetOutboundModeAsync(OutboundMode.Direct); }
            catch (Exception ex) { Debug.WriteLine($"Tray mode Direct error: {ex.Message}"); }
        };

        _trayModeItem.Items.Add(_trayModeRule);
        _trayModeItem.Items.Add(_trayModeGlobal);
        _trayModeItem.Items.Add(_trayModeDirect);
        menu.Items.Add(_trayModeItem);

        // ── 配置切换子菜单 ──
        var profileItem = new MenuFlyoutSubItem { Text = Services.LocalizationHelper.GetString("NavProfiles.Content") };
        _trayProfileMenu = profileItem;
        menu.Items.Add(profileItem);

        // Populate profiles asynchronously
        _ = PopulateTrayProfilesAsync();

        menu.Items.Add(new MenuFlyoutSeparator());

        // ── 打开数据目录 ──
        _trayOpenDataItem = new MenuFlyoutItem { Text = Services.LocalizationHelper.GetString("AboutOpenDataFolder.Content") };
        _trayOpenDataItem.Click += (_, _) =>
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
        menu.Items.Add(_trayOpenDataItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        // ── 退出 ──
        _trayExitItem = new MenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TrayExit.Text") };
        _trayExitItem.Click += (_, _) => _exitApp();
        menu.Items.Add(_trayExitItem);

        _trayIcon.ContextFlyout = menu;

        // 强制创建托盘图标（确保立即显示）
        _trayIcon.ForceCreate();
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

    private void OnStringResourcesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName)) return;
        _dispatcher.TryEnqueue(UpdateTrayMenuTexts);
    }

    /// <summary>
    /// 语言切换后逐项刷新托盘右键菜单文本。
    /// </summary>
    private void UpdateTrayMenuTexts()
    {
        if (_trayShowItem != null)
            _trayShowItem.Text = Services.LocalizationHelper.GetString("TrayShow.Text");

        if (_trayConnectItem != null)
        {
            try
            {
                var dashVm = ServiceLocator.Get<DashboardViewModel>();
                _trayConnectItem.Text = dashVm.IsRunning
                    ? Services.LocalizationHelper.GetString("TrayDisconnectProxy.Text")
                    : Services.LocalizationHelper.GetString("TrayConnectProxy.Text");
            }
            catch
            {
                _trayConnectItem.Text = Services.LocalizationHelper.GetString("TrayConnectProxy.Text");
            }
        }

        if (_trayProxyItem != null)
            _trayProxyItem.Text = Services.LocalizationHelper.GetString("TraySystemProxy.Text");

        if (_trayTunItem != null)
            _trayTunItem.Text = Services.LocalizationHelper.GetString("TrayTunMode.Text");

        if (_trayGcItem != null)
            _trayGcItem.Text = Services.LocalizationHelper.GetString("TrayForceGc.Text");

        if (_trayRestartItem != null)
            _trayRestartItem.Text = Services.LocalizationHelper.GetString("TrayRestartCore.Text");

        if (_trayModeItem != null)
            _trayModeItem.Text = Services.LocalizationHelper.GetString("TrayOutboundMode.Text");

        if (_trayModeRule != null)
            _trayModeRule.Text = Services.LocalizationHelper.GetString("DashModeRule.Content");

        if (_trayModeGlobal != null)
            _trayModeGlobal.Text = Services.LocalizationHelper.GetString("DashModeGlobal.Content");

        if (_trayModeDirect != null)
            _trayModeDirect.Text = Services.LocalizationHelper.GetString("DashModeDirect.Content");

        if (_trayProfileMenu != null)
            _trayProfileMenu.Text = Services.LocalizationHelper.GetString("NavProfiles.Content");

        if (_trayOpenDataItem != null)
            _trayOpenDataItem.Text = Services.LocalizationHelper.GetString("AboutOpenDataFolder.Content");

        if (_trayExitItem != null)
            _trayExitItem.Text = Services.LocalizationHelper.GetString("TrayExit.Text");
    }

    /// <summary>设置托盘提示文本（由主窗口根据其状态栏数据组装后写入）。</summary>
    public string ToolTipText
    {
        set => _trayIcon!.ToolTipText = value;
    }

    /// <summary>根据核心运行状态刷新托盘图标颜色（仅在状态变化时重建图标资源）。</summary>
    public void UpdateIcon(CoreState state)
    {
        if (state == _lastTrayIconState) return;
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
        _trayIcon!.Icon = _currentTrayIcon;
        oldIcon?.Dispose();
    }

    public void Dispose()
    {
        try
        {
            var sr = ServiceLocator.Get<StringResources>();
            sr.PropertyChanged -= OnStringResourcesChanged;
        }
        catch { }

        _trayIcon?.Dispose();
        _currentTrayIcon?.Dispose();
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
}
