using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>Windows 原生网络诊断目标（开发者工具）。</summary>
public enum WindowsDiagnosticTarget
{
    /// <summary>WSL 代理桥接（用户环境变量 + WSLENV）。</summary>
    Wsl = 0,

    /// <summary>终端代理环境变量（新开的 CMD / PowerShell / Git Bash）。</summary>
    Terminal = 1,

    /// <summary>Microsoft Store 回环豁免（允许本地代理访问 UWP 应用）。</summary>
    MicrosoftStore = 2,
}

/// <summary>单个诊断目标的结果。</summary>
public readonly record struct WindowsDiagnosticResult(
    WindowsDiagnosticTarget Target,
    string DisplayName,
    bool IsHealthy,
    string Message,
    string Detail);

/// <summary>
/// Windows 开发者网络诊断与修复服务。
/// 提供 WSL 代理桥接、终端代理环境变量、Microsoft Store 回环豁免三项的
/// 诊断 / 应用（一键修复）/ 重置。全部通过用户级环境变量与系统内置命令行
/// 工具实现，无需管理员权限。
/// </summary>
public sealed class WindowsNetworkDiagnosticService
{
    private const string MicrosoftStorePackageFamilyName = "Microsoft.WindowsStore_8wekyb3d8bbwe";
    private static readonly string[] WslEnvProxyTokens = ["HTTP_PROXY/u", "HTTPS_PROXY/u", "ALL_PROXY/u", "NO_PROXY/u"];
    private const string NoProxyValue = "localhost,127.0.0.1,::1";

    private readonly AppSettings _settings;

    public WindowsNetworkDiagnosticService(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>诊断单个目标当前状态。</summary>
    public Task<WindowsDiagnosticResult> DiagnoseAsync(WindowsDiagnosticTarget target, CancellationToken cancellationToken = default)
        => target switch
        {
            WindowsDiagnosticTarget.Wsl => DiagnoseWslAsync(cancellationToken),
            WindowsDiagnosticTarget.Terminal => Task.FromResult(DiagnoseTerminal()),
            WindowsDiagnosticTarget.MicrosoftStore => DiagnoseMicrosoftStoreAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

    /// <summary>应用（一键修复）单个目标。</summary>
    public async Task<WindowsDiagnosticResult> ApplyAsync(WindowsDiagnosticTarget target, CancellationToken cancellationToken = default)
    {
        switch (target)
        {
            case WindowsDiagnosticTarget.Wsl:
                ApplyTerminalProxyEnvironment();
                ApplyWslProxyBridge();
                return await DiagnoseWslAsync(cancellationToken).ConfigureAwait(false);
            case WindowsDiagnosticTarget.Terminal:
                ApplyTerminalProxyEnvironment();
                return DiagnoseTerminal();
            case WindowsDiagnosticTarget.MicrosoftStore:
                await ApplyMicrosoftStoreLoopbackAsync(cancellationToken).ConfigureAwait(false);
                return await DiagnoseMicrosoftStoreAsync(cancellationToken).ConfigureAwait(false);
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    /// <summary>重置（撤销）单个目标。</summary>
    public async Task<WindowsDiagnosticResult> ResetAsync(WindowsDiagnosticTarget target, CancellationToken cancellationToken = default)
    {
        switch (target)
        {
            case WindowsDiagnosticTarget.Wsl:
                ResetWslProxyBridge();
                return await DiagnoseWslAsync(cancellationToken).ConfigureAwait(false);
            case WindowsDiagnosticTarget.Terminal:
                ResetTerminalProxyEnvironment();
                return DiagnoseTerminal();
            case WindowsDiagnosticTarget.MicrosoftStore:
                await ResetMicrosoftStoreLoopbackAsync(cancellationToken).ConfigureAwait(false);
                return await DiagnoseMicrosoftStoreAsync(cancellationToken).ConfigureAwait(false);
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    // ── 诊断 ──

    private async Task<WindowsDiagnosticResult> DiagnoseWslAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("wsl.exe", ["--status"], TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        bool isAvailable = result.ExitCode == 0;
        string proxyUrl = BuildLocalProxyUrl();
        string wslEnv = GetUserEnv("WSLENV");
        bool hasBridge = ContainsAllWslEnvTokens(wslEnv);
        bool hasProxy = IsProxyEnvironmentConfigured(proxyUrl);
        bool isHealthy = isAvailable && hasBridge && hasProxy;
        string message = ResolveWslMessage(isHealthy, isAvailable, hasBridge);
        string detail = isAvailable ? $"WSLENV={wslEnv}; {BuildProxyEnvDetail()}" : result.Error;
        return new WindowsDiagnosticResult(WindowsDiagnosticTarget.Wsl, GetString("DevDiagWsl.Text"), isHealthy, message, detail);
    }

    private WindowsDiagnosticResult DiagnoseTerminal()
    {
        string proxyUrl = BuildLocalProxyUrl();
        bool isHealthy = IsProxyEnvironmentConfigured(proxyUrl);
        string message = isHealthy ? GetString("DevDiagTerminalReady.Text") : GetString("DevDiagTerminalMissing.Text");
        string detail = BuildProxyEnvDetail();
        return new WindowsDiagnosticResult(WindowsDiagnosticTarget.Terminal, GetString("DevDiagTerminal.Text"), isHealthy, message, detail);
    }

    private async Task<WindowsDiagnosticResult> DiagnoseMicrosoftStoreAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("CheckNetIsolation.exe", ["LoopbackExempt", "-s"], TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        bool isHealthy = result.Output.Contains(MicrosoftStorePackageFamilyName, StringComparison.OrdinalIgnoreCase);
        string message = isHealthy ? GetString("DevDiagStoreReady.Text") : GetString("DevDiagStoreMissing.Text");
        string detail = string.IsNullOrWhiteSpace(result.Output) ? result.Error : result.Output;
        return new WindowsDiagnosticResult(WindowsDiagnosticTarget.MicrosoftStore, GetString("DevDiagStore.Text"), isHealthy, message, detail);
    }

    private string ResolveWslMessage(bool isHealthy, bool isAvailable, bool hasBridge)
    {
        if (isHealthy) return GetString("DevDiagWslReady.Text");
        if (!isAvailable) return GetString("DevDiagWslUnavailable.Text");
        return hasBridge ? GetString("DevDiagWslProxyMissing.Text") : GetString("DevDiagWslBridgeMissing.Text");
    }

    // ── 应用 / 重置 ──

    private void ApplyTerminalProxyEnvironment()
    {
        string proxyUrl = BuildLocalProxyUrl();
        SetUserEnv("HTTP_PROXY", proxyUrl);
        SetUserEnv("HTTPS_PROXY", proxyUrl);
        SetUserEnv("ALL_PROXY", proxyUrl);
        SetUserEnv("NO_PROXY", NoProxyValue);
    }

    private void ApplyWslProxyBridge()
    {
        string current = GetUserEnv("WSLENV");
        var tokens = new List<string>(current.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (string token in WslEnvProxyTokens)
        {
            if (!tokens.Exists(t => StringComparer.OrdinalIgnoreCase.Equals(t, token)))
            {
                tokens.Add(token);
            }
        }

        SetUserEnv("WSLENV", string.Join(':', tokens));
    }

    private void ResetWslProxyBridge()
    {
        string current = GetUserEnv("WSLENV");
        var tokens = new List<string>(current.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        tokens.RemoveAll(t => Array.Exists(WslEnvProxyTokens, p => StringComparer.OrdinalIgnoreCase.Equals(p, t)));
        SetUserEnv("WSLENV", tokens.Count == 0 ? null : string.Join(':', tokens));
    }

    private void ResetTerminalProxyEnvironment()
    {
        SetUserEnv("HTTP_PROXY", null);
        SetUserEnv("HTTPS_PROXY", null);
        SetUserEnv("ALL_PROXY", null);
        SetUserEnv("NO_PROXY", null);
    }

    private async Task ApplyMicrosoftStoreLoopbackAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            "CheckNetIsolation.exe",
            ["LoopbackExempt", "-a", "-n=" + MicrosoftStorePackageFamilyName],
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);
        }
    }

    private async Task ResetMicrosoftStoreLoopbackAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            "CheckNetIsolation.exe",
            ["LoopbackExempt", "-d", "-n=" + MicrosoftStorePackageFamilyName],
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);
        }
    }

    // ── 工具 ──

    private string BuildLocalProxyUrl() => string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{_settings.MixedPort}");

    private static string GetUserEnv(string name) => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) ?? string.Empty;

    private static void SetUserEnv(string name, string? value) => Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);

