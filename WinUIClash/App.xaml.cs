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

            // 监听 TUN 模式/协议栈变化：
            // 核心始终由 SYSTEM 服务拉起，TUN 切换仅 PATCH /configs，绝不重启核心、绝不弹 UAC。
            // 仅当核心处于异常的用户态降级（服务不可用）时，开 TUN 会返回 false，由 UI 提示并回退开关。
            appSettings.PropertyChanged += async (s, e) =>
            {
                if (e.PropertyName == nameof(Models.AppSettings.TunMode) ||
                    e.PropertyName == nameof(Models.AppSettings.TunStack))
                {
                    if (_tunWatcherSuppressed) return;
                    await ApplyTunChangeAsync(e.PropertyName);
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
            // 若 TUN 已开启且 Helper Service 已安装（常驻），则经 SYSTEM 拉起、不弹 UAC；
            // 仅“从未安装且 TUN 开启”的首次会弹一次 UAC（安装服务），之后不再弹。
            var clash = ServiceLocator.Get<Services.IClashService>();
            _ = Task.Run(async () =>
            {
                try
                {
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

        // TUN 变更（仪表盘切换 / 托盘项 / 设置页）统一入口
        private static bool _tunWatcherSuppressed;

        private static async Task ApplyTunChangeAsync(string? changedProperty)
        {
            try
            {
                var appSettings = ServiceLocator.Get<Models.AppSettings>();
                var clash = ServiceLocator.Get<Services.IClashService>();
                var notification = ServiceLocator.Get<Services.NotificationService>();

                bool enabling = appSettings.TunMode;

                if (changedProperty == nameof(Models.AppSettings.TunMode))
                {
                    // 开/关 TUN：SetTunEnabledAsync 仅 PATCH /configs；核心常驻、绝不退出、绝不弹 UAC。
                    // 若核心处于用户态降级（服务不可用），返回 false → 下方提示并回退开关。
                    var ok = await clash.SetTunEnabledAsync(enabling);
                    if (!ok && enabling)
                    {
                        // UAC 被拒或 exe 缺失：回退开关，避免 UI 与实际状态不一致
                        _tunWatcherSuppressed = true;
                        appSettings.TunMode = false;
                        _tunWatcherSuppressed = false;
                        notification.Warning(
                            Services.LocalizationHelper.GetString("TunAdminRequired.Title"),
                            Services.LocalizationHelper.GetString("TunAdminRequired.Msg"));
                        return;
                    }
                }
                else if (changedProperty == nameof(Models.AppSettings.TunStack))
                {
                    // 切换 TUN 协议栈：mihomo 运行时通常无法热切换 stack，需重启核心才能套用。
                    // 但核心必须常驻、绝不退出，因此此处不做 RestartAsync；
                    // 新 stack 在下次核心启动（应用重启）后生效。这里仅 best-effort PATCH 一次，
                    // 部分 mihomo 版本可在运行时套用 stack。
                    if (appSettings.TunMode)
                        await clash.SetTunStackAsync(appSettings.TunStack);
                }

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
