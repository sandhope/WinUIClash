using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace WinUIClash.Services;

/// <summary>
/// 当本地缺少 mihomo 核心二进制时，从 GitHub Releases 运行时下载，
/// 使应用开箱即用（无需手动执行 download-core.ps1）。
/// 下载目标位于可写的 %LOCALAPPDATA%\WinUIClash\Core\，与 CoreProcessService 的探测路径一致。
/// </summary>
public class CoreDownloadService
{
    private readonly ILogger<CoreDownloadService> _logger;

    private const string Repo = "MetaCubeX/mihomo";

    public CoreDownloadService(ILogger<CoreDownloadService> logger)
    {
        _logger = logger;
    }

    public string LocalCoreDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinUIClash", "Core");

    /// <summary>
    /// 下载并解压 mihomo 核心。成功返回二进制完整路径，失败返回 null。
    /// </summary>
    public async Task<string?> DownloadAsync(IProgress<double>? progress = null)
    {
        try
        {
            var isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            var keyword = isArm64 ? "windows-arm64" : "windows-amd64";
            var outputName = isArm64 ? "mihomo-arm64.exe" : "mihomo.exe";

            Directory.CreateDirectory(LocalCoreDir);
            var targetPath = Path.Combine(LocalCoreDir, outputName);

            progress?.Report(0.05);
            var tag = await GetLatestTagAsync();
            if (string.IsNullOrEmpty(tag))
            {
                _logger.LogWarning("无法获取 mihomo 最新版本号");
                return null;
            }

            var zipUrl = $"https://github.com/{Repo}/releases/download/{tag}/mihomo-{keyword}-{tag}.zip";
            _logger.LogInformation("下载 mihomo {Tag} 自 {Url}", tag, zipUrl);

            var tempZip = Path.Combine(Path.GetTempPath(), $"mihomo-{tag}.zip");
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WinUIClash");

            using (var resp = await http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? -1L;
                using var stream = await resp.Content.ReadAsStreamAsync();
                using var fileStream = File.Create(tempZip);
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    downloaded += read;
                    if (total > 0)
                        progress?.Report(0.05 + 0.85 * (double)downloaded / total);
                }
            }

            progress?.Report(0.92);
            ExtractExe(tempZip, outputName, targetPath);
            File.Delete(tempZip);
            progress?.Report(1);

            if (File.Exists(targetPath))
            {
                _logger.LogInformation("mihomo 下载完成: {Path}", targetPath);
                return targetPath;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载 mihomo 核心失败");
            return null;
        }
    }

    private async Task<string?> GetLatestTagAsync()
    {
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WinUIClash");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tag_name", out var tag))
                return tag.GetString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询 mihomo 最新版本失败");
        }
        return null;
    }

    private void ExtractExe(string zipPath, string entryName, string outputPath)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, entryName, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            throw new InvalidOperationException($"压缩包中未找到 {entryName}");
        if (File.Exists(outputPath)) File.Delete(outputPath);
        entry.ExtractToFile(outputPath, overwrite: true);
    }
}