    private bool IsProxyEnvironmentConfigured(string proxyUrl)
    {
        string http = GetUserEnv("HTTP_PROXY");
        string https = GetUserEnv("HTTPS_PROXY");
        string all = GetUserEnv("ALL_PROXY");
        string no = GetUserEnv("NO_PROXY");

        return StringComparer.OrdinalIgnoreCase.Equals(http, proxyUrl)
            && StringComparer.OrdinalIgnoreCase.Equals(https, proxyUrl)
            && StringComparer.OrdinalIgnoreCase.Equals(all, proxyUrl)
            && ContainsNoProxyLoopback(no);
    }

    private string BuildProxyEnvDetail()
        => $"HTTP_PROXY={GetUserEnv("HTTP_PROXY")}; HTTPS_PROXY={GetUserEnv("HTTPS_PROXY")}; ALL_PROXY={GetUserEnv("ALL_PROXY")}; NO_PROXY={GetUserEnv("NO_PROXY")}";

    private static bool ContainsNoProxyLoopback(string noProxy)
    {
        var tokens = noProxy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Array.Exists(tokens, t => StringComparer.OrdinalIgnoreCase.Equals(t, "localhost"))
            && Array.Exists(tokens, t => StringComparer.OrdinalIgnoreCase.Equals(t, "127.0.0.1"))
            && Array.Exists(tokens, t => StringComparer.OrdinalIgnoreCase.Equals(t, "::1"));
    }

    private static bool ContainsAllWslEnvTokens(string wslEnv)
    {
        var tokens = wslEnv.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string token in WslEnvProxyTokens)
        {
            if (!Array.Exists(tokens, t => StringComparer.OrdinalIgnoreCase.Equals(t, token)))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        string fileName, string[] args, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        try
        {
            if (!process.Start())
            {
                return (-1, string.Empty, "failed to start process");
            }
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            var output = await outTask.ConfigureAwait(false);
            var error = await errTask.ConfigureAwait(false);
            return (process.ExitCode, output, error);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            return (-1, string.Empty, "timeout");
        }
    }

    private static string GetString(string key) => LocalizationHelper.GetString(key);
}
