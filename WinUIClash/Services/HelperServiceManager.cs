using System.Diagnostics;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
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

    /// <summary>查询 Helper Service 状态（对齐 FlClash checkService）。
    /// 快速路径优先：先尝试 PingHelperAsync（200ms 超时），命中则直接返回 Running，无需执行慢速 sc query。
    /// 仅在 ping 失败时才回退到 SCM 查询（判断服务是否已安装但未运行/已崩溃）。 </summary>
    public async Task<ServiceStatus> CheckServiceAsync()
    {
        // 快速路径：API 可达 → 服务必然处于 Running 态（TcpListener 绑定 = 进程存活且正常）
        if (await PingHelperAsync())
            return ServiceStatus.Running;

        // 慢速回退：通过 SCM 确认服务是否存在（可能已安装但未运行 / 崩溃）
        try
        {
            var result = await RunScCommandAsync("query", ServiceName);
            if (result.ExitCode != 0)
                return ServiceStatus.None;

            // scm 说 RUNNING 但 ping 不通 → 旧版 HttpListener 绑定失败等异常态
            if (result.Output.Contains("RUNNING"))
                return ServiceStatus.Present; // 视同"需要重启"

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
    /// 注册/确保 Helper Service 可用（对齐 FlClash registerService）。
    /// UAC 策略（核心诉求：除“首次安装/替换旧服务”外绝不再弹 UAC）：
    ///  - Running：直接复用，不弹 UAC。
    ///  - Present 且二进制与当前 Helper 不一致（旧版/损坏残留）：一次性
    ///    sc stop &amp; sc delete &amp; sc create &amp; sc start（仅一次 UAC）替换为新版本。
    ///    这是关键修复——旧版服务若不替换，会陷入“反复 sc start 提权又失败”的死循环（正是 TUN 反复弹 UAC 的根因）。
    ///  - Present 且二进制一致（我们自己安装但意外停止）：先尝试“非提权” sc start（管理员用户通常可行，不弹 UAC），
    ///    失败再提权 sc start 一次（极少的异常恢复，非常规路径）。
    ///  - None（从未安装）：首次安装（唯一一次 UAC），LocalSystem + start=auto 常驻。
    /// 返回 true 表示服务已运行（或本次成功拉起）。
    /// </summary>
    public async Task<bool> RegisterServiceAsync()
    {
        var status = await CheckServiceAsync();
        if (status == ServiceStatus.Running)
            return true; // 已常驻，直接复用，不弹 UAC

        if (status == ServiceStatus.Present)
        {
            // 已安装但当前未运行：先尝试直接启动（先非提权，失败再提权一次）。
            // 多数情况下旧服务指向的 exe 已是被覆盖的新版本，sc start 即可拉起，无需重装、不弹 UAC。
            bool running = await TryStartServiceNonElevatedAsync();
            if (!running)
            {
                // 非提权启动失败（普通用户无权限）→ 提权启动一次（这是“首次启动弹一次 UAC”的正常场景之一）
                var started = await ShellExecuteRunasAsync($"/c sc start {ServiceName}");
                if (started)
                    running = await WaitForRunningAsync();
            }
            if (running)
                return true;

            // 启动失败（服务损坏/卡死/被禁用等）：一次性重新安装（仅一次 UAC）替换旧注册，
            // 避免“反复 sc start 提权又失败”的死循环（这正是此前 TUN 反复弹 UAC 的根因）。
            _logger.LogWarning("已安装的 Helper Service 无法启动，重新安装（一次性 UAC）");
            var reinstallPath = GetHelperExePath();
            if (reinstallPath == null)
            {
                _logger.LogError("找不到 Helper Service 可执行文件");
                return false;
            }
            var cmd = $"/c sc stop {ServiceName} & timeout /t 2 /nobreak >nul & sc delete {ServiceName} & sc create {ServiceName} binPath= \"{reinstallPath}\" start= auto obj= LocalSystem & sc start {ServiceName}";
            var runas = await ShellExecuteRunasAsync(cmd);
            if (!runas) return false;
            return await WaitForRunningAsync();
        }

        // 从未安装：首次安装（唯一一次 UAC）。以 LocalSystem 身份、开机自启常驻。
        var helperPath = GetHelperExePath();
        if (helperPath == null)
        {
            _logger.LogError("找不到 Helper Service 可执行文件");
            return false;
        }

        var command = $"/c sc create {ServiceName} binPath= \"{helperPath}\" start= auto obj= LocalSystem & sc start {ServiceName}";
        _logger.LogInformation("首次安装 Helper Service（将弹 UAC），命令: {Command}", command);

        var runasResult = await ShellExecuteRunasAsync(command);
        if (!runasResult)
        {
            _logger.LogWarning("UAC 提权被拒绝或失败");
            return false;
        }

        return await WaitForRunningAsync();
    }

    /// <summary>以当前（非提权）进程执行 sc start，尝试启动已安装的服务。
    /// 普通用户通常无 SeServiceLogonRight，会失败——此方法的存在只是为了“能启动就不弹 UAC”。</summary>
    private async Task<bool> TryStartServiceNonElevatedAsync()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"start {ServiceName}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            process.Start();
            await process.WaitForExitAsync();
            // 给 SCM 一点时间把服务带入 Running
            await Task.Delay(1500);
            return await CheckServiceAsync() == ServiceStatus.Running;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "非提权 sc start 失败");
            return false;
        }
    }

    /// <summary>轮询等待服务进入 Running（最多 5 秒，200ms 间隔）</summary>
    private async Task<bool> WaitForRunningAsync()
    {
        for (int i = 0; i < 25; i++)
        {
            await Task.Delay(200);
            if (await PingHelperAsync())
            {
                _logger.LogInformation("Helper Service 已进入 Running 状态 (API 可达)");
                return true;
            }
        }
        _logger.LogWarning("Helper Service 未在 5 秒内进入 Running 状态（可能依赖缺失导致启动失败）");
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
        var runasResult = await ShellExecuteRunasAsync(command);
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
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };

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
        // 快速 TCP 预检：端口未监听时立即返回 false，避免 HttpClient 在 2 秒超时内抛出大量
        // TaskCanceledException（既刷屏又拖慢启动）。仅当端口确实开放时才发 HTTP 请求。
        if (!await IsTcpPortOpenAsync("127.0.0.1", HelperApiPort, 300))
            return false;
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
            var response = await http.GetAsync($"http://127.0.0.1:{HelperApiPort}/ping");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>在 timeoutMs 内探测 TCP 端口是否可连接。端口关闭/拒绝时快速返回 false；
    /// 仅当连接挂起（极少见的回环异常）才会在超时后返回 false，绝不抛出 TaskCanceledException。</summary>
    private static async Task<bool> IsTcpPortOpenAsync(string host, int port, int timeoutMs)
    {
        try
        {
            using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            using var cts = new CancellationTokenSource(timeoutMs);
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), cts.Token);
            return socket.Connected;
        }
        catch
        {
            return false;
        }
    }

    private string? GetHelperExePath()
    {
        // 查找 Helper Service 可执行文件（必须是“完整目录”——含依赖 DLL/deps.json/runtimeconfig，
        // 否则服务注册成功却启动即崩，被误判为 UAC 被拒）。
        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        // 优先级 1：随主程序发布的完整拷贝目录（构建时由 csproj CopyHelperService 复制）
        var candidates = new[]
        {
            Path.Combine(appDir, "Core", "WinUIClash.HelperService", "WinUIClash.HelperService.exe"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path) && Directory.Exists(Path.GetDirectoryName(path)))
                return path;
        }

        // 优先级 2：开发模式下直接引用 HelperService 的 build 输出（依赖齐全）
        var devPaths = new[]
        {
            Path.Combine(Directory.GetParent(appDir)?.Parent?.Parent?.Parent?.Parent?.FullName ?? "",
                "WinUIClash.HelperService", "bin", "Debug", "net10.0-windows10.0.26100.0", "win-x64", "WinUIClash.HelperService.exe"),
            Path.Combine(Directory.GetParent(appDir)?.Parent?.Parent?.Parent?.Parent?.FullName ?? "",
                "WinUIClash.HelperService", "bin", "Release", "net10.0-windows10.0.26100.0", "win-x64", "WinUIClash.HelperService.exe"),
        };

        foreach (var path in devPaths)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    /// <summary>通过 ShellExecuteW runas 触发 UAC 提权执行命令（后台线程执行，不阻塞 UI）</summary>
    /// <returns>true = UAC 已批准且命令已执行（是否真的成功以 RegisterServiceAsync 轮询 CheckServiceAsync 为准）；false = 用户拒绝 UAC 或进程无法启动</returns>
    private Task<bool> ShellExecuteRunasAsync(string command)
    {
        return Task.Run(() =>
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

                using var process = Process.Start(startInfo);
                if (process == null) return false;

                // 等待 UAC 确认 + 命令执行（最多 60 秒）。
                // 注意：不再以 sc start 的退出码判定成败。虚拟网卡创建较慢，sc start 常在
                // 服务尚未完全就绪时返回非 0，但服务随后会进入 Running 并成功创建网卡。
                // 真正的成败交由 RegisterServiceAsync 后续的 CheckServiceAsync 轮询判定。
                try { process.WaitForExit(60000); } catch { /* 超时也不影响轮询判定 */ }

                return true; // 仅当 UAC 被拒绝（下方 catch，NativeErrorCode=1223）才返回 false
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // 1223 = 用户取消了 UAC 提权
                _logger.LogWarning("UAC 提权被用户拒绝");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ShellExecuteW runas 执行异常");
                return false;
            }
        });
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
