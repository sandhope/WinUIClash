using System.Collections.ObjectModel;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// ClashMeta 模拟服务 — 生成逼真的假数据供 UI 开发使用
/// </summary>
public class MockClashService : IClashService
{
    private readonly Random _rng = new();
    private CoreState _coreState = CoreState.Stopped;
    private OutboundMode _mode = OutboundMode.Rule;
    private long _totalUp = 1_523_456_789L;
    private long _totalDown = 8_765_432_100L;
    private Timer? _trafficTimer;
    private Timer? _logTimer;
    private readonly List<LogEntry> _logBuffer = new();

    public event Action<LogEntry>? LogReceived;
    public event Action<Traffic>? TrafficUpdated;
    public event Action<CoreState>? CoreStateChanged;

    public CoreState CoreState => _coreState;

    // ── 代理组模拟数据 ──
    private static readonly string[] _proxyNames =
    [
        "香港-01 IEPL", "香港-02 BGP", "香港-03 IPLC",
        "日本-01 东京", "日本-02 大阪", "日本-03 HKT",
        "美国-01 圣何塞", "美国-02 洛杉矶", "美国-03 西雅图",
        "新加坡-01", "新加坡-02 CMI",
        "台湾-01 HINET", "台湾-02 TFN",
        "韩国-01 SK", "韩国-02 KT",
        "DIRECT", "REJECT"
    ];

    private static readonly string[] _groupNames =
    [
        "自动选择", "手动选择", "香港节点", "日本节点",
        "美国节点", "新加坡", "台湾节点", "韩国节点", "兜底策略"
    ];

    private readonly List<ProxyGroup> _groups;

    public MockClashService()
    {
        _groups = BuildMockGroups();
    }

    private List<ProxyGroup> BuildMockGroups()
    {
        var groups = new List<ProxyGroup>();
        foreach (var name in _groupNames)
        {
            var type = name switch
            {
                "自动选择" => ProxyGroupType.URLTest,
                "兜底策略" => ProxyGroupType.Fallback,
                _ => ProxyGroupType.Selector
            };

            var proxies = _proxyNames.Select(n => new Proxy
            {
                Name = n,
                Type = n == "DIRECT" ? "Direct" : n == "REJECT" ? "Reject" : "Vmess",
                Delay = n is "DIRECT" or "REJECT" ? 0 : _rng.Next(30, 800)
            }).ToList();

            groups.Add(new ProxyGroup
            {
                Name = name,
                Type = type,
                Now = proxies.FirstOrDefault(p => p.Name != "REJECT")?.Name ?? "DIRECT",
                Proxies = new ObservableCollection<Proxy>(proxies)
            });
        }
        return groups;
    }

    // ── 生命周期 ──

    public Task StartAsync()
    {
        _coreState = CoreState.Starting;
        CoreStateChanged?.Invoke(_coreState);

        _trafficTimer = new Timer(_ =>
        {
            var t = GetCurrentTraffic();
            TrafficUpdated?.Invoke(t);
        }, null, 0, 1000);

        _coreState = CoreState.Running;
        CoreStateChanged?.Invoke(_coreState);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _coreState = CoreState.Stopping;
        CoreStateChanged?.Invoke(_coreState);

        _trafficTimer?.Dispose();
        _trafficTimer = null;
        _logTimer?.Dispose();
        _logTimer = null;

        _coreState = CoreState.Stopped;
        CoreStateChanged?.Invoke(_coreState);
        return Task.CompletedTask;
    }

    public Task<string> GetVersionAsync() => Task.FromResult("mihomo 1.19.0 (mock)");

    // ── 流量 ──

    public Traffic GetCurrentTraffic()
    {
        return new Traffic
        {
            Up = _rng.NextInt64(10_000, 2_500_000),
            Down = _rng.NextInt64(50_000, 12_000_000),
            Timestamp = DateTime.Now
        };
    }

    public Traffic GetTotalTraffic()
    {
        _totalUp += _rng.NextInt64(100_000, 5_000_000);
        _totalDown += _rng.NextInt64(500_000, 20_000_000);
        return new Traffic { Up = _totalUp, Down = _totalDown };
    }

    public Task ResetTrafficAsync()
    {
        _totalUp = 0;
        _totalDown = 0;
        return Task.CompletedTask;
    }

