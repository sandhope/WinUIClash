using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WinUIClash.Services;
using WinUIClash.ViewModels;

namespace WinUIClash;

/// <summary>
/// 简易服务定位器 — 基于 Microsoft.Extensions.DependencyInjection
/// </summary>
public static class ServiceLocator
{
    private static ServiceProvider? _provider;

    public static IServiceProvider Provider =>
        _provider ?? throw new InvalidOperationException("ServiceLocator 尚未初始化，请先调用 Build()");

    public static void Build(StringResources stringResources)
    {
        var services = new ServiceCollection();

        // ── 日志 ──
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // ── 服务 ──
        services.AddSingleton<Models.AppSettings>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<SystemProxyService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<AutoLaunchService>();
        services.AddSingleton<CoreProcessService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<ProfileStorageService>();
        services.AddSingleton<ConfigBuildService>();
        services.AddSingleton<ConfigValidationService>();
        services.AddSingleton<CoreDownloadService>();
        services.AddSingleton<GeoResourceService>();
        services.AddSingleton<HttpClashService>();
        services.AddSingleton<HelperServiceManager>();
        services.AddSingleton<ClashOrchestrator>();
        services.AddSingleton<IClashService>(sp => sp.GetRequiredService<ClashOrchestrator>());
        services.AddSingleton(stringResources);
        services.AddSingleton<LocalizationService>();

        // ── ViewModel ──
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ProxiesViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<ConnectionsViewModel>();
        services.AddSingleton<RequestsViewModel>();
        services.AddSingleton<ResourcesViewModel>();
        services.AddSingleton<ToolsViewModel>();

        services.AddSingleton<ViewModels.Settings.LanguageSettingsViewModel>();
        services.AddSingleton<ViewModels.Settings.BasicConfigViewModel>();
        services.AddSingleton<ViewModels.Settings.AppSettingsViewModel>();
        services.AddSingleton<ViewModels.Settings.ThemeSettingsViewModel>();

        _provider = services.BuildServiceProvider();
    }

    public static T Get<T>() where T : notnull => Provider.GetRequiredService<T>();
}
