using System.Text;
using Microsoft.Extensions.Logging;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// 运行时构建 mihomo 的 config.yaml。
/// 把“活动订阅/配置”与“应用托管的控制器设置”（端口、secret、external-controller）
/// 合并后写出到 %LOCALAPPDATA%\WinUIClash\config.yaml，
/// 从而保证核心监听的外部控制器端口与应用连接的端口永远一致。
/// </summary>
public class ConfigBuildService
{
    private readonly AppSettings _settings;
    private readonly ProfileStorageService _profileStorage;
    private readonly ILogger<ConfigBuildService> _logger;

    public const int DefaultApiPort = 9090;

    public int ApiPort => _settings.ApiPort > 0 ? _settings.ApiPort : DefaultApiPort;

    public ConfigBuildService(AppSettings settings, ProfileStorageService profileStorage, ILogger<ConfigBuildService> logger)
    {
        _settings = settings;
        _profileStorage = profileStorage;
        _logger = logger;
    }

    public string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinUIClash");

    public string ConfigPath => Path.Combine(ConfigDirectory, "config.yaml");

    /// <summary>
    /// 根据当前设置与活动配置生成最终的 config.yaml，返回其完整路径。
    /// </summary>
    public async Task<string> BuildConfigAsync()
    {
        Directory.CreateDirectory(ConfigDirectory);

        var profile = await GetActiveProfileAsync();
        string baseYaml;
        if (profile != null && File.Exists(profile.Path))
        {
            baseYaml = await File.ReadAllTextAsync(profile.Path);
            _logger.LogInformation("使用活动配置 {Label} 生成 config.yaml: {Path}", profile.Label, profile.Path);
        }
        else
        {
            baseYaml = LoadBundledDefaultConfig();
            _logger.LogInformation("没有活动配置，使用内置默认配置模板");
        }

        var merged = InjectControllerSettings(baseYaml);
        await File.WriteAllTextAsync(ConfigPath, merged);
        return ConfigPath;
    }

    private async Task<Profile?> GetActiveProfileAsync()
    {
        try
        {
            var list = await _profileStorage.LoadProfileListAsync();
            return list.FirstOrDefault(p => p.IsActive)
                ?? (list.Count > 0 ? list.OrderBy(p => p.Order).First() : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "读取配置列表失败，回退到默认配置");
            return null;
        }
    }

    // 这些顶层键由应用托管，合并时从订阅原文中剥离后重新写入，避免重复键导致解析失败
    private static readonly HashSet<string> OverrideKeys = new()
    {
        "external-controller", "secret", "mixed-port", "socks-port", "port",
        "mode", "log-level", "allow-lan", "ipv6", "tun",
    };