    // ── 出站模式 ──

    public OutboundMode GetOutboundMode() => _mode;

    public Task SetOutboundModeAsync(OutboundMode mode)
    {
        _mode = mode;
        return Task.CompletedTask;
    }

    // ── 代理 ──

    public Task<IReadOnlyList<ProxyGroup>> GetProxyGroupsAsync()
    {
        return Task.FromResult<IReadOnlyList<ProxyGroup>>(_groups);
    }

    public Task ChangeProxyAsync(string groupName, string proxyName)
    {
        var group = _groups.FirstOrDefault(g => g.Name == groupName);
        if (group != null) group.Now = proxyName;
        return Task.CompletedTask;
    }

    public Task<int> TestDelayAsync(string proxyName, string? testUrl = null)
    {
        int delay = proxyName switch
        {
            "DIRECT" => _rng.Next(5, 30),
            "REJECT" => 0,
            _ => _rng.Next(20, 600)
        };
        return Task.FromResult(delay);
    }

    // ── 配置 ──

    private readonly List<Profile> _profiles =
    [
        new Profile
        {
            Label = "机场 A - 标准套餐",
            Url = "https://example.com/sub/a",
            AutoUpdate = true,
            LastUpdate = DateTime.Now.AddHours(-3),
            IsActive = true,
            SubscriptionInfo = new SubscriptionInfo
            {
                Upload = 15_000_000_000,
                Download = 85_000_000_000,
                Total = 200_000_000_000,
                Expire = DateTime.Now.AddDays(45)
            }
        },
        new Profile
        {
            Label = "机场 B - 高级线路",
            Url = "https://example.com/sub/b",
            AutoUpdate = true,
            LastUpdate = DateTime.Now.AddHours(-12),
            SubscriptionInfo = new SubscriptionInfo
            {
                Upload = 3_000_000_000,
                Download = 22_000_000_000,
                Total = 100_000_000_000,
                Expire = DateTime.Now.AddDays(120)
            }
        },
        new Profile
        {
            Label = "本地配置",
            LastUpdate = DateTime.Now.AddDays(-5),
        }
    ];

    public Task<IReadOnlyList<Profile>> GetProfilesAsync()
        => Task.FromResult<IReadOnlyList<Profile>>(_profiles);

    public Task AddProfileAsync(Profile profile)
    {
        _profiles.Add(profile);
        return Task.CompletedTask;
    }

    public Task UpdateProfileAsync(Profile profile)
    {
        var idx = _profiles.FindIndex(p => p.Id == profile.Id);
        if (idx >= 0) _profiles[idx] = profile;
        return Task.CompletedTask;
    }

    public Task DeleteProfileAsync(string profileId)
    {
        _profiles.RemoveAll(p => p.Id == profileId);
        return Task.CompletedTask;
    }

    public Task SwitchProfileAsync(string profileId)
    {
        foreach (var p in _profiles) p.IsActive = p.Id == profileId;
        return Task.CompletedTask;
    }

    public Task SyncProfileAsync(string profileId)
    {
        var p = _profiles.FirstOrDefault(x => x.Id == profileId);
        if (p != null) p.LastUpdate = DateTime.Now;
        return Task.CompletedTask;
    }

    // ── 连接 ──

    private static readonly string[] _hosts =
    [
        "www.google.com", "api.github.com", "cdn.jsdelivr.net",
        "update.microsoft.com", "fonts.googleapis.com",
        "registry.npmjs.org", "pypi.org", "releases.ubuntu.com",
        "store.steampowered.com", "api.openai.com"
    ];

    private static readonly string[] _processes =
    [
        "chrome.exe", "firefox.exe", "code.exe", "curl.exe",
        "dotnet.exe", "python.exe", "npm.exe", "System"
    ];

