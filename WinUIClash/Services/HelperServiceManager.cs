using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// 管理 WinUIClashHelperService Windows 服务的生命周期。
/// 对齐 FlClash 的 registerService / checkService / unregisterService 逻辑。
/// 
/// 注册服务需管理员权限，通过 ShellExecuteW("runas") 触发 UAC 提权。
/// 服务注册后以 SYSTEM 权限运行，通过 HTTP API (47891) 控制 mihomo 核心进程。
/// </summary>
public class HelperServiceManager
{
    private const string ServiceName = "WinUIClashHelperService";
    private const int HelperApiPort = 47891;
    private const string ServiceToken = "WinUIClashHelper";

    private readonly AppSettings _settings;
    private readonly ILogger<HelperServiceManager> _logger;

    public HelperServiceManager(AppSettings settings, ILogger<HelperServiceManager> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Helper Service HTTP API 端口</summary>
    public int ApiPort => HelperApiPort;

    /// <summary>Helper Service 鉴权 Token</summary>
    public string Token => ServiceToken;

    // ── 服务状态查询 ──

    public enum ServiceStatus { None, Present, Running }

    /// <summary>查询 Helper Service 状态（对齐 FlClash checkService）</summary>
    public async Task<ServiceStatus> CheckServiceAsync()
    {
        // 先检查 sc query
        try
        {
            var result = await RunScCommandAsync("query", ServiceName);
            if (result.ExitCode != 0)
                return ServiceStatus.None;

            if (result.Output.Contains("RUNNING"))
            {
                // 确认 API 可达（对齐 FlClash pingHelper）
                if (await PingHelperAsync())
                    return ServiceStatus.Running;
            }
            return ServiceStatus.Present;
        }
        catch
        {
            return ServiceStatus.None;
        }
    }

    /// <summary>检查当前进程是否以管理员权限运行</summary>
    public bool IsAdmin()
    {
        try
        {
            var identity = new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent());
            return identity.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // ── 服务注册（需 UAC 提权）──

    /// <summary>
    /// 注册 Helper Service（对齐 FlClash registerService）。
    /// 通过 ShellExecuteW("runas") 触发 UAC 提权，执行 sc create + sc start。
    /// 返回 true 表示注册成功或服务已运行。
    /// </summary>
    public async Task<bool> RegisterServiceAsync()
    {
        var status = await CheckServiceAsync();
        if (status == ServiceStatus.Running)
            return true;

        // 获取 Helper Service 可执行文件路径
        var helperPath = GetHelperExePath();
        if (helperPath == null)
        {
            _logger.LogError("找不到 Helper Service 可执行文件");
            return false;
        }

        // 构造 sc 命令
        var command = BuildScCreateCommand(helperPath, status);
        _logger.LogInformation("注册 Helper Service，命令: {Command}", command);

        // ShellExecuteW runas 触发 UAC 提权
        var runasResult = ShellExecuteRunas(command);
        if (!runasResult)
        {
            _logger.LogWarning("UAC 提权被拒绝或失败");
            return false;
        }

        // 等待服务就绪（最多 5 秒，对齐 FlClash retry 逻辑）
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(1000);
            var newStatus = await CheckServiceAsync();
            if (newStatus == ServiceStatus.Running)
            {
                _logger.LogInformation("Helper Service 注册并启动成功");
                return true;
            }
        }

        _logger.LogWarning("Helper Service 已注册但未在 5 秒内进入 Running 状态");
        return false;
    }

    /// <summary>卸载 Helper Service（需 UAC 提权）</summary>
    public async Task<bool> UnregisterServiceAsync()
    {
        var status = await CheckServiceAsync();
        if (status == ServiceStatus.None)
            return true;

        // 先停止服务
        if (status == ServiceStatus.Running)
        {
            var stopResult = await RunScCommandAsync("stop", ServiceName);
            await Task.Delay(2000);
        }

        // sc delete 需管理员权限
        var command = $"/c sc delete {ServiceName}";
        var runasResult = ShellExecuteRunas(command);
        if (!runasResult) return false;

        await Task.Delay(1000);
        var newStatus = await CheckServiceAsync();
        return newStatus == ServiceStatus.None;
    }

    // ── 通过 Helper API 控制 mihomo 进程 ──

    /// <summary>通过 Helper Service API 启动 mihomo 核心</summary>
    public async Task<bool> StartCoreViaHelperAsync(string corePath, string arguments)
    {
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };

            var payload = JsonSerializer.Serialize(new { path = corePath, arg = arguments });
            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            var response = await http.PostAsync(
                $"http://127.0.0.1:{HelperApiPort}/start?token={ServiceToken}", content);

            var body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode && string.IsNullOrEmpty(body))
            {
                _logger.LogInformation("通过 Helper Service 启动 mihomo 成功");
                return true;
            }

