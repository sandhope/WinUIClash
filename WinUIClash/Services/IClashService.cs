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

    // ── 流量 ──
    Traffic GetCurrentTraffic();
    Traffic GetTotalTraffic();
    Task ResetTrafficAsync();

    // ── 出站模式 ──
    OutboundMode GetOutboundMode();
    Task SetOutboundModeAsync(OutboundMode mode);

    // ── 代理 ──
    Task<IReadOnlyList<ProxyGroup>> GetProxyGroupsAsync();
    Task ChangeProxyAsync(string groupName, string proxyName);
    Task<int> TestDelayAsync(string proxyName, string? testUrl = null);

    // ── 配置 ──
    Task<IReadOnlyList<Profile>> GetProfilesAsync();
    Task AddProfileAsync(Profile profile);
    Task UpdateProfileAsync(Profile profile);
    Task DeleteProfileAsync(string profileId);
    Task SwitchProfileAsync(string profileId);
    Task SyncProfileAsync(string profileId);

    // ── 连接 ──
    Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync();
    Task CloseConnectionAsync(string connectionId);
    Task CloseAllConnectionsAsync();

    // ── 日志 ──
    event Action<LogEntry>? LogReceived;
    Task StartLogAsync();
    Task StopLogAsync();

    // ── 网络检测 ──
    Task<IpInfo> GetIpInfoAsync();

    // ── 外部提供者 ──
    Task<IReadOnlyList<ExternalProvider>> GetExternalProvidersAsync();
    Task UpdateExternalProviderAsync(string name);

    // ── 规则 ──
    Task<IReadOnlyList<Rule>> GetRulesAsync();

    // ── 内存 ──
    Task<long> GetCoreMemoryAsync();
    Task ForceGcAsync();

    // ── 事件 ──
    event Action<Traffic>? TrafficUpdated;
    event Action<CoreState>? CoreStateChanged;
}
