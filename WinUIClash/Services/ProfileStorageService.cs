using System.Text.Json;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// 本地配置档案管理 — 存储/加载 YAML 配置文件与档案列表
/// 路径：%LOCALAPPDATA%\WinUIClash\profiles\
/// </summary>
public class ProfileStorageService
{
    private static readonly string ProfilesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinUIClash", "profiles");

    private static readonly string ProfileListPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinUIClash", "profilelist.json");

    private static readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        UseProxy = false
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static ProfileStorageService()
    {
        // FlClash sends a UA containing "clash-verge" for subscription downloads
        // (lib/common/request.dart via globalState.ua = packageInfo.ua). Most
        // Clash/Mihomo providers only accept requests with a recognised UA, so
        // we mirror that exact behaviour here.
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "FlClash/1.0.0 clash-verge Platform/Windows");
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>获取配置文件在磁盘上的完整路径</summary>
    public string GetConfigPath(string profileId)
    {
        Directory.CreateDirectory(ProfilesDir);
        return Path.Combine(ProfilesDir, $"{profileId}.yaml");
    }

    /// <summary>从订阅 URL 下载配置并保存到本地，返回 (路径, 订阅信息)</summary>
    public async Task<(string Path, SubscriptionInfo? SubInfo)> DownloadAndSaveAsync(string profileId, string url)
    {
        Directory.CreateDirectory(ProfilesDir);
        var path = GetConfigPath(profileId);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var yaml = await response.Content.ReadAsStringAsync();
        await File.WriteAllTextAsync(path, yaml);

        // 解析 subscription-userinfo 响应头
        var subInfo = ParseSubscriptionInfo(response.Headers);

        return (path, subInfo);
    }

    /// <summary>仅下载订阅内容（不落盘），用于先校验再决定是否保存。</summary>
    public async Task<(string Content, SubscriptionInfo? SubInfo)> DownloadAsync(string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var yaml = await response.Content.ReadAsStringAsync();
        var subInfo = ParseSubscriptionInfo(response.Headers);

        return (yaml, subInfo);
    }

    /// <summary>从 HTTP 响应头解析 subscription-userinfo</summary>
    private static SubscriptionInfo? ParseSubscriptionInfo(System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("subscription-userinfo", out var values))
            return null;

        var raw = string.Join("", values);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var info = new SubscriptionInfo();
        var parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;

            var key = kv[0].Trim().ToLowerInvariant();
            if (!long.TryParse(kv[1].Trim(), out var val)) continue;

            switch (key)
            {
                case "upload": info.Upload = val; break;
                case "download": info.Download = val; break;
                case "total": info.Total = val; break;
                case "expire":
                    if (val > 0)
                        info.Expire = DateTimeOffset.FromUnixTimeSeconds(val).LocalDateTime;
                    break;
            }
        }

        return info.Total > 0 ? info : null;
    }

    /// <summary>将 YAML 内容直接保存到本地</summary>
    public async Task SaveContentAsync(string profileId, string content)
    {
        Directory.CreateDirectory(ProfilesDir);
        var path = GetConfigPath(profileId);
        await File.WriteAllTextAsync(path, content);
    }

    /// <summary>删除配置文件</summary>
    public void DeleteConfig(string profileId)
    {
        var path = GetConfigPath(profileId);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>配置文件是否存在</summary>
    public bool ConfigExists(string profileId)
    {
        return File.Exists(GetConfigPath(profileId));
    }

    /// <summary>保存档案列表到 JSON（仅持久化元数据，不含运行时计算属性）</summary>
    public async Task SaveProfileListAsync(IReadOnlyList<Profile> profiles)
    {
        var dtos = profiles.Select(p => new ProfileListEntry
        {
            Id = p.Id,
            Label = p.Label,
            Url = p.Url,
            Path = p.Path,
            LastUpdate = p.LastUpdate,
            AutoUpdate = p.AutoUpdate,
            AutoUpdateIntervalSeconds = (int)p.AutoUpdateInterval.TotalSeconds,
            IsActive = p.IsActive,
            Order = p.Order,
            SubUpload = p.SubscriptionInfo?.Upload ?? 0,
            SubDownload = p.SubscriptionInfo?.Download ?? 0,
            SubTotal = p.SubscriptionInfo?.Total ?? 0,
            SubExpire = p.SubscriptionInfo?.Expire,
        }).ToList();

        var json = JsonSerializer.Serialize(dtos, JsonOpts);
        Directory.CreateDirectory(Path.GetDirectoryName(ProfileListPath)!);
        await File.WriteAllTextAsync(ProfileListPath, json);
    }

    /// <summary>从 JSON 加载档案列表</summary>
    public async Task<List<Profile>> LoadProfileListAsync()
    {
        if (!File.Exists(ProfileListPath)) return new List<Profile>();

        try
        {
            var json = await File.ReadAllTextAsync(ProfileListPath);
            var entries = JsonSerializer.Deserialize<List<ProfileListEntry>>(json, JsonOpts);
            if (entries == null) return new List<Profile>();

            return entries.Select(e =>
            {
                // The persisted absolute Path can become invalid when the app
                // runs packaged (MSIX) vs unpackaged: LocalApplicationData is
                // redirected, so an old absolute path no longer exists. Always
                // prefer GetConfigPath(profile.Id) — which derives the path from
                // the current LocalApplicationData — and only keep the persisted
                // Path when that file actually exists (external/local imports).
                var resolvedPath = !string.IsNullOrEmpty(e.Path) && File.Exists(e.Path)
                    ? e.Path
                    : GetConfigPath(e.Id);

                var profile = new Profile
                {
                    Id = e.Id,
                    Label = e.Label,
                    Url = e.Url,
                    Path = resolvedPath,
                    LastUpdate = e.LastUpdate,
                    AutoUpdate = e.AutoUpdate,
                    AutoUpdateInterval = TimeSpan.FromSeconds(e.AutoUpdateIntervalSeconds),
                    IsActive = e.IsActive,
                    Order = e.Order,
                };

                if (e.SubTotal > 0)
                {
                    profile.SubscriptionInfo = new SubscriptionInfo
                    {
                        Upload = e.SubUpload,
                        Download = e.SubDownload,
                        Total = e.SubTotal,
                        Expire = e.SubExpire,
                    };
                }

                return profile;
            }).ToList();
        }
        catch
        {
            return new List<Profile>();
        }
    }
}

/// <summary>档案列表序列化条目</summary>
internal class ProfileListEntry
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Url { get; set; }
    public string Path { get; set; } = "";
    public DateTime LastUpdate { get; set; } = DateTime.Now;
    public bool AutoUpdate { get; set; }
    public int AutoUpdateIntervalSeconds { get; set; } = 86400;
    public bool IsActive { get; set; }
    public int Order { get; set; }
    public long SubUpload { get; set; }
    public long SubDownload { get; set; }
    public long SubTotal { get; set; }
    public DateTime? SubExpire { get; set; }
}
