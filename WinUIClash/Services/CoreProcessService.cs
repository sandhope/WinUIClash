using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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
    // Job Object 句柄：父进程(WinUIClash)退出时由 OS 自动回收核心，杜绝僵尸进程
    private IntPtr _jobHandle = IntPtr.Zero;

    public bool IsRunning => _process is { HasExited: false };
    /// <summary>已探测到的核心二进制完整路径；未找到时为 null。</summary>
    public string? BinaryPath => string.IsNullOrEmpty(_binaryPath) ? null : _binaryPath;
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
    /// <returns>启动结果；失败时返回错误信息。</returns>
    public async Task<(bool Success, string? ErrorMessage)> StartAsync()
    {
        if (IsRunning) return (true, null);

        if (string.IsNullOrEmpty(_binaryPath))
            return (false, LocalizationHelper.GetString("ErrorCoreBinaryNotFound.Text"));

        if (!File.Exists(_binaryPath))
            return (false, $"mihomo binary not found: {_binaryPath}");

        if (!File.Exists(_configPath))
            return (false, string.Format(LocalizationHelper.GetString("ErrorConfigNotFound.Text"), _configPath));

        _outputBuffer.Clear();
        _errorBuffer.Clear();

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
        _process.OutputDataReceived += OnOutputDataReceived;
        _process.ErrorDataReceived += OnErrorDataReceived;

        _process.Start();

        // 将核心进程挂入 Job Object：父进程退出时由 OS 强制回收（KillOnJobClose），
        // 即使清理逻辑未执行（崩溃/强制退出）也能避免核心成为僵尸进程。
        AssignToJobObject(_process.Handle);

        // 异步读取输出（防止阻塞）
        _process.BeginErrorReadLine();
        _process.BeginOutputReadLine();

        // 给核心 500ms 时间，如果启动即崩溃则立刻捕获错误
        await Task.Delay(500);
        if (_process.HasExited)
        {
            var reason = BuildExitReason();
            return (false, $"mihomo exited immediately. {reason}");
        }

        ProcessStateChanged?.Invoke(true);
        return (true, null);
    }

    private readonly List<string> _outputBuffer = new(50);
    private readonly List<string> _errorBuffer = new(50);

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;
        lock (_outputBuffer)
        {
            _outputBuffer.Add(e.Data);
            if (_outputBuffer.Count > 50) _outputBuffer.RemoveAt(0);
        }
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;
        lock (_errorBuffer)
        {
            _errorBuffer.Add(e.Data);
            if (_errorBuffer.Count > 50) _errorBuffer.RemoveAt(0);
        }
    }

    /// <summary>获取最近的核心输出/错误日志，用于启动失败诊断。</summary>
    public string GetRecentLogs()
    {
        lock (_outputBuffer) lock (_errorBuffer)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- stdout --");
            foreach (var line in _outputBuffer) sb.AppendLine(line);
            sb.AppendLine("-- stderr --");
            foreach (var line in _errorBuffer) sb.AppendLine(line);
            return sb.ToString();
        }
    }

    private string BuildExitReason()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Exit code: {_process?.ExitCode}");
        var logs = GetRecentLogs();
        if (!string.IsNullOrWhiteSpace(logs.Replace("-- stdout --", "").Replace("-- stderr --", "")))
            sb.AppendLine(logs);
        return sb.ToString();
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
            CloseJobObject();
            ProcessStateChanged?.Invoke(false);
            return;
        }

        try
        {
            // 尝试通过 REST API 发送关闭信号
            try
            {
                using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
                await http.DeleteAsync($"http://127.0.0.1:{_settings.ApiPort}/shutdown");
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
            // 先关闭 Job Object，避免 KillOnJobClose 二次杀已退出进程（无害）
            CloseJobObject();
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
        var result = await StartAsync();
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage ?? "Restart failed");
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
        CloseJobObject();
    }

    // ── Job Object：父进程退出即回收核心子进程 ──

    private void AssignToJobObject(IntPtr processHandle)
    {
        try
        {
            var hJob = CreateJobObject(IntPtr.Zero, null);
            if (hJob == IntPtr.Zero) return;

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = 0x2000 // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var pInfo = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, pInfo, false);
                if (!SetInformationJobObject(hJob, JobObjectInfoType.ExtendedLimitInformation, pInfo, (uint)length))
                    return;
            }
            finally
            {
                Marshal.FreeHGlobal(pInfo);
            }

            if (!AssignProcessToJobObject(hJob, processHandle))
            {
                CloseHandle(hJob);
                return;
            }

            // 保持句柄存活（关闭会触发 KillOnJobClose），直到 StopAsync/Dispose
            _jobHandle = hJob;
        }
        catch
        {
            // 若进程已处于其他 Job（如某些打包环境），静默跳过，清理逻辑仍会尝试 StopAsync
        }
    }

    private void CloseJobObject()
    {
        if (_jobHandle != IntPtr.Zero)
        {
            try { CloseHandle(_jobHandle); } catch { }
            _jobHandle = IntPtr.Zero;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
