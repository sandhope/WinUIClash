using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WinUIClash.HelperService;

/// <summary>
/// Helper Service 的核心逻辑：以 SYSTEM 权限运行 HTTP API，
/// 主 app 通过此 API 控制 mihomo 核心进程的启停。
/// 对齐 FlClash 的 FlClashHelperService（Rust warp HTTP API，端口 47890）。
/// </summary>
public class HelperHostedService : BackgroundService
{
    private const int ApiPort = 47891; // 避开 FlClash 的 47890
    private const string ServiceToken = "WinUIClashHelper"; // 简单鉴权 token

    private Process? _mihomoProcess;
    private readonly ILogger<HelperHostedService> _logger;

    public HelperHostedService(ILogger<HelperHostedService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{ApiPort}/");

        try
        {
            listener.Start();
            _logger.LogInformation("HelperService HTTP API 已启动，监听 127.0.0.1:{Port}", ApiPort);
        }
        catch (HttpListenerException ex)
        {
            _logger.LogError(ex, "无法启动 HTTP 监听器（端口 {Port} 可能已被占用）", ApiPort);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var context = await listener.GetContextAsync();
                await HandleRequestAsync(context);
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "处理请求时出错");
            }
        }

        // 停止时清理 mihomo 进程
        StopMihomo();
        listener.Stop();
        listener.Close();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        StopMihomo();
        await base.StopAsync(cancellationToken);
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var method = context.Request.HttpMethod;

        try
        {
            // 鉴权：所有请求需带 token 参数
            var token = context.Request.QueryString["token"];
            if (token != ServiceToken && path != "/ping")
            {
                context.Response.StatusCode = 403;
                await WriteResponseAsync(context, "Forbidden: invalid token");
                return;
            }

            switch (path)
            {
                case "/ping":
                    await HandlePingAsync(context);
                    break;

                case "/start":
                    if (method == "POST")
                        await HandleStartAsync(context);
                    else
                        context.Response.StatusCode = 405;
                    break;

                case "/stop":
                    if (method == "POST")
                        await HandleStopAsync(context);
                    else
                        context.Response.StatusCode = 405;
                    break;

                case "/status":
                    await HandleStatusAsync(context);
                    break;

                default:
                    context.Response.StatusCode = 404;
                    await WriteResponseAsync(context, "Not found");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理 {Method} {Path} 时出错", method, path);
            context.Response.StatusCode = 500;
            await WriteResponseAsync(context, $"Internal error: {ex.Message}");
        }
        finally
        {
            context.Response.Close();
        }
    }

    private async Task HandlePingAsync(HttpListenerContext context)
    {
        // 简单心跳：确认 Helper Service 正在运行（对齐 FlClash pingHelper）
        await WriteResponseAsync(context, ServiceToken);
    }

    private async Task HandleStartAsync(HttpListenerContext context)
    {
        var body = await ReadBodyAsync(context);
        if (body == null)
        {
            context.Response.StatusCode = 400;
            await WriteResponseAsync(context, "Missing request body");
            return;
        }

        var params_ = JsonSerializer.Deserialize<StartParams>(body);
        if (params_ == null || string.IsNullOrWhiteSpace(params_.Path))
        {
            context.Response.StatusCode = 400;
            await WriteResponseAsync(context, "Invalid start params: path is required");
            return;
        }

        if (!File.Exists(params_.Path))
        {
            context.Response.StatusCode = 400;
            await WriteResponseAsync(context, $"Core binary not found: {params_.Path}");
            return;
        }

        // 先停掉已有进程
        StopMihomo();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = params_.Path,
                Arguments = params_.Arg ?? "",
                WorkingDirectory = Path.GetDirectoryName(params_.Path) ?? "",
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
                _mihomoProcess.Id, params_.Path, params_.Arg);

            await WriteResponseAsync(context, ""); // 成功返回空串（对齐 FlClash）
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动 mihomo 进程失败");
            await WriteResponseAsync(context, $"Start failed: {ex.Message}");
        }
    }

    private async Task HandleStopAsync(HttpListenerContext context)
    {
        StopMihomo();
        await WriteResponseAsync(context, ""); // 成功返回空串（对齐 FlClash）
    }

    private async Task HandleStatusAsync(HttpListenerContext context)
    {
        var running = _mihomoProcess != null && !_mihomoProcess.HasExited;
        var pid = running ? _mihomoProcess?.Id : null;
        var status = JsonSerializer.Serialize(new { running, pid });
        await WriteResponseAsync(context, status);
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

    private static async Task WriteResponseAsync(HttpListenerContext context, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        context.Response.ContentLength64 = bytes.Length;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.OutputStream.WriteAsync(bytes);
    }

    private static async Task<string?> ReadBodyAsync(HttpListenerContext context)
    {
        if (context.Request.ContentLength64 == 0) return null;
        using var reader = new StreamReader(context.Request.InputStream);
        return await reader.ReadToEndAsync();
    }
}

/// <summary>启动参数（对齐 FlClash Rust StartParams）</summary>
public class StartParams
{
    public string Path { get; set; } = "";
    public string Arg { get; set; } = "";
}
