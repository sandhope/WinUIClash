using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUIClash
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>当前主窗口实例，供全局访问（如主题切换）</summary>
        public static Window? CurrentWindow { get; private set; }

        /// <summary>保存的导航页面，用于语言切换后恢复</summary>
        private static string? _savedNavigationTag;

        private static System.Threading.Mutex? _instanceMutex;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();

            // 全局异常处理
            UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private async void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled exception: {e.Exception}");

            // 记录到日志文件
            try
            {
                var logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinUIClash");
                System.IO.Directory.CreateDirectory(logDir);
                var logFile = System.IO.Path.Combine(logDir, "crash.log");
                var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n";
                System.IO.File.AppendAllText(logFile, entry);
            }
            catch { }

            // 尝试显示错误对话框（如果窗口可用）
            if (CurrentWindow?.Content is FrameworkElement root)
            {
                try
                {
                    var dialog = new ContentDialog
                    {
                        Title = Services.LocalizationHelper.GetString("AppErrorTitle.Text"),
                        Content = string.Format(Services.LocalizationHelper.GetString("AppErrorContent.Text").Replace("\\n", "\n"), e.Exception.Message),
                        PrimaryButtonText = Services.LocalizationHelper.GetString("AppErrorContinue.Content"),
                        CloseButtonText = Services.LocalizationHelper.GetString("AppErrorExit.Content"),
                        XamlRoot = root.XamlRoot,
                    };

                    if (await dialog.ShowAsync() == ContentDialogResult.None)
                    {
                        e.Handled = false; // 用户选择退出
                    }
                    else
                    {
                        e.Handled = true; // 用户选择继续
                    }
                }
                catch
                {
                    e.Handled = true;
                }
            }
            else
            {
                e.Handled = true;
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Unobserved task exception: {e.Exception}");
            e.SetObserved(); // 标记为已处理，防止进程崩溃

            try
            {
                var logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinUIClash");
                System.IO.Directory.CreateDirectory(logDir);
                var logFile = System.IO.Path.Combine(logDir, "crash.log");
                var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Unobserved: {e.Exception}\n\n";
                System.IO.File.AppendAllText(logFile, entry);
            }
            catch { }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // 单实例检测：防止同时运行多个 WinUIClash 实例
            _instanceMutex = new System.Threading.Mutex(true, "WinUIClash_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                // 已有实例在运行，退出当前进程
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
            }

            Resources.Add("LogLevelToColorConverter", new Converters.LogLevelToColorConverter());
            Resources.Add("BoolToVisibilityConverter", new Converters.BoolToVisibilityConverter());
            Resources.Add("InverseBoolToVisibilityConverter", new Converters.InverseBoolToVisibilityConverter());
            Resources.Add("NullToVisibilityConverter", new Converters.NullToVisibilityConverter());
            Resources.Add("DelayToColorConverter", new Converters.DelayToColorConverter());
            Resources.Add("DelayToTextConverter", new Converters.DelayToTextConverter());
            Resources.Add("BytesToStringConverter", new Converters.BytesToStringConverter());
            Resources.Add("BytesToSpeedConverter", new Converters.BytesToSpeedConverter());
            Resources.Add("DateTimeToRelativeConverter", new Converters.DateTimeToRelativeConverter());
            Resources.Add("DateTimeToTimeConverter", new Converters.DateTimeToTimeConverter());
            Resources.Add("HexToColorConverter", new Converters.HexToColorConverter());
            Resources.Add("EmptyCollectionToVisibilityConverter", new Converters.EmptyCollectionToVisibilityConverter());
            Resources.Add("NonEmptyCollectionToVisibilityConverter", new Converters.NonEmptyCollectionToVisibilityConverter());
            Resources.Add("InverseBoolConverter", new Converters.InverseBoolConverter());

            var stringResources = (Services.StringResources)Resources["S"];
            Services.LocalizationHelper.Initialize(stringResources);
            ServiceLocator.Build(stringResources);

            // 加载持久化设置
            var settingsService = ServiceLocator.Get<Services.SettingsService>();
            settingsService.Load();
            settingsService.EnableAutoSave();

            // 初始化系统代理：核心尚未启动，先禁用系统代理，避免指向死端口。
            // 系统代理的实际启用/禁用由 DashboardViewModel 在核心状态变化时根据设置统一处理。
            var appSettings = ServiceLocator.Get<Models.AppSettings>();
            var proxyService = ServiceLocator.Get<Services.SystemProxyService>();
            proxyService.Disable();
            proxyService.WatchSettings();

            // 监听 TUN 模式变化：需要重建 config.yaml 并重启核心
            // （TUN 配置在 config.yaml 里，核心需要重新读取才能创建/关闭虚拟网卡）
            appSettings.PropertyChanged += async (s, e) =>
            {
                if (e.PropertyName == nameof(Models.AppSettings.TunMode) ||
                    e.PropertyName == nameof(Models.AppSettings.TunStack))
                {
                    if (_tunWatcherSuppressed) return;
                    await ApplyTunChangeAsync();
                }
            };

            // 应用语言设置（必须在 MainWindow 构造之前，否则状态栏文字会显示为资源 key）
            var localizationService = ServiceLocator.Get<Services.LocalizationService>();
            localizationService.Initialize();

            CurrentWindow = new MainWindow();
            CurrentWindow.Activate();

            // 应用主题设置（明暗模式 + 主题色）
            ViewModels.Settings.ThemeSettingsViewModel.InitializeTheme();

            // 静默启动：如果命令行包含 --silent 或设置中启用了静默启动，则最小化到托盘
            var cmdArgs = Environment.GetCommandLineArgs();
            bool isSilent = appSettings.SilentLaunch ||
                            cmdArgs.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));
            if (isSilent)
            {
                CurrentWindow.AppWindow.Hide();
            }

            // 启动时自动拉起 Clash 核心（与 FlClash 一致：应用启动即连接核心，
            // 使网络/测速/代理页立即可用，避免手动点击启动的等待）。
            // 但 TUN 模式需要 UAC 提权注册 Helper Service，不在启动时自动拉起，
            // 避免软件一启动就弹管理员权限提示；TUN 模式下由用户点击开始按钮时触发。
            var clash = ServiceLocator.Get<Services.IClashService>();
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!appSettings.TunMode)
                        await clash.StartAsync();
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Core auto-start failed: {ex.Message}"); }
            });

            // 启动时自动检查更新
            if (appSettings.AutoCheckUpdate)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 等待5秒让应用初始化完成
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        var updateService = ServiceLocator.Get<Services.UpdateService>();
                        var update = await updateService.CheckForUpdateAsync();
                        if (update != null)
                        {
                            var notification = ServiceLocator.Get<Services.NotificationService>();
                            notification.Info(
                                Services.LocalizationHelper.GetString("AppUpdateFound.Text"),
                                string.Format(Services.LocalizationHelper.GetString("AppUpdateMsg.Text"), update.TagName));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Auto update check failed: {ex.Message}");
                    }
                });
            }

            // 恢复之前保存的导航页面
            if (!string.IsNullOrEmpty(_savedNavigationTag))
            {
                var mainWindow = CurrentWindow as MainWindow;
                mainWindow?.NavigateTo(_savedNavigationTag);
                _savedNavigationTag = null;
            }
        }

        // TUN 变更（仪表盘切换 / 托盘项 / 设置页）统一入口：开启弹 UAC 提权 + 经 Helper 重启核心
        private static bool _tunWatcherSuppressed;

        private static async Task ApplyTunChangeAsync()
        {
            try
            {
                var appSettings = ServiceLocator.Get<Models.AppSettings>();
                var clash = ServiceLocator.Get<Services.IClashService>();
                var helper = ServiceLocator.Get<Services.HelperServiceManager>();
                var notification = ServiceLocator.Get<Services.NotificationService>();

                var enabling = appSettings.TunMode; // 仅 TunMode 决定是否提权；TunStack 仅重建

                if (enabling)
                {
                    // 开启 TUN 必须提权：未注册时 RegisterServiceAsync 内部弹 UAC
                    var registered = await helper.RegisterServiceAsync();
                    if (!registered)
                    {
                        // 用户拒绝或 exe 缺失：回退开关，避免 UI 与实际状态不一致
                        _tunWatcherSuppressed = true;
                        appSettings.TunMode = false;
                        _tunWatcherSuppressed = false;
                        notification.Warning(
                            Services.LocalizationHelper.GetString("TunAdminRequired.Title"),
                            Services.LocalizationHelper.GetString("TunAdminRequired.Msg"));
                        return;
                    }
                }

                // 重建核心进程以套用 TUN 配置（开启经 Helper 以 SYSTEM 拉起，可建虚拟网卡；关闭则普通启动）
                await clash.RestartAsync();

                // 校准 TUN 开关 UI（实际网卡状态）
                await ServiceLocator.Get<ViewModels.DashboardViewModel>().SyncTunStateAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyTunChange error: {ex.Message}");
            }
        }

        public static void RecreateMainWindow(string navigationTag)
    {
        _savedNavigationTag = navigationTag;

        var oldWindow = CurrentWindow as MainWindow;
        if (oldWindow == null) return;

        oldWindow.DispatcherQueue.TryEnqueue(async () =>
        {
            await oldWindow.PerformCleanupAsync();

            CurrentWindow = new MainWindow();
            CurrentWindow.Activate();

            oldWindow.Close();
        });
    }
    }
}
