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
            ServiceLocator.Build();

            // 加载持久化设置
            var settingsService = ServiceLocator.Get<Services.SettingsService>();
            settingsService.Load();
            settingsService.EnableAutoSave();

            // 初始化系统代理
            var appSettings = ServiceLocator.Get<Models.AppSettings>();
            var proxyService = ServiceLocator.Get<Services.SystemProxyService>();
            proxyService.ApplyCurrentState();
            proxyService.WatchSettings();
            if (appSettings.SystemProxy && appSettings.ProxyGuardEnabled)
            {
                proxyService.StartGuard();
            }

            // 监听 TUN 模式变化，实时调用核心 API
            appSettings.PropertyChanged += async (s, e) =>
            {
                if (e.PropertyName == nameof(Models.AppSettings.TunMode) ||
                    e.PropertyName == nameof(Models.AppSettings.TunStack))
                {
                    try
                    {
                        var clash = ServiceLocator.Get<Services.IClashService>();
                        if (clash.CoreState == Models.CoreState.Running)
                        {
                            await clash.SetTunEnabledAsync(appSettings.TunMode);
                            if (appSettings.TunMode)
                                await clash.SetTunStackAsync(appSettings.TunStack);
                        }
                    }
                    catch { }
                }
            };

            CurrentWindow = new MainWindow();
            CurrentWindow.Activate();

            // 应用主题设置（明暗模式 + 主题色）
            ViewModels.Settings.ThemeSettingsViewModel.InitializeTheme();

            // 应用语言设置
            var lang = appSettings.Language;
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;

            // 静默启动：如果命令行包含 --silent 或设置中启用了静默启动，则最小化到托盘
            var cmdArgs = Environment.GetCommandLineArgs();
            bool isSilent = appSettings.SilentLaunch ||
                            cmdArgs.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));
            if (isSilent)
            {
                CurrentWindow.AppWindow.Hide();
            }

            // 如果设置了自动运行，启动 Clash 核心
            if (appSettings.AutoRun)
            {
                var coreService = ServiceLocator.Get<Services.CoreProcessService>();

                // Apply custom binary path if set
                if (!string.IsNullOrWhiteSpace(appSettings.CoreBinaryPath))
                    coreService.SetBinaryPath(appSettings.CoreBinaryPath);

                _ = Task.Run(async () =>
                {
                    try { await coreService.StartAsync(); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Core auto-start failed: {ex.Message}"); }
                });
            }

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
        }
    }
}
