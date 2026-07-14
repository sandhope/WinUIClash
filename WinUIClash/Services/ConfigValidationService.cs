using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using WinUIClash;

namespace WinUIClash.Services;

/// <summary>
/// 使用 mihomo 核心的 <c>-t</c> 参数对 Clash/Mihomo 配置做真实语法与结构校验。
///
/// 这是独立改进项（非学自 ClashSharp）：ClashSharp 的 <c>MihomoProfileShapeValidator</c>
/// 只做静态形状检查、并在注释中明确声明“不调用 mihomo -t”。此处借助本机已下载的核心
/// 二进制获得更强的保证，防止无效订阅配置导致核心启动失败。
///
/// <list type="bullet">
///   <item><description>Invariants：不修改任何用户配置，仅把待校验文本写入 %TEMP% 临时文件并在结束后删除。</description></item>
///   <item><description>Thread safety：无状态，方法可并发调用（各自使用独立临时文件）。</description></item>
///   <item><description>Side effects：启动短生命周期的 mihomo 子进程；在 %TEMP% 创建/删除临时文件。</description></item>
/// </list>
/// </summary>
public class ConfigValidationService
{
    private readonly ILogger<ConfigValidationService>? _logger;

    public ConfigValidationService(ILogger<ConfigValidationService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>校验结果。</summary>
    public record ValidationResult(
        /// <summary>配置是否有效（或核心不可用时被跳过，视为有效）。</summary>
        bool Valid,
        /// <summary>因核心二进制不可用而跳过校验（应视为通过，不阻断导入）。</summary>
        bool Skipped,
        /// <summary>无效时的错误信息（来自 mihomo 进程输出）。</summary>
        string? Error);

    /// <summary>对一段 YAML 配置文本做完整校验。</summary>
    public async Task<ValidationResult> ValidateConfigTextAsync(string yaml)
    {
        var corePath = FindCoreBinary();
        if (corePath == null)
        {
            // 首次安装尚未下载核心时，无法校验，跳过而非误报无效，避免阻断导入。
            _logger?.LogWarning("配置校验跳过：未找到 mihomo 核心二进制");
            return new ValidationResult(Valid: true, Skipped: true, Error: null);
        }

        // 1. 规范化换行符
        var normalized = yaml.Replace("\r\n", "\n");

        // 2. 形状校验（快速预筛，避免把明显非配置文本交给核心）
        if (!HasExpectedShape(normalized))
        {
            return new ValidationResult(
                Valid: false,
                Skipped: false,
                Error: LocalizationHelper.GetString("ConfigValidationShapeError.Text"));
        }

        // 3. 写入临时文件
        var tmp = Path.Combine(Path.GetTempPath(), $"winuiclash_validate_{Guid.NewGuid():N}.yaml");
        try
        {
            await File.WriteAllTextAsync(tmp, normalized);

            // 4. 调用 mihomo -t -f <tmp> 做完整语法验证
            var (ok, error) = await RunMihomoTAsync(corePath, tmp);
            return new ValidationResult(Valid: ok, Skipped: false, Error: error);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 临时文件清理失败可忽略 */ }
        }
    }

    /// <summary>校验磁盘上的配置文件。</summary>
    public Task<ValidationResult> ValidateConfigFileAsync(string path)
    {
        if (!File.Exists(path))
            return Task.FromResult(new ValidationResult(Valid: false, Skipped: false, Error: $"文件不存在: {path}"));
        return ValidateConfigTextAsync(File.ReadAllText(path));
    }

    /// <summary>
    /// 运行 <c>mihomo -t -f &lt;file&gt; -d &lt;configDir&gt;</c> 校验配置。
    /// 退出码 0 视为有效；否则收集进程输出作为错误信息。
    /// </summary>
    private async Task<(bool Ok, string? Error)> RunMihomoTAsync(string corePath, string tmpFile)
    {
        var workingDir = Path.GetDirectoryName(corePath)!;
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinUIClash");

        var psi = new ProcessStartInfo
        {
            FileName = corePath,
            // -d 指向应用配置目录，使相对 geo 资源可被解析
            Arguments = $"-t -f \"{tmpFile}\" -d \"{configDir}\"",
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        try
        {
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // 超时保护：核心通常 1~2s 内完成校验
            if (!proc.WaitForExit(15000))
            {
                try { proc.Kill(true); } catch { /* 进程可能已退出 */ }
                return (false, LocalizationHelper.GetString("ConfigValidationTimeout.Text"));
            }

            var error = string.IsNullOrWhiteSpace(stderr.ToString()) ? stdout.ToString() : stderr.ToString();
            return (proc.ExitCode == 0, proc.ExitCode == 0 ? null : error.Trim());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "mihomo -t 校验进程启动失败");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// 基础形状校验：至少包含一个顶层映射键（行首非缩进后跟冒号）。
    /// 仅用于快速预筛，真正的 YAML/结构合法性由 mihomo -t 决定。
    /// </summary>
    private static bool HasExpectedShape(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) return false;

        foreach (var raw in yaml.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            if (!line.StartsWith(" ") && !line.StartsWith("\t") && line.Contains(':'))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 探测 mihomo 可执行文件路径（与 CoreProcessService 一致的查找顺序）。
    /// 找不到时返回 null（调用方据此跳过校验）。
    /// </summary>
    private static string? FindCoreBinary()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var localDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinUIClash");
        var isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        var binaryName = isArm64 ? "mihomo-arm64.exe" : "mihomo.exe";

        var candidates = new[]
        {
            Path.Combine(appDir, "Core", binaryName),
            Path.Combine(appDir, "Core", "mihomo.exe"),
            Path.Combine(appDir, "Core", "mihomo-arm64.exe"),
            Path.Combine(appDir, binaryName),
            Path.Combine(appDir, "mihomo.exe"),
            Path.Combine(localDir, binaryName),
            Path.Combine(localDir, "mihomo.exe"),
        };

        foreach (var path in candidates)
            if (File.Exists(path)) return path;

        return null;
    }
}
