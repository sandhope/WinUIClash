using CommunityToolkit.Mvvm.ComponentModel;
using WinUIClash.Models;

namespace WinUIClash.ViewModels.Settings;

/// <summary>
/// 基础配置 ViewModel — 端口、日志、UA、IPv6 等
/// </summary>
public partial class BasicConfigViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public BasicConfigViewModel(AppSettings settings)
    {
        _settings = settings;
    }

    // 端口
    public int MixedPort
    {
        get => _settings.MixedPort;
        set { if (_settings.MixedPort != value) { _settings.MixedPort = value; OnPropertyChanged(); } }
    }

    public int SocksPort
    {
        get => _settings.SocksPort;
        set { if (_settings.SocksPort != value) { _settings.SocksPort = value; OnPropertyChanged(); } }
    }

    public int HttpPort
    {
        get => _settings.HttpPort;
        set { if (_settings.HttpPort != value) { _settings.HttpPort = value; OnPropertyChanged(); } }
    }

    // 日志级别
    public string[] LogLevelOptions { get; } = ["debug", "info", "warning", "error", "silent"];

    public string LogLevel
    {
        get => _settings.LogLevel;
        set { if (_settings.LogLevel != value) { _settings.LogLevel = value; OnPropertyChanged(); } }
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
        set { if (_settings.KeepAliveInterval != value) { _settings.KeepAliveInterval = value; OnPropertyChanged(); } }
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
        set { if (_settings.Ipv6 != value) { _settings.Ipv6 = value; OnPropertyChanged(); } }
    }

    public bool AllowLan
    {
        get => _settings.AllowLan;
        set { if (_settings.AllowLan != value) { _settings.AllowLan = value; OnPropertyChanged(); } }
    }

    public bool UnifiedDelay
    {
        get => _settings.UnifiedDelay;
        set { if (_settings.UnifiedDelay != value) { _settings.UnifiedDelay = value; OnPropertyChanged(); } }
    }

    public bool TcpConcurrent
    {
        get => _settings.TcpConcurrent;
        set { if (_settings.TcpConcurrent != value) { _settings.TcpConcurrent = value; OnPropertyChanged(); } }
    }

    public bool FindProcessMode
    {
        get => _settings.FindProcessMode;
        set { if (_settings.FindProcessMode != value) { _settings.FindProcessMode = value; OnPropertyChanged(); } }
    }

    public bool ExternalController
    {
        get => _settings.ExternalController;
        set { if (_settings.ExternalController != value) { _settings.ExternalController = value; OnPropertyChanged(); } }
    }
}
