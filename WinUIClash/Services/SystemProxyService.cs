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
            ?? throw new InvalidOperationException("无法打开 Internet Settings 注册表项");

        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", $"127.0.0.1:{_settings.HttpPort}", RegistryValueKind.String);
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
                    break;
                case nameof(AppSettings.HttpPort):
                case nameof(AppSettings.BypassDomains):
                    if (_settings.SystemProxy) Enable();
                    break;
            }
        };
    }

    /// <summary>
    /// 确保退出时关闭系统代理（防止代理残留）
    /// </summary>
    public void EnsureDisabledOnExit()
    {
        if (_settings.SystemProxy)
        {
            Disable();
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
