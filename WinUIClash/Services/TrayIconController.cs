using System;
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
    private ToggleMenuFlyoutItem? _trayProxyItem;
    private ToggleMenuFlyoutItem? _trayConnectItem;
    private MenuFlyoutSubItem? _trayProfileMenu;

    private System.Drawing.Icon? _currentTrayIcon;
    private CoreState _lastTrayIconState = CoreState.Stopped;

    public TrayIconController(Action showWindow, Action exitApp, DispatcherQueue dispatcher)
    {
        _showWindow = showWindow;
        _exitApp = exitApp;
        _dispatcher = dispatcher;
        InitTrayIcon();
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
        var showItem = new MenuFlyoutItem { Text = Services.LocalizationHelper.GetString("TrayShow.Text") };
        showItem.Click += (_, _) => _showWindow();
        menu.Items.Add(showItem);

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
                    await clash.RestartAsync();
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
        exitItem.Click += (_, _) => _exitApp();
        menu.Items.Add(exitItem);

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