    private string InjectControllerSettings(string yaml)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            if (IsTopLevelOverride(line)) continue;
            kept.Add(line);
        }

        var sb = new StringBuilder();
        sb.AppendLine("# ------------------------------------------------------------------");
        sb.AppendLine("# 本文件由 WinUIClash 自动生成，控制器相关设置由应用托管。");
        sb.AppendLine("# ------------------------------------------------------------------");
        sb.AppendLine($"mixed-port: {_settings.MixedPort}");
        sb.AppendLine($"socks-port: {_settings.SocksPort}");
        sb.AppendLine($"port: {_settings.HttpPort}");
        sb.AppendLine($"external-controller: 127.0.0.1:{ApiPort}");
        if (!string.IsNullOrWhiteSpace(_settings.ApiSecret))
            sb.AppendLine($"secret: \"{_settings.ApiSecret}\"");
        // 初始为直连模式：核心常驻后台但不代理任何流量（符合 REFACTOR_GUIDE T2）。
        // 真正的“连接”由 UI 在用户点击时通过 PATCH /configs 切换为 rule/global。
        sb.AppendLine("mode: direct");
        sb.AppendLine($"log-level: {_settings.LogLevel}");
        sb.AppendLine($"allow-lan: {(_settings.AllowLan ? "true" : "false")}");
        sb.AppendLine($"ipv6: {(_settings.Ipv6 ? "true" : "false")}");

        // TUN 配置（对齐 FlClash config 写入方式，核心启动时即知 TUN 参数）
        sb.AppendLine("tun:");
        sb.AppendLine($"  enable: {(_settings.TunMode ? "true" : "false")}");
        sb.AppendLine($"  device: WinUIClash");
        sb.AppendLine($"  stack: {_settings.TunStack}");
        sb.AppendLine("  dns-hijack:");
        sb.AppendLine("    - any:53");
        sb.AppendLine("    - tcp://any:53");
        sb.AppendLine("  auto-route: true");
        sb.AppendLine("  auto-detect-interface: true");
        sb.AppendLine("  strict-route: true");
        sb.AppendLine();

        foreach (var line in kept)
            sb.AppendLine(line);

        return sb.ToString();
    }

    /// <summary>判断是否为需要被应用托管的顶层键（忽略注释与缩进的行）。</summary>
    private static bool IsTopLevelOverride(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (line.StartsWith(" ") || line.StartsWith("\t")) return false;
        if (line.StartsWith("#")) return false;
        var idx = line.IndexOf(':');
        if (idx <= 0) return false;
        var key = line[..idx].Trim();
        return OverrideKeys.Contains(key);
    }

    private static string NormalizeMode(string? mode) => mode?.ToLowerInvariant() switch
    {
        "global" => "global",
        "direct" => "direct",
        _ => "rule",
    };

    private string LoadBundledDefaultConfig()
    {
        // 优先使用打包的默认模板（含 DNS / proxy-groups / rules）
        var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Core", "config.yaml");
        if (File.Exists(bundled))
            return File.ReadAllText(bundled);

        // 极端兜底：内置最小化配置
        return "mixed-port: " + _settings.MixedPort + "\n" +
               "socks-port: " + _settings.SocksPort + "\n" +
               "port: " + _settings.HttpPort + "\n" +
               "external-controller: 127.0.0.1:" + ApiPort + "\n" +
               "mode: direct\n" +
               "log-level: " + _settings.LogLevel + "\n" +
               "allow-lan: false\n" +
               "ipv6: false\n" +
               "dns:\n" +
               "  enable: true\n" +
               "  enhanced-mode: fake-ip\n" +
               "  fake-ip-range: 198.18.0.1/16\n" +
               "  default-nameserver:\n" +
               "    - 223.5.5.5\n" +
               "    - 119.29.29.29\n" +
               "  nameserver:\n" +
               "    - https://doh.pub/dns-query\n" +
               "    - https://dns.alidns.com/dns-query\n" +
               "proxy-groups:\n" +
               "  - name: \"PROXY\"\n" +
               "    type: select\n" +
               "    proxies:\n" +
               "      - DIRECT\n" +
               "  - name: \"AUTO\"\n" +
               "    type: url-test\n" +
               "    proxies:\n" +
               "      - DIRECT\n" +
               "    url: \"https://www.gstatic.com/generate_204\"\n" +
               "    interval: 300\n" +
               "rules:\n" +
               "  - GEOIP,CN,DIRECT\n" +
               "  - MATCH,PROXY\n";
    }

    /// <summary>
    /// 从生成的 config.yaml 中读取 proxy-groups 的名称顺序。
    /// mihomo REST /proxies 返回 Go map（JSON 序列化时按键 Unicode 排序），
    /// 不保留 config.yaml 中 proxy-groups 的原始顺序。
    /// FlClash 通过 gRPC/FFI 接口的 all 列表保留了顺序，
    /// WinUIClash 用此方法从 config 文件还原顺序以 1:1 对齐。
    /// </summary>
    public List<string> GetProxyGroupOrder()
    {
        var result = new List<string>();
        if (!File.Exists(ConfigPath)) return result;

        try
        {
            var lines = File.ReadAllLines(ConfigPath);
            bool inProxyGroups = false;
            foreach (var line in lines)
            {
                // Detect top-level "proxy-groups:" key
                if (!line.StartsWith(" ") && !line.StartsWith("\t") && !line.StartsWith("#"))
                {
                    var idx = line.IndexOf(':');
                    if (idx > 0 && line[..idx].Trim() == "proxy-groups")
                    {
                        inProxyGroups = true;
                        continue;
                    }
                    // Exit if we've entered proxy-groups and hit another top-level key
                    if (inProxyGroups && idx > 0)
                        break;
                    if (inProxyGroups && line.Trim() == "")
                        continue;
                }

                if (!inProxyGroups) continue;

                // Block style: "  - name: 维云云"
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("- name:") || trimmed.StartsWith("-name:"))
                {
                    var colonIdx = trimmed.IndexOf(':');
                    if (colonIdx < 0) continue;
                    var value = trimmed[(colonIdx + 1)..].Trim();
                    // Strip quotes
                    if ((value.StartsWith('"') && value.EndsWith('"')) ||
                        (value.StartsWith('\'') && value.EndsWith('\'')))
                        value = value[1..^1];
                    if (!string.IsNullOrEmpty(value))
                        result.Add(value);
                    continue;
                }

                // Flow style: "  - { name: 维云云, type: select, ... }"
                if (trimmed.StartsWith("- {") || trimmed.StartsWith("-{"))
                {
                    var nameMatch = System.Text.RegularExpressions.Regex.Match(
                        trimmed, @"name:\s*([^,}]+)");
                    if (nameMatch.Success)
                    {
                        var value = nameMatch.Groups[1].Value.Trim();
                        if ((value.StartsWith('"') && value.EndsWith('"')) ||
                            (value.StartsWith('\'') && value.EndsWith('\'')))
                            value = value[1..^1];
                        if (!string.IsNullOrEmpty(value))
                            result.Add(value);
                    }
                }
            }
        }
        catch
        {
            // Best-effort; if parsing fails, return empty (caller falls back to API order)
        }
        return result;
    }
}