    public Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync()
    {
        int count = _rng.Next(5, 15);
        var list = new List<ConnectionInfo>(count);
        for (int i = 0; i < count; i++)
        {
            var host = _hosts[_rng.Next(_hosts.Length)];
            var proc = _processes[_rng.Next(_processes.Length)];
            var chain = _groupNames[_rng.Next(Math.Min(3, _groupNames.Length))];
            list.Add(new ConnectionInfo
            {
                Id = Guid.NewGuid().ToString()[..8],
                Upload = _rng.NextInt64(1_000, 50_000_000),
                Download = _rng.NextInt64(5_000, 200_000_000),
                UploadSpeed = _rng.NextInt64(0, 500_000),
                DownloadSpeed = _rng.NextInt64(0, 5_000_000),
                Start = DateTime.Now.AddSeconds(-_rng.Next(1, 3600)),
                Metadata = new ConnectionMetadata
                {
                    Network = _rng.NextDouble() > 0.3 ? "tcp" : "udp",
                    Host = host,
                    SourceIP = $"127.0.0.1",
                    SourcePort = _rng.Next(10000, 65535).ToString(),
                    DestinationIP = $"{_rng.Next(1, 223)}.{_rng.Next(0, 255)}.{_rng.Next(0, 255)}.{_rng.Next(1, 254)}",
                    DestinationPort = _rng.NextDouble() > 0.5 ? "443" : "80",
                    Process = proc,
                    DnsMode = "fake-ip"
                },
                Chains = new ObservableCollection<string> { chain, "DIRECT" },
                Rule = "DomainSuffix",
                RulePayload = host
            });
        }
        return Task.FromResult<IReadOnlyList<ConnectionInfo>>(list);
    }

    public Task CloseConnectionAsync(string connectionId) => Task.CompletedTask;
    public Task CloseAllConnectionsAsync() => Task.CompletedTask;

    // ── 日志 ──

    private static readonly string[] _logMessages =
    [
        "[TCP] 127.0.0.1:54321 --> www.google.com:443 match DomainKeyword(google) using 自动选择[香港-01 IEPL]",
        "[UDP] 127.0.0.1:12345 --> dns.google:53 match DomainSuffix(dns.google) using DIRECT",
        "[TCP] 127.0.0.1:33210 --> api.github.com:443 match DomainSuffix(github.com) using 手动选择[日本-01 东京]",
        "DNS: resolve www.google.com answer: 142.250.80.46",
        "DNS: resolve api.github.com answer: 140.82.114.5",
        "[TCP] 127.0.0.1:44556 --> registry.npmjs.org:443 match DomainSuffix(npmjs.org) using 兜底策略[美国-01 圣何塞]",
        "[Rule] update count: 2356",
        "Socks listener stopped: 127.0.0.1:7891",
        "HTTP listener stopped: 127.0.0.1:7890",
        "Mixed(http+socks) proxy listening at: 127.0.0.1:7890",
        "[TCP] 127.0.0.1:55123 --> update.microsoft.com:443 match DomainKeyword(microsoft) using DIRECT",
        "Start initial DNS resolve",
        "Geodata cache loaded: GeoIP.dat (2.4 MB)",
        "TUN adapter opened: clash0",
        "Sniffer: sniffed www.google.com:443 as TLS",
        "[UDP] 127.0.0.1:53 --> 8.8.8.8:53 using nameserver policy"
    ];

    public Task StartLogAsync()
    {
        _logTimer = new Timer(_ =>
        {
            var level = _rng.Next(10) switch
            {
                < 5 => Models.LogLevel.Info,
                < 8 => Models.LogLevel.Debug,
                < 9 => Models.LogLevel.Warning,
                _ => Models.LogLevel.Error
            };

            var entry = new LogEntry
            {
                Level = level,
                Payload = _logMessages[_rng.Next(_logMessages.Length)],
                Timestamp = DateTime.Now
            };
            LogReceived?.Invoke(entry);
        }, null, 0, 800);

        return Task.CompletedTask;
    }

    public Task StopLogAsync()
    {
        _logTimer?.Dispose();
        _logTimer = null;
        return Task.CompletedTask;
    }

    // ── 网络检测 ──

    public Task<IpInfo> GetIpInfoAsync()
    {
        var infos = new[]
        {
            new IpInfo { Ip = "103.152.220.42", CountryCode = "HK" },
            new IpInfo { Ip = "163.171.140.8", CountryCode = "JP" },
            new IpInfo { Ip = "198.51.100.77", CountryCode = "US" },
            new IpInfo { Ip = "203.160.123.9", CountryCode = "SG" },
        };
        return Task.FromResult(infos[_rng.Next(infos.Length)]);
    }

    // ── 外部提供者 ──

