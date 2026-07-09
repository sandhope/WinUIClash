using System.Text.Json;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// 将 AppSettings 持久化到本地 JSON 文件
/// 路径：%LOCALAPPDATA%\WinUIClash\settings.json
/// </summary>
public class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinUIClash");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly AppSettings _settings;
    private System.Timers.Timer? _debounceTimer;

    public SettingsService(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>从磁盘加载设置（启动时调用）</summary>
    public void Load()
    {
        if (!File.Exists(SettingsPath)) return;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var dto = JsonSerializer.Deserialize<SettingsDto>(json, JsonOptions);
            if (dto == null) return;

            // 基础配置
            _settings.MixedPort = dto.MixedPort > 0 ? dto.MixedPort : 7890;
            _settings.SocksPort = dto.SocksPort > 0 ? dto.SocksPort : 7891;
            _settings.HttpPort = dto.HttpPort > 0 ? dto.HttpPort : 7892;
            if (dto.LogLevel != null) _settings.LogLevel = dto.LogLevel;
            if (dto.UserAgent != null) _settings.UserAgent = dto.UserAgent;
            _settings.KeepAliveInterval = dto.KeepAliveInterval;
            if (dto.TestUrl != null) _settings.TestUrl = dto.TestUrl;
            _settings.Ipv6 = dto.Ipv6;
            _settings.AllowLan = dto.AllowLan;
            _settings.UnifiedDelay = dto.UnifiedDelay;
            _settings.TcpConcurrent = dto.TcpConcurrent;
            _settings.FindProcessMode = dto.FindProcessMode;
            _settings.ExternalController = dto.ExternalController;
            if (dto.ApiSecret != null) _settings.ApiSecret = dto.ApiSecret;
            if (dto.CoreBinaryPath != null) _settings.CoreBinaryPath = dto.CoreBinaryPath;

            // 应用设置
            _settings.MinimizeOnExit = dto.MinimizeOnExit;
            _settings.AutoLaunch = dto.AutoLaunch;
            _settings.SilentLaunch = dto.SilentLaunch;
            _settings.AutoRun = dto.AutoRun;
            _settings.AutoRestart = dto.AutoRestart;
            _settings.AutoCheckUpdate = dto.AutoCheckUpdate;
            _settings.CloseConnections = dto.CloseConnections;
            _settings.OnlyStatisticsProxy = dto.OnlyStatisticsProxy;

            // 主题
            if (dto.ThemeMode != null) _settings.ThemeMode = dto.ThemeMode;
            _settings.PrimaryColorIndex = dto.PrimaryColorIndex;

            // 系统代理
            _settings.SystemProxy = dto.SystemProxy;
            if (dto.BypassDomains != null) _settings.BypassDomains = dto.BypassDomains;
            _settings.ProxyGuardEnabled = dto.ProxyGuardEnabled;
            _settings.ProxyGuardInterval = dto.ProxyGuardInterval > 0 ? dto.ProxyGuardInterval : 30;
            _settings.TunMode = dto.TunMode;
            if (dto.TunStack != null) _settings.TunStack = dto.TunStack;

            // 窗口状态
            _settings.WindowWidth = dto.WindowWidth > 0 ? dto.WindowWidth : 1280;
            _settings.WindowHeight = dto.WindowHeight > 0 ? dto.WindowHeight : 800;
            _settings.WindowX = dto.WindowX;
            _settings.WindowY = dto.WindowY;
            _settings.IsSidebarCompact = dto.IsSidebarCompact;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Load failed: {ex.Message}");
        }
    }

    /// <summary>保存到磁盘（防抖：500ms 内多次调用只写一次）</summary>
    public void Save()
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();

        _debounceTimer = new System.Timers.Timer(500) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) => SaveImmediate();
        _debounceTimer.Start();
    }

    /// <summary>立即保存（退出时调用）</summary>
    public void SaveImmediate()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);

            var dto = new SettingsDto
            {
                // 基础配置
                MixedPort = _settings.MixedPort,
                SocksPort = _settings.SocksPort,
                HttpPort = _settings.HttpPort,
                LogLevel = _settings.LogLevel,
                UserAgent = _settings.UserAgent,
                KeepAliveInterval = _settings.KeepAliveInterval,
                TestUrl = _settings.TestUrl,
                Ipv6 = _settings.Ipv6,
                AllowLan = _settings.AllowLan,
                UnifiedDelay = _settings.UnifiedDelay,
                TcpConcurrent = _settings.TcpConcurrent,
                FindProcessMode = _settings.FindProcessMode,
                ExternalController = _settings.ExternalController,
                ApiSecret = _settings.ApiSecret,
                CoreBinaryPath = _settings.CoreBinaryPath,

                // 应用设置
                MinimizeOnExit = _settings.MinimizeOnExit,
                AutoLaunch = _settings.AutoLaunch,
                SilentLaunch = _settings.SilentLaunch,
                AutoRun = _settings.AutoRun,
                AutoRestart = _settings.AutoRestart,
                AutoCheckUpdate = _settings.AutoCheckUpdate,
                CloseConnections = _settings.CloseConnections,
                OnlyStatisticsProxy = _settings.OnlyStatisticsProxy,

                // 主题
                ThemeMode = _settings.ThemeMode,
                PrimaryColorIndex = _settings.PrimaryColorIndex,

                // 系统代理
                SystemProxy = _settings.SystemProxy,
                BypassDomains = _settings.BypassDomains,
                ProxyGuardEnabled = _settings.ProxyGuardEnabled,
                ProxyGuardInterval = _settings.ProxyGuardInterval,
                TunMode = _settings.TunMode,
                TunStack = _settings.TunStack,

                // 窗口状态
                WindowWidth = _settings.WindowWidth,
                WindowHeight = _settings.WindowHeight,
                WindowX = _settings.WindowX,
                WindowY = _settings.WindowY,
                IsSidebarCompact = _settings.IsSidebarCompact,
            };

            var json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Save failed: {ex.Message}");
        }
    }

    /// <summary>注册属性变更监听，自动触发保存</summary>
    public void EnableAutoSave()
    {
        _settings.PropertyChanged += (_, _) => Save();
    }
}