            _logger.LogWarning("Helper Service start 返回: {Status} {Body}", response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "通过 Helper Service 启动 mihomo 失败");
            return false;
        }
    }

    /// <summary>通过 Helper Service API 停止 mihomo 核心</summary>
    public async Task<bool> StopCoreViaHelperAsync()
    {
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
            var response = await http.PostAsync(
                $"http://127.0.0.1:{HelperApiPort}/stop?token={ServiceToken}", null);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "通过 Helper Service 停止 mihomo 失败");
            return false;
        }
    }

    // ── 内部方法 ──

    private async Task<bool> PingHelperAsync()
    {
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
            var response = await http.GetAsync($"http://127.0.0.1:{HelperApiPort}/ping");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private string? GetHelperExePath()
    {
        // 查找 Helper Service 可执行文件
        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(appDir, "WinUIClash.HelperService.exe"),
            Path.Combine(appDir, "Core", "WinUIClash.HelperService.exe"),
            Path.Combine(appDir, "Helper", "WinUIClash.HelperService.exe"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        // 开发模式下查找 build 输出
        var devPaths = new[]
        {
            Path.Combine(Directory.GetParent(appDir)?.Parent?.Parent?.FullName ?? "", 
                "WinUIClash.HelperService", "bin", "Debug", "net9.0-windows", "win-x64", "WinUIClash.HelperService.exe"),
            Path.Combine(Directory.GetParent(appDir)?.Parent?.Parent?.FullName ?? "", 
                "WinUIClash.HelperService", "bin", "Release", "net9.0-windows", "win-x64", "WinUIClash.HelperService.exe"),
        };

        foreach (var path in devPaths)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private string BuildScCreateCommand(string helperPath, ServiceStatus currentStatus)
    {
        // 对齐 FlClash：如果服务已存在但非 running，先 taskkill + sc delete 再重新 create
        var parts = new List<string> { "/c" };

        if (currentStatus == ServiceStatus.Present)
        {
            parts.AddRange(new[] { "taskkill", "/F", "/IM", $"{ServiceName}.exe", "&", "sc", "delete", ServiceName, "&" });
        }

        parts.AddRange(new[] { "sc", "create", ServiceName, $"binPath= \"{helperPath}\"", "start= auto", "&&", "sc", "start", ServiceName });

        return string.Join(" ", parts);
    }

    /// <summary>通过 ShellExecuteW runas 触发 UAC 提权执行命令</summary>
    private bool ShellExecuteRunas(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = command,
                UseShellExecute = true,
                Verb = "runas", // 触发 UAC 提权
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(30000); // 等待 UAC + 命令执行（最多 30 秒）
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ShellExecuteW runas 失败（可能用户拒绝 UAC）");
            return false;
        }
    }

    private async Task<ScResult> RunScCommandAsync(string action, string serviceName)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"{action} {serviceName}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit(5000);
            return new ScResult(process.ExitCode, output);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "sc {Action} {Service} 执行失败", action, serviceName);
            return new ScResult(-1, "");
        }
    }

    private record ScResult(int ExitCode, string Output);
}
