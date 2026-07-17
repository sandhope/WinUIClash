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
using WinUIClash.ViewModels.Settings;

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

                    ThemeSettingsViewModel.ApplyAccentBrushesTo(dialog.Resources);

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
        /// 启动核心：在 UI 线程完成端口冲突预检与弹窗，重 IO 的启动放到后台。
        /// 参考 ClashSharp：检测与弹窗都在 UI 线程、XamlRoot 已就绪，避免后台线程 + DispatcherQueue 转发导致弹窗失败。
        /// </summary>
        private static async Task StartCoreWithConflictCheckAsync()
        {
            var orchestrator = ServiceLocator.Get<Services.ClashOrchestrator>();

            // 等待主窗口 XamlRoot 就绪（首次布局完成后才有），否则 ContentDialog 的 XamlRoot 为 null 会失败。
            var xamlRoot = await WaitForXamlRootAsync();
            if (xamlRoot == null)
            {
                // 拿不到 XamlRoot 就直接后台启动（不弹窗）
                _ = Task.Run(async () =>
                {
                    try { await orchestrator.StartAsync(); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Core auto-start failed: {ex.Message}"); }
                });
                return;
            }

            // 首次启动引导（评审 §4.1）：在端口冲突预检前、UI 线程串行弹出，完成/跳过后置位标记不再弹。
            var appSettings = ServiceLocator.Get<Models.AppSettings>();
            if (!appSettings.HasCompletedFirstRunGuide)
            {
                try
                {
                    var guide = new Views.FirstRunGuideDialog { XamlRoot = xamlRoot };
                    // ThemeSettingsViewModel.ApplyAccentBrushesTo(guide.Resources);
                    await guide.ShowAsync();
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"First-run guide failed: {ex.Message}"); }
                finally
                {
                    appSettings.HasCompletedFirstRunGuide = true; // 属性变更触发自动保存
                }
            }

            // 端口冲突预检（IO 部分放后台，不阻塞 UI）
            var conflict = await Task.Run(() => orchestrator.DetectPortConflictAsync());
            int[]? pidsToKill = null;
            if (conflict != null)
            {
                // 直接在 UI 线程弹窗，无需 DispatcherQueue 转发
                var resolution = await ShowPortConflictDialogAsync(xamlRoot, conflict);
                if (resolution == Services.ConflictResolution.KillProcess)
                    pidsToKill = conflict.Pids;
            }

            // 启动核心（重 IO），放后台
            _ = Task.Run(async () =>
            {
                try { await orchestrator.StartAsync(pidsToKill); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Core auto-start failed: {ex.Message}"); }
            });
        }

        /// <summary>等待主窗口 XamlRoot 就绪（最多约 5 秒）。</summary>
        private static async Task<XamlRoot?> WaitForXamlRootAsync()
        {
            for (int i = 0; i < 100; i++)
            {
                if (CurrentWindow?.Content is FrameworkElement fe && fe.XamlRoot != null)
                    return fe.XamlRoot;
                await Task.Delay(50);
            }
            return null;
        }

        /// <summary>在 UI 线程直接弹出端口冲突对话框，返回用户的选择。</summary>
        private static async Task<Services.ConflictResolution> ShowPortConflictDialogAsync(XamlRoot xamlRoot, Services.PortConflictInfo info)
        {
            var ports = string.Join(", ", info.Ports);
            var dialog = new ContentDialog
            {
                Title = Services.LocalizationHelper.GetString("PortConflictTitle.Text"),
                Content = string.Format(Services.LocalizationHelper.GetString("PortConflictMsg.Text"), ports),
                PrimaryButtonText = Services.LocalizationHelper.GetString("PortConflictKill.Content"),
                CloseButtonText = Services.LocalizationHelper.GetString("PortConflictClose.Content"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
            };

            ThemeSettingsViewModel.ApplyAccentBrushesTo(dialog.Resources);

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary
                ? Services.ConflictResolution.KillProcess
                : Services.ConflictResolution.Proceed;
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
            Resources.Add("BoolToOpacityConverter", new Converters.BoolToOpacityConverter());
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
            Resources.Add("StringEqualsConverter", new Converters.StringEqualsConverter());

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

            // TUN 模式/协议栈：与 FlClash 一致，开关仅是“用户设置”。
            // 切换只持久化偏好（SettingsService 自动保存），不在此处 PATCH 核心、
            // 不创建/删除虚拟网卡（避免点击开关即操作网卡）。UI 开关状态由
            // DashboardViewModel 订阅 AppSettings.TunMode 的 PropertyChanged 同步；

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
            // 启动核心：端口冲突预检 + 弹窗都在 UI 线程做（参考 ClashSharp 的 OnContentFrameLoaded 模式，
            // 全程 UI 线程、XamlRoot 已就绪，避免后台线程 + DispatcherQueue 转发导致弹窗失败）。
            // 重 IO 的启动本身仍放后台，不阻塞 UI。
            _ = StartCoreWithConflictCheckAsync();

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

        // TUN 开关仅作为“用户设置”由 DashboardViewModel 订阅 AppSettings.TunMode 同步 UI；

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
