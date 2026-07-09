using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// 真实的 ClashMeta REST API 客户端实现
/// 对接 ClashMeta 的 :9090 RESTful API
/// </summary>
public class HttpClashService : IClashService, IDisposable
{
    private readonly HttpClient _http;
    private ClientWebSocket? _trafficWs;
    private ClientWebSocket? _logWs;
    private CancellationTokenSource? _trafficCts;
    private CancellationTokenSource? _logCts;

    private CoreState _coreState = CoreState.Stopped;
    private Traffic _currentTraffic = new();
    private Traffic _totalTraffic = new();
    private OutboundMode _outboundMode = OutboundMode.Rule;

    public CoreState CoreState => _coreState;
    public event Action<Traffic>? TrafficUpdated;
    public event Action<CoreState>? CoreStateChanged;
    public event Action<LogEntry>? LogReceived;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public HttpClashService()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:9090"),
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public void SetApiEndpoint(string host, int port, string? secret = null)
    {
        _http.BaseAddress = new Uri($"http://{host}:{port}");
        if (!string.IsNullOrEmpty(secret))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", secret);
        }
    }

    // ── 生命周期 ──

    public async Task StartAsync()
    {
        try
        {
            await CheckHealthAsync();
            _coreState = CoreState.Running;
            CoreStateChanged?.Invoke(_coreState);
        }
        catch
        {
            _coreState = CoreState.Stopped;
            CoreStateChanged?.Invoke(_coreState);
            throw;
        }
    }

    public Task StopAsync()
    {
        StopTrafficStream();
        _coreState = CoreState.Stopped;
        CoreStateChanged?.Invoke(_coreState);
        return Task.CompletedTask;
    }

    private async Task CheckHealthAsync()
    {
        var resp = await _http.GetAsync("/version");
        resp.EnsureSuccessStatusCode();
    }

    public async Task<string> GetVersionAsync()
    {
        var json = await _http.GetStringAsync("/version");
        var dto = JsonSerializer.Deserialize<VersionDto>(json, JsonOpts);
        return dto?.Version ?? dto?.Meta ?? "unknown";
    }

    // ── 流量 ──

    public Traffic GetCurrentTraffic() => _currentTraffic;
    public Traffic GetTotalTraffic() => _totalTraffic;

    public async Task ResetTrafficAsync()
    {
        _totalTraffic = new Traffic();
        await _http.PostAsync("/traffic/reset", null);
    }

    public async Task StartTrafficStreamAsync()
    {
        StopTrafficStream();
        _trafficCts = new CancellationTokenSource();
        _trafficWs = new ClientWebSocket();

        var wsUri = new Uri($"ws://{_http.BaseAddress!.Authority}/traffic?token={GetToken()}");
        try
        {
            await _trafficWs.ConnectAsync(wsUri, _trafficCts.Token);

            var buffer = new byte[1024];
            while (_trafficWs.State == WebSocketState.Open && !_trafficCts.IsCancellationRequested)
            {
                var result = await _trafficWs.ReceiveAsync(buffer, _trafficCts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var dto = JsonSerializer.Deserialize<TrafficDto>(json, JsonOpts);
                if (dto != null)
                {
                    _currentTraffic = new Traffic
                    {
                        Up = dto.Up,
                        Down = dto.Down,
                        Timestamp = DateTime.Now,
                    };
                    _totalTraffic.Up += dto.Up;
                    _totalTraffic.Down += dto.Down;
                    TrafficUpdated?.Invoke(_currentTraffic);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    private void StopTrafficStream()
    {
        _trafficCts?.Cancel();
        _trafficWs?.Dispose();
        _trafficWs = null;
    }

    // ── 出站模式 ──

    public OutboundMode GetOutboundMode() => _outboundMode;

    public async Task SetOutboundModeAsync(OutboundMode mode)
    {
        var modeStr = mode switch
        {
            OutboundMode.Global => "global",
            OutboundMode.Direct => "direct",
            _ => "rule",
        };

        var json = JsonSerializer.Serialize(new { mode = modeStr });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        await _http.PatchAsync("/configs", content);
        _outboundMode = mode;
    }

    // ── 代理 ──

    public async Task<IReadOnlyList<ProxyGroup>> GetProxyGroupsAsync()
    {
        var json = await _http.GetStringAsync("/proxies");
        var dto = JsonSerializer.Deserialize<ProxiesResponse>(json, JsonOpts);
        if (dto?.Proxies == null) return Array.Empty<ProxyGroup>();

        var groups = new List<ProxyGroup>();
        foreach (var (name, info) in dto.Proxies)
        {
            if (info.Type == "Selector" || info.Type == "URLTest" ||
                info.Type == "Fallback" || info.Type == "LoadBalance")
            {
                var group = new ProxyGroup
                {
                    Name = name,
                    Type = Enum.TryParse<ProxyGroupType>(info.Type, true, out var gType) ? gType : ProxyGroupType.Selector,
                    Now = info.Now ?? "",
                };
                if (info.All != null)
                {
                    foreach (var proxyName in info.All)
                    {
                        if (dto.Proxies.TryGetValue(proxyName, out var proxyInfo))
                        {
                            group.Proxies.Add(new Proxy
                            {
                                Name = proxyName,
                                Type = proxyInfo.Type ?? "Unknown",
                                Delay = proxyInfo.History?.LastOrDefault()?.Delay ?? 0,
                            });
                        }
                        else
                        {
                            group.Proxies.Add(new Proxy { Name = proxyName, Type = "Unknown" });
                        }
                    }
                }
                groups.Add(group);
            }
        }
        return groups;
    }

    public async Task ChangeProxyAsync(string groupName, string proxyName)
    {
        var json = JsonSerializer.Serialize(new { name = proxyName });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        await _http.PutAsync($"/proxies/{Uri.EscapeDataString(groupName)}", content);
    }

    public async Task<int> TestDelayAsync(string proxyName, string? testUrl = null)
    {
        var url = testUrl ?? "https://www.gstatic.com/generate_204";
        var resp = await _http.GetAsync(
            $"/proxies/{Uri.EscapeDataString(proxyName)}/delay?url={Uri.EscapeDataString(url)}&timeout=5000");
        if (!resp.IsSuccessStatusCode) return 0;

        var json = await resp.Content.ReadAsStringAsync();
        var dto = JsonSerializer.Deserialize<DelayResponse>(json, JsonOpts);
        return dto?.Delay ?? 0;
    }

    // ── 配置 ──

    public Task<IReadOnlyList<Profile>> GetProfilesAsync()
    {
        // Clash API doesn't directly expose profiles list; this would come from
        // local config management. For now return empty - profiles are managed locally.
        return Task.FromResult<IReadOnlyList<Profile>>(Array.Empty<Profile>());
    }

    public Task AddProfileAsync(Profile profile) => Task.CompletedTask;
    public Task UpdateProfileAsync(Profile profile) => Task.CompletedTask;
    public Task DeleteProfileAsync(string profileId) => Task.CompletedTask;
    public Task SwitchProfileAsync(string profileId) => Task.CompletedTask;

    public async Task SyncProfileAsync(string profileId)
    {
        // Trigger config reload via PUT /configs
        var json = JsonSerializer.Serialize(new { path = "" });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        await _http.PutAsync("/configs", content);
    }

    // ── 连接 ──

    public async Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync()
    {
        var json = await _http.GetStringAsync("/connections");
        var dto = JsonSerializer.Deserialize<ConnectionsResponse>(json, JsonOpts);
        if (dto?.Connections == null) return Array.Empty<ConnectionInfo>();

        return dto.Connections.Select(c => new ConnectionInfo
        {
            Id = c.Id,
            Start = c.Start ?? DateTime.Now,
            Download = c.Download,
            Upload = c.Upload,
            Metadata = new ConnectionMetadata
            {
                Network = c.Metadata?.Network ?? "",
                SourceIP = c.Metadata?.SourceIP ?? "",
                SourcePort = c.Metadata?.SourcePort ?? "",
                DestinationIP = c.Metadata?.DestinationIP ?? "",
                DestinationPort = c.Metadata?.DestinationPort ?? "",
                Host = c.Metadata?.Host ?? "",
                DnsMode = c.Metadata?.DnsMode ?? "",
                Process = c.Metadata?.ProcessPath ?? "",
            },
            Chains = new System.Collections.ObjectModel.ObservableCollection<string>(c.Chains ?? new List<string>()),
            Rule = c.Rule ?? "",
            RulePayload = c.RulePayload ?? "",
        }).ToList();
    }

    public async Task CloseConnectionAsync(string connectionId)
    {
        await _http.DeleteAsync($"/connections/{connectionId}");
    }

    public async Task CloseAllConnectionsAsync()
    {
        await _http.DeleteAsync("/connections");
    }

    // ── 日志 ──

    public async Task StartLogAsync()
    {
        _logCts = new CancellationTokenSource();
        _logWs = new ClientWebSocket();

        var wsUri = new Uri($"ws://{_http.BaseAddress!.Authority}/logs?token={GetToken()}&level=info");
        try
        {
            await _logWs.ConnectAsync(wsUri, _logCts.Token);

            var buffer = new byte[4096];
            while (_logWs.State == WebSocketState.Open && !_logCts.IsCancellationRequested)
            {
                var result = await _logWs.ReceiveAsync(buffer, _logCts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var dto = JsonSerializer.Deserialize<LogDto>(json, JsonOpts);
                if (dto != null)
                {
                    var level = Enum.TryParse<LogLevel>(dto.Type, true, out var l) ? l : LogLevel.Info;
                    LogReceived?.Invoke(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = level,
                        Payload = dto.Payload ?? "",
                    });
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    public Task StopLogAsync()
    {
        _logCts?.Cancel();
        _logWs?.Dispose();
        _logWs = null;
        return Task.CompletedTask;
    }

    // ── 网络检测 ──

    public async Task<IpInfo> GetIpInfoAsync()
    {
        // Use a public API to check IP
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = await client.GetStringAsync("https://api.ip.sb/geoip");
            var dto = JsonSerializer.Deserialize<IpGeoDto>(json, JsonOpts);
            return new IpInfo
            {
                Ip = dto?.Ip ?? "Unknown",
                CountryCode = dto?.CountryCode ?? "",
            };
        }
        catch
        {
            return new IpInfo { Ip = "Unknown", CountryCode = "" };
        }
    }

    // ── 外部提供者 ──

    public async Task<IReadOnlyList<ExternalProvider>> GetExternalProvidersAsync()
    {
        var result = new List<ExternalProvider>();

        // Fetch proxy providers
        try
        {
            var json = await _http.GetStringAsync("/providers/proxies");
            var dto = JsonSerializer.Deserialize<ProvidersResponse>(json, JsonOpts);
            if (dto?.Providers != null)
            {
                result.AddRange(dto.Providers.Select(p => new ExternalProvider
                {
                    Name = p.Name ?? "",
                    Type = p.Type ?? "",
                    VehicleType = p.VehicleType ?? "",
                    Count = p.Count,
                    UpdateAt = p.UpdatedAt,
                    Category = "proxy",
                }));
            }
        }
        catch { /* proxy providers unavailable */ }

        // Fetch rule providers
        try
        {
            var json = await _http.GetStringAsync("/providers/rules");
            var dto = JsonSerializer.Deserialize<ProvidersResponse>(json, JsonOpts);
            if (dto?.Providers != null)
            {
                result.AddRange(dto.Providers.Select(p => new ExternalProvider
                {
                    Name = p.Name ?? "",
                    Type = p.Type ?? "",
                    VehicleType = p.VehicleType ?? "",
                    Count = p.Count,
                    UpdateAt = p.UpdatedAt,
                    Category = "rule",
                }));
            }
        }
        catch { /* rule providers unavailable */ }

        return result;
    }

    public async Task UpdateExternalProviderAsync(string name, string category = "proxy")
    {
        var endpoint = category == "rule"
            ? $"/providers/rules/{Uri.EscapeDataString(name)}"
            : $"/providers/proxies/{Uri.EscapeDataString(name)}";
        await _http.PutAsync(endpoint, null);
    }

    // ── 规则 ──

    public async Task<IReadOnlyList<Rule>> GetRulesAsync()
    {
        var json = await _http.GetStringAsync("/rules");
        var dto = JsonSerializer.Deserialize<RulesResponse>(json, JsonOpts);
        if (dto?.Rules == null) return Array.Empty<Rule>();

        return dto.Rules.Select(r => new Rule
        {
            Type = r.Type ?? "",
            Payload = r.Payload ?? "",
            Proxy = r.Proxy ?? "",
            Size = r.Size,
        }).ToList();
    }

    // ── 内存 ──

    public async Task<long> GetCoreMemoryAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("/memory");
            var dto = JsonSerializer.Deserialize<MemoryDto>(json, JsonOpts);
            return dto?.Inuse ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    public async Task ForceGcAsync()
    {
        await _http.PostAsync("/memory/force-gc", null);
    }

    // ── 辅助方法 ──

    private string GetToken()
    {
        var auth = _http.DefaultRequestHeaders.Authorization;
        return auth?.Parameter ?? "";
    }

    public void Dispose()
    {
        StopTrafficStream();
        _logCts?.Cancel();
        _logWs?.Dispose();
        _http.Dispose();
    }

    // ── DTO 类型 ──

    private class TrafficDto
    {
        public long Up { get; set; }
        public long Down { get; set; }
    }

    private class ProxiesResponse
    {
        public Dictionary<string, ProxyDto>? Proxies { get; set; }
    }

    private class ProxyDto
    {
        public string? Type { get; set; }
        public string? Now { get; set; }
        public List<string>? All { get; set; }
        public List<HistoryDto>? History { get; set; }
    }

    private class HistoryDto
    {
        public int Delay { get; set; }
    }

    private class DelayResponse
    {
        public int Delay { get; set; }
    }

    private class ConnectionsResponse
    {
        public List<ConnectionDto>? Connections { get; set; }
    }

    private class ConnectionDto
    {
        public string Id { get; set; } = "";
        public DateTime? Start { get; set; }
        public long Download { get; set; }
        public long Upload { get; set; }
        public ConnectionMetaDto? Metadata { get; set; }
        public List<string>? Chains { get; set; }
        public string? Rule { get; set; }
        public string? RulePayload { get; set; }
    }

    private class ConnectionMetaDto
    {
        public string? Network { get; set; }
        public string? Type { get; set; }
        public string? SourceIP { get; set; }
        public string? SourcePort { get; set; }
        public string? DestinationIP { get; set; }
        public string? DestinationPort { get; set; }
        public string? Host { get; set; }
        public string? DnsMode { get; set; }
        public string? ProcessPath { get; set; }
    }

    private class LogDto
    {
        public string? Type { get; set; }
        public string? Payload { get; set; }
    }

    private class IpGeoDto
    {
        public string? Ip { get; set; }
        public string? Country { get; set; }
        public string? CountryCode { get; set; }
        public string? Isp { get; set; }
    }

    private class ProvidersResponse
    {
        public List<ProviderDto>? Providers { get; set; }
    }

    private class ProviderDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? VehicleType { get; set; }
        public int Count { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private class RulesResponse
    {
        public List<RuleDto>? Rules { get; set; }
    }

    private class RuleDto
    {
        public string? Type { get; set; }
        public string? Payload { get; set; }
        public string? Proxy { get; set; }
        public int Size { get; set; }
    }

    private class MemoryDto
    {
        public long Inuse { get; set; }
    }

    private class VersionDto
    {
        public string? Version { get; set; }
        public string? Meta { get; set; }
    }
}
