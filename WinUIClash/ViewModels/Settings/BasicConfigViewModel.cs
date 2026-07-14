using CommunityToolkit.Mvvm.ComponentModel;
using WinUIClash.Helpers;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels.Settings;

/// <summary>
/// 基础配置 ViewModel — 端口、日志、UA、IPv6 等
/// Changes are live-applied to the running core via PATCH /configs (debounced 1s).
/// </summary>
public partial class BasicConfigViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IClashService _clash;
    private readonly DebounceHelper _debounce;

    public BasicConfigViewModel(AppSettings settings, IClashService clash)
    {
        _settings = settings;
        _clash = clash;
        // 防抖：连续配置变更合并为一次 PATCH（核心未运行时跳过）
        _debounce = new DebounceHelper(_ => PatchCoreAsync(), TimeSpan.FromSeconds(1));
    }

    private void SchedulePatchToCore()
    {
        _debounce.Pulse();
    }

    private async Task PatchCoreAsync()
    {
        if (_clash.CoreState != CoreState.Running) return;
        try
        {
            await _clash.PatchCoreConfigAsync(_settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PatchCoreConfig error: {ex.Message}");
        }
    }

    // 端口
    public int MixedPort
    {
        get => _settings.MixedPort;
        set { if (_settings.MixedPort != value) { _settings.MixedPort = value; OnPropertyChanged(); SchedulePatchToCore(); } }
    }

    public int SocksPort
    {
        get => _settings.SocksPort;
        set { if (_settings.SocksPort != value) { _settings.SocksPort = value; OnPropertyChanged(); SchedulePatchToCore(); } }
    }

    public int HttpPort
    {
        get => _settings.HttpPort;
        set { if (_settings.HttpPort != value) { _settings.HttpPort = value; OnPropertyChanged(); SchedulePatchToCore(); } }
    }

    // 日志级别
    public string[] LogLevelOptions { get; } = ["debug", "info", "warning", "error", "silent"];

    public string LogLevel
    {
        get => _settings.LogLevel;
        set { if (_settings.LogLevel != value) { _settings.LogLevel = value; OnPropertyChanged(); SchedulePatchToCore(); } }
    }

    // User-Agent
    public string[] UserAgentOptions { get; } = ["clash-verge", "ClashforWindows", "curl/8.0"];

    public string UserAgent
    {
        get => _settings.UserAgent;
        set { if (_settings.UserAgent != value) { _settings.UserAgent = value; OnPropertyChanged(); } }
    }

    // Keep-Alive
    public int KeepAliveInterval
    {
        get => _settings.KeepAliveInterval;
        set { if (_settings.KeepAliveInterval != value) { _settings.KeepAliveInterval = value; OnPropertyChanged(); SchedulePatchToCore(); } }
    }

    // 测速 URL
    public string TestUrl
    {
        get => _settings.TestUrl;
        set { if (_settings.TestUrl != value) { _settings.TestUrl = value; OnPropertyChanged(); } }
    }

    // 开关
    public bool Ipv6
    {
        get => _settings.Ipv6;
        set { if (_settings.Ipv6 != value) { _settings.Ipv6 = value; OnPropertyChanged(); SchedulePatchToCore(); } }
    }

    public bool AllowLan
    {
        get => _settings.AllowLan;
        set { if (_settings.AllowLan != value) { _settings.AllowLan = value; OnPropertyChanged(); SchedulePatchToCore(); } }
    }

    public bool UnifiedDelay
    {
        get => _settings.UnifiedDelay;
        set { if (_settings.UnifiedDelay != value) { _settings.UnifiedDelay = value; OnPropertyChanged(); SchedulePatchToCore(); } }
    }

    public bool TcpConcurrent
    {
        get => _settings.TcpConcurrent;
        set { if (_settings.TcpConcurrent != value) { _settings.TcpConcurrent = value; OnPropertyChanged(); SchedulePatchToCore(); } }
    }

    public string[] FindProcessModeOptions { get; } = ["off", "strict", "always"];

    public string FindProcessMode
    {
        get => _settings.FindProcessMode;
        set { if (_settings.FindProcessMode != value) { _settings.FindProcessMode = value; OnPropertyChanged(); SchedulePatchToCore(); } }
    }

    public bool ExternalController
    {
        get => _settings.ExternalController;
        set { if (_settings.ExternalController != value) { _settings.ExternalController = value; OnPropertyChanged(); } }
    }

    public string ApiSecret
    {
        get => _settings.ApiSecret;
        set { if (_settings.ApiSecret != value) { _settings.ApiSecret = value; OnPropertyChanged(); } }
    }

    public bool AutoRestart
    {
        get => _settings.AutoRestart;
        set { if (_settings.AutoRestart != value) { _settings.AutoRestart = value; OnPropertyChanged(); } }
    }

    public string CoreBinaryPath
    {
        get => _settings.CoreBinaryPath;
        set { if (_settings.CoreBinaryPath != value) { _settings.CoreBinaryPath = value; OnPropertyChanged(); } }
    }
}
