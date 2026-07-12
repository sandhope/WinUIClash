using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// 管理 Windows 系统代理设置（通过注册表 + WinINet 通知）
/// </summary>
public class SystemProxyService
{
    private const string InternetSettingsKey =
        @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionProxy = 38;
    private const int InternetOptionRefresh = 37;

    private readonly AppSettings _settings;
    private Timer? _guardTimer;

    public SystemProxyService(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// 启用系统代理，指向本地 Clash HTTP 端口
    /// </summary>
    public void Enable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true)
            ?? throw new InvalidOperationException(LocalizationHelper.GetString("ErrorRegistryAccess.Text"));

        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        // FlClash 对齐：系统代理指向核心的 mixed-port（混合端口，HTTP/SOCKS 均可），
        // 与 proxy_manager 使用的 proxyState.port == mixedPort 一致。
        key.SetValue("ProxyServer", $"127.0.0.1:{_settings.MixedPort}", RegistryValueKind.String);
        key.SetValue("ProxyOverride", _settings.BypassDomains, RegistryValueKind.String);

        NotifyProxyChanged();
    }

    /// <summary>
    /// 禁用系统代理
    /// </summary>
    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true);
        if (key == null) return;

        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
        NotifyProxyChanged();
    }

    /// <summary>
    /// 根据 AppSettings.SystemProxy 状态自动启用/禁用
    /// </summary>
    public void ApplyCurrentState()
    {
        if (_settings.SystemProxy) Enable();
        else Disable();
    }

    /// <summary>
    /// 注册属性变更监听，自动响应 SystemProxy 切换
    /// </summary>
    public void WatchSettings()
    {
        _settings.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(AppSettings.SystemProxy):
                    ApplyCurrentState();
                    // Start/stop guard based on proxy state
                    if (_settings.SystemProxy && _settings.ProxyGuardEnabled)
                        StartGuard();
                    else
                        StopGuard();
                    break;
                case nameof(AppSettings.MixedPort):
                case nameof(AppSettings.BypassDomains):
                    if (_settings.SystemProxy) Enable();
                    break;
                case nameof(AppSettings.ProxyGuardEnabled):
                    if (_settings.SystemProxy && _settings.ProxyGuardEnabled)
                        StartGuard();
                    else
                        StopGuard();
                    break;
                case nameof(AppSettings.ProxyGuardInterval):
                    if (_guardTimer != null)
                    {
                        StopGuard();
                        StartGuard();
                    }
                    break;
            }
        };
    }

    /// <summary>
    /// 启动代理守护：定期检查注册表值是否被篡改，自动恢复
    /// </summary>
    public void StartGuard()
    {
        StopGuard();
        var intervalMs = Math.Max(5, _settings.ProxyGuardInterval) * 1000;
        _guardTimer = new Timer(CheckGuard, null, intervalMs, intervalMs);
    }

    /// <summary>
    /// 停止代理守护
    /// </summary>
    public void StopGuard()
    {
        _guardTimer?.Dispose();
        _guardTimer = null;
    }

    /// <summary>
    /// 确保退出时关闭系统代理（防止代理残留）
    /// </summary>
    public void EnsureDisabledOnExit()
    {
        StopGuard();
        if (_settings.SystemProxy)
        {
            Disable();
        }
    }

    private void CheckGuard(object? state)
    {
        if (!_settings.SystemProxy) return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, false);
            if (key == null) return;

            var proxyEnable = key.GetValue("ProxyEnable") as int? ?? 0;
            var proxyServer = key.GetValue("ProxyServer") as string ?? "";
            var proxyOverride = key.GetValue("ProxyOverride") as string ?? "";

            var expectedServer = $"127.0.0.1:{_settings.MixedPort}";

            if (proxyEnable != 1 || proxyServer != expectedServer || proxyOverride != _settings.BypassDomains)
            {
                Enable();
                System.Diagnostics.Debug.WriteLine("[SystemProxyGuard] Re-applied proxy settings (values were changed)");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemProxyGuard] Check failed: {ex.Message}");
        }
    }

    private static void NotifyProxyChanged()
    {
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(
        IntPtr hInternet, int dwOption, IntPtr lpBuffer, int lpdwBufferLength);
}
