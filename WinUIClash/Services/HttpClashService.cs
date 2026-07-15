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
    private ClientWebSocket? _memoryWs;
    private CancellationTokenSource? _trafficCts;
    private CancellationTokenSource? _logCts;
    private CancellationTokenSource? _memoryCts;

    private CoreState _coreState = CoreState.Stopped;
    private Traffic _currentTraffic = new();
    private Traffic _totalTraffic = new();
    private OutboundMode _outboundMode = OutboundMode.Rule;
    private long _currentMemory = 0;

    public CoreState CoreState => _coreState;
    public event Action<Traffic>? TrafficUpdated;
    public event Action<CoreState>? CoreStateChanged;
    public event Action<OutboundMode>? OutboundModeChanged;
    public event Action<LogEntry>? LogReceived;
    public event Action<long>? MemoryUpdated;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = AppJsonContext.Default,
    };

    public HttpClashService()
    {
        _http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri("http://127.0.0.1:9090"),
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public void SetApiEndpoint(string host, int port, string? secret = null)
    {
        // HttpClient 在发送第一个请求后不能再修改 BaseAddress 和 DefaultRequestHeaders。
        // 端口固定为 9090，因此只设置一次；后续重启核心不再重复设置。
        if (_http.BaseAddress == null)
        {
            _http.BaseAddress = new Uri($"http://{host}:{port}");
            if (!string.IsNullOrEmpty(secret))
            {
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", secret);
            }
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

    // 对应 IClashService.ShutdownAsync：仅停止 WS 流量流（进程由 CoreProcessService 负责）
    public Task ShutdownAsync()
    {
        StopTrafficStream();
        StopMemoryStream();
        _coreState = CoreState.Stopped;
        CoreStateChanged?.Invoke(_coreState);
        return Task.CompletedTask;
    }

    // HttpClashService 为纯 REST 客户端，无进程可重启；重启由 ClashOrchestrator 协调。
    public Task RestartAsync() => Task.CompletedTask;

    private async Task CheckHealthAsync()
    {
        var resp = await _http.GetAsync("/version");
        resp.EnsureSuccessStatusCode();
    }

    public async Task<string> GetVersionAsync()
    {
        var json = await _http.GetStringAsync("/version");
        var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.VersionDto);
        return dto?.Version ?? "unknown";
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
                var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.TrafficDto);
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

    public async Task StartMemoryStreamAsync()
    {
        StopMemoryStream();
        _memoryCts = new CancellationTokenSource();
        _memoryWs = new ClientWebSocket();

        var wsUri = new Uri($"ws://{_http.BaseAddress!.Authority}/memory?token={GetToken()}");
        try
        {
            await _memoryWs.ConnectAsync(wsUri, _memoryCts.Token);

            var buffer = new byte[1024];
            while (_memoryWs.State == WebSocketState.Open && !_memoryCts.IsCancellationRequested)
            {
                var result = await _memoryWs.ReceiveAsync(buffer, _memoryCts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.MemoryDto);
                if (dto != null)
                {
                    _currentMemory = dto.Inuse;
                    MemoryUpdated?.Invoke(_currentMemory);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    private void StopMemoryStream()
    {
        _memoryCts?.Cancel();
        _memoryWs?.Dispose();
        _memoryWs = null;
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

        var json = JsonSerializer.Serialize(new ModePayload { Mode = modeStr }, AppJsonContext.Default.ModePayload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        await _http.PatchAsync("/configs", content);
        _outboundMode = mode;
        OutboundModeChanged?.Invoke(mode);
    }

    // ── TUN 模式 ──

    public async Task<bool> GetTunEnabledAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("/configs");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tun", out var tun))
                return tun.TryGetProperty("enable", out var en) && en.GetBoolean();
            return false;
        }
        catch { return false; }
    }

    public async Task<bool> SetTunEnabledAsync(bool enabled, string? stack = null)
    {
        // 发送完整 tun 配置（与 config.yaml 中的 tun 块一致），避免 mihomo PATCH 时
        // 仅合并部分字段导致 device/stack/strict-route 丢失。stack 尊重用户设置，缺省回退 mixed。
        var tunStack = string.IsNullOrWhiteSpace(stack) ? "mixed" : stack;
        var payload = JsonSerializer.Serialize(new TunConfigPayload
        {
            Tun = new TunSettings
            {
                Enable = enabled,
                Stack = tunStack,
                Device = "WinUIClash",
                AutoRoute = true,
                AutoDetectInterface = true,
                DnsHijack = ["any:53"],
                StrictRoute = true,
            }
        }, AppJsonContext.Default.TunConfigPayload);
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        await _http.PatchAsync("/configs", content);
        return true;
    }

    public async Task SetTunStackAsync(string stack)
    {
        var payload = JsonSerializer.Serialize(new TunStackPayload
        {
            Tun = new TunStackInner { Stack = stack }
        }, AppJsonContext.Default.TunStackPayload);
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        await _http.PatchAsync("/configs", content);
    }

    // ── 代理 ──

    public async Task<IReadOnlyList<ProxyGroup>> GetProxyGroupsAsync()
    {
        var json = await _http.GetStringAsync("/proxies");
        var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProxiesResponse);
        if (dto?.Proxies == null) return Array.Empty<ProxyGroup>();

        var groups = new List<ProxyGroup>();
        foreach (var (name, info) in dto.Proxies)
        {
            // FlClash 1:1：代理页 Tab 不包含 GLOBAL 组（它由出站模式选择器 Global/Rule/Direct 表示）。
            if (name.Equals("GLOBAL", StringComparison.OrdinalIgnoreCase))
                continue;

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
        var json = JsonSerializer.Serialize(new NamePayload { Name = proxyName }, AppJsonContext.Default.NamePayload);
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
        var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.DelayResponse);
        return dto?.Delay ?? 0;
    }

    public async Task<Dictionary<string, int>> TestGroupDelayAsync(string groupName, string? testUrl = null)
    {
        var url = testUrl ?? "https://www.gstatic.com/generate_204";

        // 现代 mihomo 已移除 /group/{name}/delay 端点；改为读取该组的成员节点，
        // 逐个调用正确的 /proxies/{name}/delay 进行测速。
        var json = await _http.GetStringAsync("/proxies");
        var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProxiesResponse);
        if (dto?.Proxies == null ||
            !dto.Proxies.TryGetValue(groupName, out var groupInfo) ||
            groupInfo.All == null)
        {
            return new Dictionary<string, int>();
        }

        var results = new Dictionary<string, int>(groupInfo.All.Count);
        using var throttle = new SemaphoreSlim(8);
        var tasks = groupInfo.All.Select(async node =>
        {
            await throttle.WaitAsync();
            try
            {
                var delay = await TestDelayAsync(node, url);
                lock (results) results[node] = delay;
            }
            finally
            {
                throttle.Release();
            }
        });
        await Task.WhenAll(tasks);
        return results;
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

    public async Task SwitchProfileAsync(string profileId, string configPath = "")
    {
        if (string.IsNullOrWhiteSpace(configPath)) return;

        // Normalize to absolute path
        configPath = Path.GetFullPath(configPath);
        if (!File.Exists(configPath)) return;

        // Normalize backslashes for the ClashMeta API (it expects forward slashes)
        var normalizedPath = configPath.Replace('\\', '/');
        var json = JsonSerializer.Serialize(new PathPayload { Path = normalizedPath }, AppJsonContext.Default.PathPayload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        await _http.PutAsync("/configs", content);
    }

    public async Task SyncProfileAsync(string profileId, string? url = null, string configPath = "")
    {
        // If a subscription URL is provided, download the config first
        if (!string.IsNullOrWhiteSpace(url))
        {
            var storage = new ProfileStorageService();
            var result = await storage.DownloadAndSaveAsync(profileId, url);
            configPath = result.Path;
        }

        // Reload config via PUT /configs if we have a path
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var normalizedPath = Path.GetFullPath(configPath).Replace('\\', '/');
            var json = JsonSerializer.Serialize(new PathPayload { Path = normalizedPath }, AppJsonContext.Default.PathPayload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _http.PutAsync("/configs", content);
        }
    }

    // ── 连接 ──

    public async Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync()
    {
        var json = await _http.GetStringAsync("/connections");
        var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.ConnectionsResponse);
        if (dto?.Connections == null) return Array.Empty<ConnectionInfo>();

        return dto.Connections.Select(c => new ConnectionInfo
        {
            Id = c.Id,
            Start = c.Start ?? DateTime.Now,
            Download = c.Download,
            Upload = c.Upload,
            UploadSpeed = c.UploadSpeed,
            DownloadSpeed = c.DownloadSpeed,
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

    public async Task StartLogAsync(string level = "info")
    {
        await StopLogAsync();

        _logCts = new CancellationTokenSource();
        _logWs = new ClientWebSocket();

        var wsLevel = string.IsNullOrWhiteSpace(level) ? "info" : level.ToLowerInvariant();
        var wsUri = new Uri($"ws://{_http.BaseAddress!.Authority}/logs?token={GetToken()}&level={wsLevel}");
        try
        {
            await _logWs.ConnectAsync(wsUri, _logCts.Token);

            var buffer = new byte[4096];
            while (_logWs.State == WebSocketState.Open && !_logCts.IsCancellationRequested)
            {
                var result = await _logWs.ReceiveAsync(buffer, _logCts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.LogDto);
                if (dto != null)
                {
                    var parsedLevel = Enum.TryParse<LogLevel>(dto.Type, true, out var l) ? l : LogLevel.Info;
                    LogReceived?.Invoke(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = parsedLevel,
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
        // 对齐 FlClash: IP 检测走系统代理（代理开启时经过 mihomo → 显示代理出口 IP）
        // 代理关闭时系统代理 ProxyEnable=0 → 自动直连 → 显示真实 IP
        // 多源容错: 取第一个成功响应（对齐 FlClash request.dart checkIp）
        var sources = new (string Url, Func<JsonElement, IpInfo> Parse)[]
        {
            ("https://api.ip.sb/geoip", d => new IpInfo
            {
                Ip = d.TryGetProperty("ip", out var ip1) ? ip1.GetString() ?? "" : "",
                CountryCode = d.TryGetProperty("country_code", out var cc1) ? cc1.GetString() ?? "" : "",
            }),
            ("https://ipinfo.io/json", d => new IpInfo
            {
                Ip = d.TryGetProperty("ip", out var ip2) ? ip2.GetString() ?? "" : "",
                CountryCode = d.TryGetProperty("country", out var cc2) ? cc2.GetString() ?? "" : "",
            }),
            ("https://ipwho.is", d => new IpInfo
            {
                Ip = d.TryGetProperty("ip", out var ip3) ? ip3.GetString() ?? "" : "",
                CountryCode = d.TryGetProperty("country_code", out var cc3) ? cc3.GetString() ?? "" : "",
            }),
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        foreach (var (url, parse) in sources)
        {
            try
            {
                var json = await client.GetStringAsync(url);
                var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.JsonElement);
                var info = parse(dto);
                if (!string.IsNullOrEmpty(info.Ip))
                    return info;
            }
            catch
            {
                // Try next source
            }
        }
        return new IpInfo { Ip = "Unknown", CountryCode = "" };
    }

    // ── 外部提供者 ──

    public async Task<IReadOnlyList<ExternalProvider>> GetExternalProvidersAsync()
    {
        var result = new List<ExternalProvider>();

        // Fetch proxy providers
        try
        {
            var json = await _http.GetStringAsync("/providers/proxies");
            var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProvidersResponse);
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
            var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProvidersResponse);
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

    // ── GeoIP/GeoSite 数据库 ──

    public async Task UpdateGeoDatabaseAsync(string name)
    {
        var payload = JsonSerializer.Serialize(new NamePayload { Name = name }, AppJsonContext.Default.NamePayload);
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        await _http.PutAsync("/configs/geo", content);
    }

    // ── 运行时配置热更新 ──

    public async Task PatchCoreConfigAsync(AppSettings settings)
    {
        var payload = JsonSerializer.Serialize(new CoreConfigPatch
        {
            MixedPort = settings.MixedPort,
            SocksPort = settings.SocksPort,
            Port = settings.HttpPort,
            LogLevel = settings.LogLevel,
            Ipv6 = settings.Ipv6,
            AllowLan = settings.AllowLan,
            UnifiedDelay = settings.UnifiedDelay,
            TcpConcurrent = settings.TcpConcurrent,
            FindProcessMode = settings.FindProcessMode,
            KeepAliveInterval = settings.KeepAliveInterval,
        }, AppJsonContext.Default.CoreConfigPatch);
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        await _http.PatchAsync("/configs", content);
    }

    // ── 提供者健康检查 ──

    public async Task HealthCheckProviderAsync(string name, string category = "proxy")
    {
        var endpoint = category == "rule"
            ? $"/providers/rules/{Uri.EscapeDataString(name)}/health"
            : $"/providers/proxies/{Uri.EscapeDataString(name)}/health";
        await _http.GetAsync(endpoint);
    }

    // ── 规则 ──

    public async Task<IReadOnlyList<Rule>> GetRulesAsync()
    {
        var json = await _http.GetStringAsync("/rules");
        var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.RulesResponse);
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

    public Task<long> GetCoreMemoryAsync() => Task.FromResult(_currentMemory);

    public async Task ForceGcAsync()
    {
        await _http.PostAsync("/memory/force-gc", null);
    }

    // ── 缓存 ──

    public async Task FlushFakeIpCacheAsync()
    {
        var response = await _http.DeleteAsync("/cache/fakeip");
        response.EnsureSuccessStatusCode();
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
}
