using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace WinUIClash.Services;

/// <summary>
/// 检查 GitHub Releases 获取应用更新信息
/// </summary>
public class UpdateService
{
    private readonly ILogger<UpdateService> _logger;
    private const string GitHubRepo = "chen08209/WinUIClash"; // TODO: replace with actual repo
    private const string ReleasesUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { { "User-Agent", "WinUIClash-UpdateChecker" } },
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
    }

    /// <summary>当前应用版本</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);

    /// <summary>
    /// 检查是否有新版本可用
    /// </summary>
    /// <returns>更新信息，如果已是最新则返回 null</returns>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync(ReleasesUrl, ct);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOpts);

            if (release == null || string.IsNullOrEmpty(release.TagName))
            {
                _logger.LogWarning("Empty or invalid release response from GitHub");
                return null;
            }

            var latestVersion = ParseVersion(release.TagName);
            if (latestVersion == null)
            {
                _logger.LogWarning("Could not parse version tag: {Tag}", release.TagName);
                return null;
            }

            if (latestVersion <= CurrentVersion)
            {
                _logger.LogInformation("Already up to date: current={Current}, latest={Latest}",
                    CurrentVersion, latestVersion);
                return null;
            }

            _logger.LogInformation("Update available: {Current} → {Latest}", CurrentVersion, latestVersion);

            // Find the appropriate asset for download
            var downloadUrl = release.Assets?
                .FirstOrDefault(a => a.Name.Contains("win", StringComparison.OrdinalIgnoreCase) &&
                                     (a.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
                                      a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                ?.BrowserDownloadUrl ?? release.HtmlUrl;

            return new UpdateInfo(
                Version: latestVersion,
                TagName: release.TagName,
                ReleaseNotes: release.Body ?? "",
                DownloadUrl: downloadUrl,
                ReleasePageUrl: release.HtmlUrl,
                PublishedAt: release.PublishedAt);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error while checking for updates");
            throw new UpdateCheckException("Network error. Please check your connection.", ex);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking for updates");
            throw new UpdateCheckException("Failed to check for updates.", ex);
        }
    }

    /// <summary>
    /// 打开浏览器跳转到发布页面
    /// </summary>
    public static void OpenReleasePage(string url)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    private static Version? ParseVersion(string tag)
    {
        // Handle tags like "v1.2.3", "1.2.3", "v1.2.3-beta"
        var clean = tag.TrimStart('v', 'V');
        var dashIndex = clean.IndexOf('-');
        if (dashIndex >= 0) clean = clean[..dashIndex];

        return Version.TryParse(clean, out var version) ? version : null;
    }

    // ── DTOs ──

    public record UpdateInfo(
        Version Version,
        string TagName,
        string ReleaseNotes,
        string DownloadUrl,
        string ReleasePageUrl,
        DateTimeOffset? PublishedAt);

    private sealed class GitHubRelease
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

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = "";
    }
}

/// <summary>Exception thrown when update check fails</summary>
public class UpdateCheckException : Exception
{
    public UpdateCheckException(string message, Exception inner) : base(message, inner) { }
}
