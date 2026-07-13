using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WinUIClash.HelperService;

/// <summary>
/// Helper Service 的核心逻辑：以 SYSTEM 权限运行轻量 HTTP API，
/// 主 app 通过此 API 控制 mihomo 核心进程的启停。
/// 对齐 FlClash 的 FlClashHelperService（Rust warp HTTP API，端口 47890）。
///
/// 关键实现说明：这里**不使用** System.Net.HttpListener，因为它依赖 http.sys 的
/// URL ACL 保留（在 LocalSystem 下经常绑定失败，导致服务进程“已启动”但 API 不可达，
/// 主程序误判服务不可用、退回用户态启动核心，进而 TUN 无法创建虚拟网卡）。
/// 改用裸 TcpListener 监听 127.0.0.1 回环地址——回环 TCP 绑定不需要任何 URL ACL，
/// SYSTEM 下必然能绑定，从而让服务稳定可用。
/// </summary>
public class HelperHostedService : BackgroundService
{
    private const int ApiPort = 47891; // 避开 FlClash 的 47890
    private const string ServiceToken = "WinUIClashHelper"; // 简单鉴权 token

    private Process? _mihomoProcess;
    private readonly ILogger<HelperHostedService> _logger;
    private TcpListener? _listener;

    public HelperHostedService(ILogger<HelperHostedService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener = new TcpListener(IPAddress.Loopback, ApiPort);
        try
        {
            _listener.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "无法绑定 127.0.0.1:{Port}，HelperService 无法工作", ApiPort);
            return;
        }

