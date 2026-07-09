using System.Diagnostics;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// 管理 ClashMeta (mihomo) 核心进程的生命周期
/// </summary>
public class CoreProcessService : IDisposable
{
    private Process? _process;
    private readonly AppSettings _settings;
    private string _binaryPath = "";
    private string _configPath = "";
    private string _workingDir = "";

    public bool IsRunning => _process is { HasExited: false };
    public event Action<bool>? ProcessStateChanged;

    public CoreProcessService(AppSettings settings)
    {
        _settings = settings;
        DetectBinary();
    }

    /// <summary>
    /// 自动检测 mihomo 可执行文件路径
    /// 查找顺序: 应用目录 → 系统 PATH → %LOCALAPPDATA%\WinUIClash
    /// </summary>
    private void DetectBinary()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var localDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinUIClash");

        var candidates = new[]
        {
            Path.Combine(appDir, "mihomo.exe"),
            Path.Combine(appDir, "clash.exe"),
            Path.Combine(appDir, "mihomo", "mihomo.exe"),
            Path.Combine(appDir, "core", "mihomo.exe"),
            Path.Combine(localDir, "mihomo.exe"),
            Path.Combine(localDir, "clash.exe"),
            Path.Combine(localDir, "core", "mihomo.exe"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                _binaryPath = path;
                _workingDir = Path.GetDirectoryName(path)!;
                break;
            }
        }

        // 尝试从 PATH 中查找
        if (string.IsNullOrEmpty(_binaryPath))
        {
            var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? [];
            foreach (var dir in pathDirs)
            {
                var mihomo = Path.Combine(dir, "mihomo.exe");
                if (File.Exists(mihomo))
                {
                    _binaryPath = mihomo;
                    _workingDir = dir;
                    break;
                }
            }
        }

        // 设置默认配置文件路径
        _configPath = Path.Combine(localDir, "config.yaml");
        Directory.CreateDirectory(localDir);
    }

    /// <summary>设置自定义二进制路径</summary>
    public void SetBinaryPath(string path)
    {
        _binaryPath = path;
        _workingDir = Path.GetDirectoryName(path) ?? "";
    }

    /// <summary>设置自定义配置文件路径</summary>
    public void SetConfigPath(string path)
    {
        _configPath = path;
    }

    /// <summary>
    /// 启动 mihomo 核心进程
    /// </summary>
    public Task StartAsync()
    {
        if (IsRunning) return Task.CompletedTask;

        if (string.IsNullOrEmpty(_binaryPath))
            throw new FileNotFoundException("未找到 mihomo 可执行文件，请在设置中指定路径");

        if (!File.Exists(_configPath))
            throw new FileNotFoundException($"配置文件不存在: {_configPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = _binaryPath,
            Arguments = $"-d \"{Path.GetDirectoryName(_configPath)}\" -f \"{_configPath}\"",
            WorkingDirectory = _workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _process = new Process { StartInfo = startInfo };
        _process.EnableRaisingEvents = true;
        _process.Exited += OnProcessExited;

        _process.Start();

        // 异步读取输出（防止阻塞）
        _process.BeginErrorReadLine();
        _process.BeginOutputReadLine();

        ProcessStateChanged?.Invoke(true);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止 mihomo 核心进程（优先优雅退出，超时后强制终止）
    /// </summary>
    public async Task StopAsync()
    {
        if (_process == null || _process.HasExited)
        {
            _process?.Dispose();
            _process = null;
            ProcessStateChanged?.Invoke(false);
            return;
        }

        try
        {
            // 尝试通过 REST API 发送关闭信号
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                await http.DeleteAsync($"http://127.0.0.1:{_settings.HttpPort + 1900}/shutdown");
            }
            catch { /* API 不可用时忽略 */ }

            // 等待进程退出
            var exited = _process.WaitForExit(3000);
            if (!exited)
            {
                _process.Kill(true);
                _process.WaitForExit(2000);
            }
        }
        catch { /* 进程可能已退出 */ }
        finally
        {
            _process.Dispose();
            _process = null;
            ProcessStateChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// 重启核心进程（用于配置变更后）
    /// </summary>
    public async Task RestartAsync()
    {
        await StopAsync();
        await Task.Delay(500);
        await StartAsync();
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        ProcessStateChanged?.Invoke(false);
    }

    public void Dispose()
    {
        if (_process != null && !_process.HasExited)
        {
            try { _process.Kill(); } catch { }
            _process.Dispose();
        }
    }
}
