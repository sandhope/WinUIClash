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
            var proxyService = ServiceLocator.Get<Services.SystemProxyService>();
            proxyService.ApplyCurrentState();
            proxyService.WatchSettings();

            CurrentWindow = new MainWindow();
            CurrentWindow.Activate();

            // 应用主题设置（明暗模式 + 主题色）
            ViewModels.Settings.ThemeSettingsViewModel.InitializeTheme();

            // 应用语言设置
            var appSettings = ServiceLocator.Get<Models.AppSettings>();
            var lang = appSettings.Language;
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;

            // 如果设置了自动运行，启动 Clash 核心
            if (appSettings.AutoRun)
            {
                var coreService = ServiceLocator.Get<Services.CoreProcessService>();
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
                            notification.Info("发现新版本", $"WinUIClash {update.TagName} 已发布，前往工具页查看。");
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
