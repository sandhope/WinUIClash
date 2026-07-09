using Microsoft.Extensions.DependencyInjection;
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

    public static void Build()
    {
        var services = new ServiceCollection();

        // ── 服务 ──
        services.AddSingleton<IClashService, MockClashService>();
        services.AddSingleton<Models.AppSettings>();
        services.AddSingleton<Services.SettingsService>();
        services.AddSingleton<Services.SystemProxyService>();
        services.AddSingleton<Services.CoreProcessService>();

        // ── ViewModel ──
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ProxiesViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<ConnectionsViewModel>();
        services.AddSingleton<RequestsViewModel>();
        services.AddSingleton<ResourcesViewModel>();
        services.AddSingleton<RulesViewModel>();
        services.AddSingleton<ToolsViewModel>();

        _provider = services.BuildServiceProvider();
    }

    public static T Get<T>() where T : notnull => Provider.GetRequiredService<T>();
}
