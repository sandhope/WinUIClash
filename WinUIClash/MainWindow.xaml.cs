using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinUIClash.Models;

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

    private TaskbarIcon? _trayIcon;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        // 窗口基础配置
        Title = "WinUIClash";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));

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

        // 拦截窗口关闭事件
        Closed += OnWindowClosed;
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

        var showItem = new MenuFlyoutItem { Text = "显示主窗口" };
        showItem.Click += (_, _) => ShowWindow();
        menu.Items.Add(showItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = "退出" };
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        _trayIcon.ContextFlyout = menu;

        // 强制创建托盘图标（确保立即显示）
        _trayIcon.ForceCreate();
    }

    private void ShowWindow()
    {
        this.Show();
        AppWindow.Show();
        // 确保窗口在前台
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        PInvoke.SetForegroundWindow(new HandleRef(null, hwnd));
    }

    private void ExitApp()
    {
        _isExiting = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        Close();
        Application.Current.Exit();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_isExiting) return;

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

        // 正常退出
        _trayIcon?.Dispose();
        _trayIcon = null;
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
