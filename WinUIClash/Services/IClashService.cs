using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// ClashMeta 核心服务接口 — 封装与后端引擎的所有交互
/// </summary>
public interface IClashService
{
    // ── 生命周期 ──
    CoreState CoreState { get; }
    Task StartAsync();
    Task StopAsync();
    Task<string> GetVersionAsync();

    // ── 流量 ──
    Traffic GetCurrentTraffic();
    Traffic GetTotalTraffic();
    Task ResetTrafficAsync();
    Task StartTrafficStreamAsync();

    // ── 出站模式 ──
    OutboundMode GetOutboundMode();
    Task SetOutboundModeAsync(OutboundMode mode);

    // ── TUN 模式 ──
    Task<bool> GetTunEnabledAsync();
    Task SetTunEnabledAsync(bool enabled);
    Task SetTunStackAsync(string stack);

    // ── 代理 ──
    Task<IReadOnlyList<ProxyGroup>> GetProxyGroupsAsync();
    Task ChangeProxyAsync(string groupName, string proxyName);
    Task<int> TestDelayAsync(string proxyName, string? testUrl = null);
    Task<Dictionary<string, int>> TestGroupDelayAsync(string groupName, string? testUrl = null);

    // ── 配置 ──
    Task<IReadOnlyList<Profile>> GetProfilesAsync();
    Task AddProfileAsync(Profile profile);
    Task UpdateProfileAsync(Profile profile);
    Task DeleteProfileAsync(string profileId);
    Task SwitchProfileAsync(string profileId, string configPath = "");
    Task SyncProfileAsync(string profileId, string? url = null, string configPath = "");

    // ── 连接 ──
    Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync();
    Task CloseConnectionAsync(string connectionId);
    Task CloseAllConnectionsAsync();

    // ── 日志 ──
    event Action<LogEntry>? LogReceived;
    Task StartLogAsync(string level = "info");
    Task StopLogAsync();

    // ── 网络检测 ──
    Task<IpInfo> GetIpInfoAsync();
    Task<string> QueryDnsAsync(string name, string type = "A");

    // ── 外部提供者 ──
    Task<IReadOnlyList<ExternalProvider>> GetExternalProvidersAsync();
    Task UpdateExternalProviderAsync(string name, string category = "proxy");

    // ── GeoIP/GeoSite 数据库 ──
    Task UpdateGeoDatabaseAsync(string name);

    // ── 运行时配置 ──
    Task PatchCoreConfigAsync(AppSettings settings);

    // ── 提供者健康检查 ──
    Task HealthCheckProviderAsync(string name, string category = "proxy");

    // ── 规则 ──
    Task<IReadOnlyList<Rule>> GetRulesAsync();

    // ── 内存 ──
    Task<long> GetCoreMemoryAsync();
    Task ForceGcAsync();

    // ── 缓存 ──
    Task FlushFakeIpCacheAsync();

    // ── 事件 ──
    event Action<Traffic>? TrafficUpdated;
    event Action<CoreState>? CoreStateChanged;
    event Action<OutboundMode>? OutboundModeChanged;
}