        _logger.LogInformation("HelperService HTTP API 已启动，监听 127.0.0.1:{Port}", ApiPort);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AcceptTcpClient 出错");
                    break;
                }

                // 每个连接独立处理，不阻塞监听循环
                _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
            }
        }
        finally
        {
            StopMihomo();
            try { _listener.Stop(); } catch { }
            _listener = null;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        StopMihomo();
        try { _listener?.Stop(); } catch { }
        await base.StopAsync(cancellationToken);
    }

    // ── 最小 HTTP/1.1 服务器（仅服务本地回环，接口极简）──

    private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

                var requestLine = await reader.ReadLineAsync(stoppingToken);
                if (string.IsNullOrWhiteSpace(requestLine)) return;

                var parts = requestLine.Split(' ');
                if (parts.Length < 2) { await SendResponseAsync(stream, 400, "Bad Request"); return; }
                var method = parts[0];
                var rawUrl = parts[1];

                var path = rawUrl;
                var query = "";
                var qIdx = rawUrl.IndexOf('?');
                if (qIdx >= 0)
                {
                    path = rawUrl.Substring(0, qIdx);
                    query = rawUrl.Substring(qIdx + 1);
                }

                // 读取请求头，解析 Content-Length
                var contentLength = 0;
                string? headerLine;
                while (!string.IsNullOrWhiteSpace(headerLine = await reader.ReadLineAsync(stoppingToken)))
                {
                    if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(headerLine.Substring("Content-Length:".Length).Trim(), out var cl))
                    {
                        contentLength = cl;
                    }
                }

                // 读取请求体
                var body = "";
                if (contentLength > 0)
                {
                    var buf = new char[contentLength];
                    var read = 0;
                    while (read < contentLength)
                    {
                        var n = await reader.ReadAsync(buf.AsMemory(read, contentLength - read), stoppingToken);
                        if (n == 0) break;
                        read += n;
                    }
                    body = new string(buf, 0, read);
                }

                // 鉴权：除 /ping 外的所有请求需带有效 token
                var queryParams = ParseQuery(query);
                var tokenOk = path == "/ping" ||
                              (queryParams.TryGetValue("token", out var token) && token == ServiceToken);
                if (!tokenOk)
                {
                    await SendResponseAsync(stream, 403, "Forbidden: invalid token");
                    return;
                }

                switch (path)
                {
                    case "/ping":
                        await SendResponseAsync(stream, 200, ServiceToken);
                        break;
                    case "/start":
                        if (method == "POST")
                            await HandleStartAsync(stream, body);
                        else
                            await SendResponseAsync(stream, 405, "Method Not Allowed");
                        break;
                    case "/stop":
                        if (method == "POST")
                        {
                            StopMihomo();
                            await SendResponseAsync(stream, 200, "");
                        }
                        else
                            await SendResponseAsync(stream, 405, "Method Not Allowed");
                        break;
                    case "/status":
                    {
                        var running = _mihomoProcess != null && !_mihomoProcess.HasExited;
                        var status = JsonSerializer.Serialize(new
                        {
                            running,
                            pid = running ? _mihomoProcess?.Id : (int?)null,
                        });
                        await SendResponseAsync(stream, 200, status);
                        break;
                    }
                    default:
                        await SendResponseAsync(stream, 404, "Not found");
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "处理客户端请求时出错");
        }
    }

    private async Task HandleStartAsync(NetworkStream stream, string body)
    {
        StartParams? p = null;
        try { p = JsonSerializer.Deserialize<StartParams>(body, JsonOpts); } catch { }

        if (p == null || string.IsNullOrWhiteSpace(p.Path))
        {
            await SendResponseAsync(stream, 400, "Invalid start params: path is required");
            return;
        }
        if (!File.Exists(p.Path))
        {
            await SendResponseAsync(stream, 400, $"Core binary not found: {p.Path}");
            return;
        }

        // 先停掉已有进程
        StopMihomo();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = p.Path,
                Arguments = p.Arg ?? "",
                WorkingDirectory = Path.GetDirectoryName(p.Path) ?? "",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            _mihomoProcess = new Process { StartInfo = startInfo };
            _mihomoProcess.EnableRaisingEvents = true;
            _mihomoProcess.Exited += (_, _) =>
            {
                _logger.LogInformation("mihomo 进程已退出（PID: {Pid}）", _mihomoProcess?.Id);
            };

            _mihomoProcess.Start();
            _mihomoProcess.BeginErrorReadLine();
            _mihomoProcess.BeginOutputReadLine();

            _logger.LogInformation("mihomo 进程已启动（PID: {Pid}，命令: {Path} {Arg})",
                _mihomoProcess.Id, p.Path, p.Arg);

            await SendResponseAsync(stream, 200, ""); // 成功返回空串（对齐 FlClash）
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动 mihomo 进程失败");
            await SendResponseAsync(stream, 500, $"Start failed: {ex.Message}");
        }
    }

    private void StopMihomo()
    {
        if (_mihomoProcess == null || _mihomoProcess.HasExited) return;

        try
        {
            _mihomoProcess.Kill(true);
            _mihomoProcess.WaitForExit(3000);
        }
        catch { /* 进程可能已退出 */ }

        _mihomoProcess.Dispose();
        _mihomoProcess = null;
    }

    private static async Task SendResponseAsync(NetworkStream stream, int statusCode, string content)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(content);
        var header = new StringBuilder();
        header.AppendLine($"HTTP/1.1 {statusCode} {ReasonPhrase(statusCode)}");
        header.AppendLine("Content-Type: text/plain; charset=utf-8");
        header.AppendLine("Content-Length: " + bodyBytes.Length);
        header.AppendLine("Connection: close");
        header.AppendLine();

        var headerBytes = Encoding.ASCII.GetBytes(header.ToString());
        await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
        if (bodyBytes.Length > 0)
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
        await stream.FlushAsync();
    }

    private static string ReasonPhrase(int code) => code switch
    {
        200 => "OK",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        500 => "Internal Server Error",
        _ => "OK",
    };

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var dict = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(query)) return dict;
        foreach (var pair in query.Split('&'))
        {
            var idx = pair.IndexOf('=');
            if (idx > 0)
                dict[pair.Substring(0, idx)] = pair.Substring(idx + 1);
        }
        return dict;
    }
}

/// <summary>启动参数（对齐 FlClash Rust StartParams）</summary>
public class StartParams
{
    public string Path { get; set; } = "";
    public string Arg { get; set; } = "";
}
