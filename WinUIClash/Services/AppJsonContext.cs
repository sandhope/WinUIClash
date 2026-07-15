using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinUIClash.Services;

// ═══════════════════════════════════════════════════════════════════════════════
//  Source-generated JSON serializer context — eliminates IL2026 trim warnings.
//  Register every type that passes through JsonSerializer.Serialize/Deserialize.
// ═══════════════════════════════════════════════════════════════════════════════

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    PropertyNameCaseInsensitive = true)]
// ── Settings & Profiles ──
[JsonSerializable(typeof(SettingsDto))]
[JsonSerializable(typeof(List<ProfileListEntry>))]
// ── UpdateService ──
[JsonSerializable(typeof(GitHubRelease))]
// ── HttpClashService response DTOs ──
[JsonSerializable(typeof(VersionDto))]
[JsonSerializable(typeof(TrafficDto))]
[JsonSerializable(typeof(MemoryDto))]
[JsonSerializable(typeof(ProxiesResponse))]
[JsonSerializable(typeof(DelayResponse))]
[JsonSerializable(typeof(ConnectionsResponse))]
[JsonSerializable(typeof(LogDto))]
[JsonSerializable(typeof(RulesResponse))]
[JsonSerializable(typeof(ProvidersResponse))]
[JsonSerializable(typeof(JsonElement))]
// ── HttpClashService request payloads ──
[JsonSerializable(typeof(ModePayload))]
[JsonSerializable(typeof(NamePayload))]
[JsonSerializable(typeof(PathPayload))]
[JsonSerializable(typeof(TunConfigPayload))]
[JsonSerializable(typeof(TunStackPayload))]
[JsonSerializable(typeof(CoreConfigPatch))]
// ── HelperServiceManager ──
[JsonSerializable(typeof(HelperStartPayload))]
internal partial class AppJsonContext : JsonSerializerContext;

// ═══════════════════════════════════════════════════════════════════════════════
//  Request payload DTOs (replace anonymous types for trim-safe serialization)
// ═══════════════════════════════════════════════════════════════════════════════

internal sealed class ModePayload
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }
}

internal sealed class NamePayload
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

internal sealed class PathPayload
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }
}

internal sealed class HelperStartPayload
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("arg")]
    public required string Arg { get; init; }
}

internal sealed class TunConfigPayload
{
    [JsonPropertyName("tun")]
    public required TunSettings Tun { get; init; }
}

internal sealed class TunSettings
{
    [JsonPropertyName("enable")]
    public bool Enable { get; init; }

    [JsonPropertyName("stack")]
    public string Stack { get; init; } = "";

    [JsonPropertyName("device")]
    public string Device { get; init; } = "";

    [JsonPropertyName("auto_route")]
    public bool AutoRoute { get; init; }

    [JsonPropertyName("auto_detect_interface")]
    public bool AutoDetectInterface { get; init; }

    [JsonPropertyName("dns_hijack")]
    public string[] DnsHijack { get; init; } = [];

    [JsonPropertyName("strict_route")]
    public bool StrictRoute { get; init; }
}

internal sealed class TunStackPayload
{
    [JsonPropertyName("tun")]
    public required TunStackInner Tun { get; init; }
}

internal sealed class TunStackInner
{
    [JsonPropertyName("stack")]
    public required string Stack { get; init; }
}

internal sealed class CoreConfigPatch
{
    [JsonPropertyName("mixed_port")]
    public int MixedPort { get; init; }

    [JsonPropertyName("socks_port")]
    public int SocksPort { get; init; }

    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("log_level")]
    public string LogLevel { get; init; } = "";

    [JsonPropertyName("ipv6")]
    public bool Ipv6 { get; init; }

    [JsonPropertyName("allow_lan")]
    public bool AllowLan { get; init; }

    [JsonPropertyName("unified_delay")]
    public bool UnifiedDelay { get; init; }

    [JsonPropertyName("tcp_concurrent")]
    public bool TcpConcurrent { get; init; }

    [JsonPropertyName("find_process_mode")]
    public string FindProcessMode { get; init; } = "";

    [JsonPropertyName("keep_alive_interval")]
    public int KeepAliveInterval { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
//  Response DTOs (moved from HttpClashService private nested → internal)
// ═══════════════════════════════════════════════════════════════════════════════

internal sealed class VersionDto
{
    public string? Version { get; set; }
    public bool Meta { get; set; }
}

internal sealed class TrafficDto
{
    public long Up { get; set; }
    public long Down { get; set; }
}

internal sealed class MemoryDto
{
    public long Inuse { get; set; }
    public long Alloc { get; set; }
    public long Sys { get; set; }
    public long Idle { get; set; }
    public long Released { get; set; }
    public long HeapObjects { get; set; }
}

internal sealed class ProxiesResponse
{
    public Dictionary<string, ProxyDto>? Proxies { get; set; }
}

internal sealed class ProxyDto
{
    public string? Type { get; set; }
    public string? Now { get; set; }
    public List<string>? All { get; set; }
    public List<HistoryDto>? History { get; set; }
}

internal sealed class HistoryDto
{
    public int Delay { get; set; }
}

internal sealed class DelayResponse
{
    public int Delay { get; set; }
}

internal sealed class ConnectionsResponse
{
    public List<ConnectionDto>? Connections { get; set; }
}

internal sealed class ConnectionDto
{
    public string Id { get; set; } = "";
    public DateTime? Start { get; set; }
    public long Download { get; set; }
    public long Upload { get; set; }
    public long UploadSpeed { get; set; }
    public long DownloadSpeed { get; set; }
    public ConnectionMetaDto? Metadata { get; set; }
    public List<string>? Chains { get; set; }
    public string? Rule { get; set; }
    public string? RulePayload { get; set; }
}

internal sealed class ConnectionMetaDto
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

internal sealed class LogDto
{
    public string? Type { get; set; }
    public string? Payload { get; set; }
}

internal sealed class RulesResponse
{
    public List<RuleDto>? Rules { get; set; }
}

internal sealed class RuleDto
{
    public string? Type { get; set; }
    public string? Payload { get; set; }
    public string? Proxy { get; set; }
    public int Size { get; set; }
}

internal sealed class ProvidersResponse
{
    public List<ProviderDto>? Providers { get; set; }
}

internal sealed class ProviderDto
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? VehicleType { get; set; }
    public int Count { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ── UpdateService DTOs (moved from private nested → internal) ──

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = "";

    [JsonPropertyName("body")]
    public string? Body { get; init; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; init; } = "";

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; init; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; init; }
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; init; } = "";
}
