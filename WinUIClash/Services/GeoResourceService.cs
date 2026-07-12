using System.IO;
using System.Net.Http;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// 管理 mihomo 的 Geo 数据库（MMDB / ASN / GEOIP / GEOSITE）。
/// 文件存放在核心数据目录（mihomo -d 指定的目录，即 ConfigBuildService.ConfigDirectory），
/// 与 FlClash 将 geo 文件放在 homeDirPath 一致。
/// </summary>
public class GeoResourceService
{
    private readonly string _geoDir;
    private readonly HttpClient _http;

    public GeoResourceService(ConfigBuildService configBuild)
    {
        _geoDir = configBuild.ConfigDirectory;
        // 关键：下载 Geo 资源必须绕过系统代理（UseProxy=false），否则请求会被路由到 127.0.0.1 代理端口失败。
        _http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromMinutes(5),
        };
    }

    public static string FileNameFor(GeoResourceType type) => type switch
    {
        GeoResourceType.MMDB => "GEOIP.metadb",
        GeoResourceType.ASN => "ASN.mmdb",
        GeoResourceType.GEOIP => "GEOIP.dat",
        GeoResourceType.GEOSITE => "GEOSITE.dat",
        _ => "unknown",
    };

    public string GetFilePath(GeoResourceType type) =>
        Path.Combine(_geoDir, FileNameFor(type));

    /// <summary>读取文件大小与最后修改时间；文件不存在返回 null。</summary>
    public (long size, DateTime lastModified)? GetFileInfo(GeoResourceType type)
    {
        var path = GetFilePath(type);
        if (!File.Exists(path)) return null;
        var fi = new FileInfo(path);
        return (fi.Length, fi.LastWriteTime);
    }

    public async Task UpdateAsync(
        GeoResourceType type,
        string url,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Geo 资源地址为空", nameof(url));

        var path = GetFilePath(type);
        Directory.CreateDirectory(_geoDir);

        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(path);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total.HasValue && total.Value > 0)
                progress?.Report((double)read / total.Value);
        }
    }
}