/// <summary>JSON 序列化 DTO</summary>
internal class SettingsDto
{
    // 基础配置
    public int MixedPort { get; set; } = 7890;
    public int SocksPort { get; set; } = 7891;
    public int HttpPort { get; set; } = 7892;
    public string LogLevel { get; set; } = "info";
    public string UserAgent { get; set; } = "clash-verge";
    public int KeepAliveInterval { get; set; } = 30;
    public string TestUrl { get; set; } = "https://www.gstatic.com/generate_204";
    public bool Ipv6 { get; set; }
    public bool AllowLan { get; set; }
    public bool UnifiedDelay { get; set; }
    public bool TcpConcurrent { get; set; }
    public bool FindProcessMode { get; set; }
    public bool ExternalController { get; set; } = true;
    public string ApiSecret { get; set; } = "";
    public string CoreBinaryPath { get; set; } = "";

    // 应用设置
    public bool MinimizeOnExit { get; set; } = true;
    public bool AutoLaunch { get; set; }
    public bool SilentLaunch { get; set; }
    public bool AutoRun { get; set; }
    public bool AutoRestart { get; set; } = true;
    public bool AutoCheckUpdate { get; set; } = true;
    public bool CloseConnections { get; set; }
    public bool OnlyStatisticsProxy { get; set; }

    // 主题
    public string ThemeMode { get; set; } = "System";
    public int PrimaryColorIndex { get; set; }

    // 系统代理
    public bool SystemProxy { get; set; }
    public string BypassDomains { get; set; } = "localhost;127.0.0.1;<local>";
    public bool ProxyGuardEnabled { get; set; } = true;
    public int ProxyGuardInterval { get; set; } = 30;
    public bool TunMode { get; set; }
    public string TunStack { get; set; } = "mixed";

    // 窗口状态
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public bool IsSidebarCompact { get; set; }
}