    public Task<IReadOnlyList<ExternalProvider>> GetExternalProvidersAsync()
    {
        var providers = new List<ExternalProvider>
        {
            new()
            {
                Name = "GeoIP",
                Type = "GeoIP",
                Count = 398_000,
                VehicleType = "HTTP",
                UpdateAt = DateTime.Now.AddDays(-2),
                Path = "GeoIP.dat",
                Category = "rule",
            },
            new()
            {
                Name = "GeoSite",
                Type = "GeoSite",
                Count = 1_250_000,
                VehicleType = "HTTP",
                UpdateAt = DateTime.Now.AddDays(-1),
                Path = "GeoSite.dat",
                Category = "rule",
            },
            new()
            {
                Name = "ASN",
                Type = "ASN",
                Count = 45_000,
                VehicleType = "HTTP",
                UpdateAt = DateTime.Now.AddDays(-5),
                Path = "ASN.mmdb",
                Category = "rule",
            },
            new()
            {
                Name = "SubPool",
                Type = "HTTP",
                Count = 24,
                VehicleType = "HTTP",
                UpdateAt = DateTime.Now.AddHours(-6),
                Path = "proxies/sub.yaml",
                Category = "proxy",
            },
        };
        return Task.FromResult<IReadOnlyList<ExternalProvider>>(providers);
    }

    public Task UpdateExternalProviderAsync(string name, string category = "proxy") => Task.CompletedTask;

    // ── 规则 ──

    public Task<IReadOnlyList<Rule>> GetRulesAsync()
    {
        var rules = new List<Rule>
        {
            new() { Type = "MATCH", Payload = "", Proxy = "DIRECT" },
            new() { Type = "GEOIP", Payload = "CN", Proxy = "DIRECT" },
            new() { Type = "GEOSITE", Payload = "cn", Proxy = "DIRECT" },
            new() { Type = "GEOSITE", Payload = "geolocation-!cn", Proxy = "PROXY" },
            new() { Type = "GEOSITE", Payload = "google", Proxy = "PROXY" },
            new() { Type = "GEOSITE", Payload = "github", Proxy = "PROXY" },
            new() { Type = "GEOSITE", Payload = "openai", Proxy = "PROXY" },
            new() { Type = "GEOSITE", Payload = "telegram", Proxy = "PROXY" },
            new() { Type = "GEOSITE", Payload = "twitter", Proxy = "PROXY" },
            new() { Type = "GEOSITE", Payload = "youtube", Proxy = "PROXY" },
            new() { Type = "GEOSITE", Payload = "apple", Proxy = "DIRECT" },
            new() { Type = "GEOSITE", Payload = "microsoft", Proxy = "DIRECT" },
            new() { Type = "GEOSITE", Payload = "icloud", Proxy = "DIRECT" },
            new() { Type = "DOMAIN-SUFFIX", Payload = "local", Proxy = "DIRECT" },
            new() { Type = "DOMAIN-SUFFIX", Payload = "localhost", Proxy = "DIRECT" },
            new() { Type = "DOMAIN-KEYWORD", Payload = "google", Proxy = "PROXY" },
            new() { Type = "DOMAIN-KEYWORD", Payload = "github", Proxy = "PROXY" },
            new() { Type = "IP-CIDR", Payload = "127.0.0.0/8", Proxy = "DIRECT" },
            new() { Type = "IP-CIDR", Payload = "10.0.0.0/8", Proxy = "DIRECT" },
            new() { Type = "IP-CIDR", Payload = "172.16.0.0/12", Proxy = "DIRECT" },
            new() { Type = "IP-CIDR", Payload = "192.168.0.0/16", Proxy = "DIRECT" },
            new() { Type = "IP-CIDR6", Payload = "::1/128", Proxy = "DIRECT" },
            new() { Type = "IP-CIDR6", Payload = "fc00::/7", Proxy = "DIRECT" },
            new() { Type = "PROCESS-NAME", Payload = "clash", Proxy = "DIRECT" },
        };
        return Task.FromResult<IReadOnlyList<Rule>>(rules);
    }

    // ── 内存 ──

    public Task<long> GetCoreMemoryAsync()
        => Task.FromResult(_rng.NextInt64(30_000_000, 120_000_000));

    public Task ForceGcAsync() => Task.CompletedTask;
}
