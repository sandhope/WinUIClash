using CommunityToolkit.Mvvm.ComponentModel;

namespace WinUIClash.Models;

/// <summary>
/// 应用设置（持久化到本地 JSON 文件）
/// </summary>
public partial class AppSettings : ObservableObject
{
    // ── 基础配置 ──

    [ObservableProperty] private int _mixedPort = 7890;
    [ObservableProperty] private int _socksPort = 7891;
    [ObservableProperty] private int _httpPort = 7892;
    [ObservableProperty] private string _logLevel = "info";
    [ObservableProperty] private string _userAgent = "clash-verge";
    [ObservableProperty] private int _keepAliveInterval = 30;
    [ObservableProperty] private string _testUrl = "https://www.gstatic.com/generate_204";
    [ObservableProperty] private bool _ipv6 = false;
    [ObservableProperty] private bool _allowLan = false;
    [ObservableProperty] private bool _unifiedDelay = false;
    [ObservableProperty] private bool _tcpConcurrent = false;
    [ObservableProperty] private bool _findProcessMode = false;
    [ObservableProperty] private bool _externalController = true;
    [ObservableProperty] private string _apiSecret = "";
    [ObservableProperty] private string _coreBinaryPath = "";

    // ── 应用设置 ──

    [ObservableProperty] private bool _minimizeOnExit = true;
    [ObservableProperty] private bool _autoLaunch = false;
    [ObservableProperty] private bool _silentLaunch = false;
    [ObservableProperty] private bool _autoRun = false;
    [ObservableProperty] private bool _autoRestart = true;
    [ObservableProperty] private bool _autoCheckUpdate = true;
    [ObservableProperty] private bool _closeConnections = false;
    [ObservableProperty] private bool _onlyStatisticsProxy = false;
    [ObservableProperty] private bool _showNotifications = true;
    [ObservableProperty] private string _language = "zh-CN";

    // ── 主题设置 ──

    [ObservableProperty] private string _themeMode = "System";   // Light / Dark / System
    [ObservableProperty] private int _primaryColorIndex = 0;

    // ── 系统代理 ──

    [ObservableProperty] private bool _systemProxy = false;
    [ObservableProperty] private string _bypassDomains = "localhost;127.0.0.1;<local>";
    [ObservableProperty] private bool _proxyGuardEnabled = true;
    [ObservableProperty] private int _proxyGuardInterval = 30;
    [ObservableProperty] private bool _tunMode = false;
    [ObservableProperty] private string _tunStack = "mixed";

    // ── 窗口状态 ──

    [ObservableProperty] private int _windowWidth = 1280;
    [ObservableProperty] private int _windowHeight = 800;
    [ObservableProperty] private int _windowX;
    [ObservableProperty] private int _windowY;
    [ObservableProperty] private bool _isSidebarCompact;
}
