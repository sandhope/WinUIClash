using Microsoft.Win32;

namespace WinUIClash.Services;

/// <summary>
/// 开机自启服务 — 通过注册表 Run 键管理
/// </summary>
public class AutoLaunchService
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WinUIClash";

    /// <summary>检查是否已设置开机自启</summary>
    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    /// <summary>启用开机自启</summary>
    public void Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exePath)) return;

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.SetValue(AppName, $"\"{exePath}\" --silent");
        }
        catch { /* 注册表写入失败时静默 */ }
    }

    /// <summary>禁用开机自启</summary>
    public void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch { /* 注册表写入失败时静默 */ }
    }

    /// <summary>根据设置同步开机自启状态</summary>
    public void ApplyState(bool shouldEnable)
    {
        if (shouldEnable && !IsEnabled())
            Enable();
        else if (!shouldEnable && IsEnabled())
            Disable();
    }
}
