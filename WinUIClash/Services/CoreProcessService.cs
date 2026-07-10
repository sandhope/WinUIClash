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
    /// 查找顺序: 打包的 Core 目录 → 应用目录 → %LOCALAPPDATA%\WinUIClash → 系统 PATH
    /// 支持 x64 和 ARM64 架构自动选择
    /// </summary>
    private void DetectBinary()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var localDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinUIClash");

        // 检测当前架构
        var isArm64 = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64;

        // 根据架构选择对应的二进制文件名
        var binaryName = isArm64 ? "mihomo-arm64.exe" : "mihomo.exe";

        var candidates = new[]
        {
            // 优先使用打包的 Core 目录
            Path.Combine(appDir, "Core", binaryName),
            Path.Combine(appDir, "Core", "mihomo.exe"),
            Path.Combine(appDir, "Core", "mihomo-arm64.exe"),
            // 应用目录下的其他常见位置
            Path.Combine(appDir, binaryName),
            Path.Combine(appDir, "mihomo.exe"),
            Path.Combine(appDir, "clash.exe"),
            Path.Combine(appDir, "mihomo", binaryName),
            Path.Combine(appDir, "core", binaryName),
            // 用户本地目录
            Path.Combine(localDir, binaryName),
            Path.Combine(localDir, "mihomo.exe"),
            Path.Combine(localDir, "clash.exe"),
            Path.Combine(localDir, "core", binaryName),
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
                var mihomo = Path.Combine(dir, binaryName);
                if (File.Exists(mihomo))
                {
                    _binaryPath = mihomo;
                    _workingDir = dir;
                    break;
                }
                // 兼容旧的命名
                mihomo = Path.Combine(dir, "mihomo.exe");
                if (File.Exists(mihomo))
                {
                    _binaryPath = mihomo;
                    _workingDir = dir;
                    break;
                }
            }
        }

        // 配置文件路径：优先用户自定义，否则使用打包的默认配置
        var userConfigPath = Path.Combine(localDir, "config.yaml");
        var bundledConfigPath = Path.Combine(appDir, "Core", "config.yaml");

        if (File.Exists(userConfigPath))
        {
            _configPath = userConfigPath;
        }
        else if (File.Exists(bundledConfigPath))
        {
            // 复制打包的默认配置到用户目录
            Directory.CreateDirectory(localDir);
            File.Copy(bundledConfigPath, userConfigPath, overwrite: false);
            _configPath = userConfigPath;
        }
        else
        {
            _configPath = userConfigPath;
            Directory.CreateDirectory(localDir);
        }
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
            throw new FileNotFoundException(LocalizationHelper.GetString("ErrorCoreBinaryNotFound.Text"));

        if (!File.Exists(_configPath))
            throw new FileNotFoundException(string.Format(LocalizationHelper.GetString("ErrorConfigNotFound.Text"), _configPath));

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
