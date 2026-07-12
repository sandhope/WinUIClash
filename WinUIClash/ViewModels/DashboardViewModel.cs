using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 仪表盘 ViewModel — 网速图表、出站模式、流量统计、网络检测等
/// </summary>
public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly IClashService _clash;
    private readonly AppSettings _settings;
    private readonly DispatcherQueue _dispatcher;
    private bool _initialized;
    private DispatcherTimer? _connTimer;

    public DashboardViewModel(IClashService clash, AppSettings settings)
    {
        _clash = clash;
        _settings = settings;
        _dispatcher = DispatcherQueue.GetForCurrentThread()!;
        _clash.TrafficUpdated += OnTrafficUpdated;
        _clash.CoreStateChanged += HandleCoreStateChanged;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    // ── 核心状态 ──

    [ObservableProperty] private CoreState _coreState = CoreState.Stopped;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private SolidColorBrush _statusBrush = new(Color.FromArgb(255, 255, 107, 107));
    // FAB 启动按钮：颜色随核心状态联动（停止=绿引导启动 / 运行=橙红提示停止）
    [ObservableProperty] private SolidColorBrush _startButtonBrush = new(Color.FromArgb(255, 76, 175, 80));
    [ObservableProperty] private string _runtimeText = "";

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CoreToggleText));
    }

    private DateTime? _startTime;

    private void HandleCoreStateChanged(CoreState state)
    {
        _dispatcher.TryEnqueue(() =>
        {
            CoreState = state;

            // 启动中 / 停止中：仅反映过渡态
            if (state == CoreState.Starting)
            {
                StatusText = LocalizationHelper.GetString("DashStarting.Text");
                StatusBrush = new SolidColorBrush(Color.FromArgb(255, 255, 193, 7));
                StartButtonBrush = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));
                return;
            }
            if (state == CoreState.Stopping)
            {
                StatusText = LocalizationHelper.GetString("DashStopping.Text");
                StatusBrush = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0));
                StartButtonBrush = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));
                return;
            }

            // 核心常驻：进程存活 ≠ 已连接。状态文本/图标跟随“代理是否激活”(IsRunning)。
            StatusText = IsRunning
                ? LocalizationHelper.GetString("DashRunning.Text")
                : LocalizationHelper.GetString("DashStopped.Text");
            StatusBrush = IsRunning
                ? new SolidColorBrush(Color.FromArgb(255, 76, 175, 80))
                : new SolidColorBrush(Color.FromArgb(255, 255, 107, 107));
            // FAB 颜色随代理状态联动（对齐 FlClash startButton 动画）
            StartButtonBrush = IsRunning
                ? new SolidColorBrush(Color.FromArgb(255, 255, 87, 34))   // 橙红 = 提示停止
                : new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));  // 绿色 = 引导启动

            if (state == CoreState.Running)
            {
                _startTime ??= DateTime.Now;
                RuntimeText = LocalizationHelper.GetString("DashRuntime.Text") + Converters.TimeFormatter.Duration(TimeSpan.Zero);
                // 核心常驻就绪后刷新数据（不在这里启停系统代理，系统代理由 ToggleCoreAsync 控制）
                _ = RefreshActiveProfileAsync();
                _ = RefreshActiveProxyNodeAsync();
                _ = CheckIpAsync();
                // 校准 TUN 实际状态（虚拟网卡由核心创建，启动后才可知真实状态），保持 UI 与托盘一致
                _ = SyncTunStateAsync();
            }
            else
            {
                _startTime = null;
                RuntimeText = "";
            }
        });
    }

    [RelayCommand]
    private async Task ToggleCoreAsync()
    {
        try
        {
            // 核心常驻后台：FAB 仅切换“代理是否激活”，绝不启停进程。
            if (_isRunning)
                await DisconnectProxyAsync();
            else
                await ConnectProxyAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ToggleCore error: {ex.Message}");
        }
    }

    /// <summary>连接代理：确保常驻核心存活 → PATCH mode 为选定模式 → 开启系统代理。</summary>
    private async Task ConnectProxyAsync()
    {
        try
        {
            // 兜底：确保常驻核心进程存活（非 TUN 模式启动时已拉起）
            if (_clash.CoreState != CoreState.Running)
                await _clash.StartAsync();

            // 按用户选择的出站模式激活（直连选择视为 Rule，以便真正建立连接）
            var mode = _outboundMode == OutboundMode.Direct ? OutboundMode.Rule : _outboundMode;
            await _clash.SetOutboundModeAsync(mode);

            // 仅当用户开启了“系统代理”开关时才启用 Windows 系统代理（对齐 FlClash）
            if (_settings.SystemProxy)
                ServiceLocator.Get<SystemProxyService>().Enable();
            IsRunning = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Connect proxy error: {ex.Message}");
        }
    }

    /// <summary>断开代理：PATCH mode 为 direct → 关闭系统代理。核心继续常驻。</summary>
    private async Task DisconnectProxyAsync()
    {
        try
        {
            // 切回直连模式（核心继续常驻，0 性能开销）
            await _clash.SetOutboundModeAsync(OutboundMode.Direct);

            // 仅当用户开启了“系统代理”开关时才关闭（未开启则本就未启用，无需动作）
            if (_settings.SystemProxy)
                ServiceLocator.Get<SystemProxyService>().Disable();
            IsRunning = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Disconnect proxy error: {ex.Message}");
        }
    }

    // ── 实时网速 ──

    [ObservableProperty] private string _uploadSpeed = "0 B/s";
    [ObservableProperty] private string _downloadSpeed = "0 B/s";
    [ObservableProperty] private long _uploadValue;
    [ObservableProperty] private long _downloadValue;

    public ObservableCollection<Traffic> TrafficHistory { get; } = new();

    private static readonly int[] ChartRangeOptions = [60, 120, 300];

    [ObservableProperty] private int _chartTimeRange = 120;

    public string ChartTimeRangeLabel => ChartTimeRange switch
    {
        60 => "1m",
        300 => "5m",
        _ => "2m",
    };

    partial void OnChartTimeRangeChanged(int value)
    {
        OnPropertyChanged(nameof(ChartTimeRangeLabel));
        // Trim excess data points when shrinking the window
        while (TrafficHistory.Count > value)
            TrafficHistory.RemoveAt(0);
    }

    [RelayCommand]
    private void CycleChartRange()
    {
        var idx = Array.IndexOf(ChartRangeOptions, ChartTimeRange);
        ChartTimeRange = ChartRangeOptions[(idx + 1) % ChartRangeOptions.Length];
    }

    private void OnTrafficUpdated(Traffic t)
    {
        _dispatcher.TryEnqueue(() =>
        {
            UploadValue = t.Up;
            DownloadValue = t.Down;
            UploadSpeed = Converters.ByteFormatter.FormatSpeed(t.Up);
            DownloadSpeed = Converters.ByteFormatter.FormatSpeed(t.Down);

            TrafficHistory.Add(t);
            while (TrafficHistory.Count > ChartTimeRange) TrafficHistory.RemoveAt(0);

            if (_startTime.HasValue)
            {
                RuntimeText = LocalizationHelper.GetString("DashRuntime.Text") + Converters.TimeFormatter.Duration(DateTime.Now - _startTime.Value);
            }
        });
    }

    // ── 出站模式 ──

    [ObservableProperty] private OutboundMode _outboundMode = OutboundMode.Rule;
    [ObservableProperty] private bool _isModeRule = true;
    [ObservableProperty] private bool _isModeGlobal;
    [ObservableProperty] private bool _isModeDirect;

    public string OutboundModeLabel => OutboundMode switch
    {
        OutboundMode.Global => LocalizationHelper.GetString("DashModeGlobal.Content"),
        OutboundMode.Direct => LocalizationHelper.GetString("DashModeDirect.Content"),
        _ => LocalizationHelper.GetString("DashModeRule.Content"),
    };

    partial void OnOutboundModeChanged(OutboundMode value) => OnPropertyChanged(nameof(OutboundModeLabel));

    partial void OnIsModeRuleChanged(bool value)
    {
        if (!value) return;
        // 已连接：立即切换模式；未连接：仅记录偏好，连接时再应用
        if (IsRunning && OutboundMode != OutboundMode.Rule)
            _ = SetModeInternalAsync(OutboundMode.Rule);
        else
            SyncModeState(OutboundMode.Rule);
    }
    partial void OnIsModeGlobalChanged(bool value)
    {
        if (!value) return;
        if (IsRunning && OutboundMode != OutboundMode.Global)
            _ = SetModeInternalAsync(OutboundMode.Global);
        else
            SyncModeState(OutboundMode.Global);
    }
    partial void OnIsModeDirectChanged(bool value)
    {
        if (!value) return;
        if (IsRunning)
            _ = DisconnectProxyAsync();   // 选择直连 = 断开代理
        else
            SyncModeState(OutboundMode.Direct);
    }

    private async Task SetModeInternalAsync(OutboundMode mode)
    {
        await _clash.SetOutboundModeAsync(mode);
        SyncModeState(mode);
    }

    private void SyncModeState(OutboundMode mode)
    {
        OutboundMode = mode;
        IsModeRule = mode == OutboundMode.Rule;
        IsModeGlobal = mode == OutboundMode.Global;
        IsModeDirect = mode == OutboundMode.Direct;
    }

    // ── 核心启停按钮文本（FAB：启动/停止核心，对齐 FlClash isStart）──

    public string CoreToggleText => IsRunning
        ? LocalizationHelper.GetString("DashCoreStop.Text")
        : LocalizationHelper.GetString("DashCoreStart.Text");

    // ── 流量统计 ──

    [ObservableProperty] private string _totalUpload = "0 B";
    [ObservableProperty] private string _totalDownload = "0 B";
    [ObservableProperty] private int _activeConnections;
    [ObservableProperty] private int _ruleCount;
    [ObservableProperty] private int _providerCount;

    public async Task RefreshProviderCountAsync()
    {
        try
        {
            var providers = await _clash.GetExternalProvidersAsync();
            ProviderCount = providers.Count;
        }
        catch { ProviderCount = 0; }
    }

    public async Task RefreshRuleCountAsync()
    {
        try
        {
            var rules = await _clash.GetRulesAsync();
            RuleCount = rules.Count;
        }
        catch { RuleCount = 0; }
    }

    public async Task RefreshTotalTrafficAsync()
    {
        var total = _clash.GetTotalTraffic();
        TotalUpload = Converters.ByteFormatter.Format(total.Up);
        TotalDownload = Converters.ByteFormatter.Format(total.Down);
    }

    public async Task RefreshConnectionCountAsync()
    {
        try
        {
            var connections = await _clash.GetConnectionsAsync();
            ActiveConnections = connections.Count;
        }
        catch { ActiveConnections = 0; }
    }

    [RelayCommand]
    private async Task ResetTrafficAsync()
    {
        await _clash.ResetTrafficAsync();
        TotalUpload = "0 B";
        TotalDownload = "0 B";
    }

    // ── 网络检测 ──

    [ObservableProperty] private string _externalIp = "";
    [ObservableProperty] private string _countryFlag = "---";
    [ObservableProperty] private bool _isCheckingIp;

    [RelayCommand]
    private async Task CheckIpAsync()
    {
        IsCheckingIp = true;
        try
        {
            var info = await _clash.GetIpInfoAsync();
            ExternalIp = info.Ip;
            CountryFlag = CountryCodeToFlag(info.CountryCode);
        }
        catch
        {
            ExternalIp = LocalizationHelper.GetString("DashIpFailed.Text");
            CountryFlag = "❌";
        }
        finally
        {
            IsCheckingIp = false;
        }
    }

    // ── 内存 ──

    [ObservableProperty] private string _coreMemory = "--";
    [ObservableProperty] private string _coreVersion = "--";

    [RelayCommand]
    private async Task RefreshMemoryAsync()
    {
        var bytes = await _clash.GetCoreMemoryAsync();
        CoreMemory = Converters.ByteFormatter.Format(bytes);
    }

    // ── 内网 IP ──

    [ObservableProperty] private string _localIp = "";

    public async Task RefreshLocalIpAsync()
    {
        try
        {
            var host = System.Net.Dns.GetHostName();
            var addresses = await System.Net.Dns.GetHostAddressesAsync(host);
            var ipv4 = addresses
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            LocalIp = ipv4?.ToString() ?? LocalizationHelper.GetString("DashIpLocalFailed.Text");
        }
        catch
        {
            LocalIp = LocalizationHelper.GetString("DashIpLocalFailed.Text");
        }
    }

    // ── 活跃代理节点 ──

    [ObservableProperty] private string? _activeProxyNode;

    public async Task RefreshActiveProxyNodeAsync()
    {
        try
        {
            var groups = await _clash.GetProxyGroupsAsync();
            // Show the selected node of the first proxy group (usually the main selector)
            var now = groups.FirstOrDefault()?.Now;
            ActiveProxyNode = string.IsNullOrEmpty(now) ? null : now;
        }
        catch
        {
            ActiveProxyNode = null;
        }
    }

    // ── 活跃配置名 ──

    [ObservableProperty] private string? _activeProfileName;

    public async Task RefreshActiveProfileAsync()
    {
        try
        {
            var profiles = await _clash.GetProfilesAsync();
            var active = profiles.FirstOrDefault(p => p.IsActive);
            ActiveProfileName = string.IsNullOrEmpty(active?.Label) ? null : active!.Label;
        }
        catch
        {
            ActiveProfileName = null;
        }
    }

    // ── TUN 模式状态 ──

    [ObservableProperty] private bool _isTunEnabled;

    // ── 快速切换 ──

    public bool IsSystemProxyOn
    {
        get => _settings.SystemProxy;
        set
        {
            if (_settings.SystemProxy != value)
            {
                _settings.SystemProxy = value;
                OnPropertyChanged();
                // 仅持久化偏好。系统代理的实际启用/禁用由开始/停止按钮按 _settings.SystemProxy 决定，
                // 单独切换此开关不做即时动作（对齐 FlClash）。
            }
        }
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.SystemProxy))
            _dispatcher.TryEnqueue(() => OnPropertyChanged(nameof(IsSystemProxyOn)));
        if (e.PropertyName == nameof(AppSettings.TunMode))
            _dispatcher.TryEnqueue(() => IsTunEnabled = _settings.TunMode);
    }

    [RelayCommand]
    private async Task ToggleTunModeAsync()
    {
        // 仅翻转发开关；实际的 UAC 提权 + 经 Helper 重启核心由 App.xaml.cs 的设置监听统一处理，
        // 保证仪表盘切换、托盘项、设置页三处行为一致。UI 状态由 OnSettingsPropertyChanged / 校准逻辑同步。
        _settings.TunMode = !IsTunEnabled;
    }

    /// <summary>从核心实际状态校准 TUN 开关 UI（核心启动后才可知虚拟网卡真实状态）</summary>
    public async Task SyncTunStateAsync()
    {
        try
        {
            var tun = await _clash.GetTunEnabledAsync();
            _dispatcher.TryEnqueue(() => IsTunEnabled = tun);
        }
        catch { /* 核心未就绪时不强行变更 */ }
    }

    // ── 初始化 ──

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        // 初始化本地化默认值
        StatusText = LocalizationHelper.GetString("DashStopped.Text");
        ExternalIp = LocalizationHelper.GetString("DashIpChecking.Text");
        LocalIp = LocalizationHelper.GetString("DashIpLocalFetching.Text");

        SyncModeState(_clash.GetOutboundMode());
        await RefreshTotalTrafficAsync();
        await RefreshConnectionCountAsync();
        await RefreshRuleCountAsync();
        await RefreshProviderCountAsync();
        await RefreshActiveProxyNodeAsync();
        await RefreshActiveProfileAsync();

        try { IsTunEnabled = await _clash.GetTunEnabledAsync(); }
        catch { IsTunEnabled = false; }

        await CheckIpAsync();
        await RefreshLocalIpAsync();
        await RefreshMemoryAsync();

        try { CoreVersion = await _clash.GetVersionAsync(); }
        catch { CoreVersion = "--"; }

        // 连接数轮询（每5秒）
        _connTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _connTimer.Tick += async (_, _) => await RefreshConnectionCountAsync();
        _connTimer.Start();
    }

    public void Dispose()
    {
        _connTimer?.Stop();
        _connTimer = null;
        _clash.TrafficUpdated -= OnTrafficUpdated;
        _clash.CoreStateChanged -= HandleCoreStateChanged;
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private static string CountryCodeToFlag(string code)
    {
        // 使用国家代码文本代替 Unicode 国旗表情，
        // 因为 Windows 10 的 Segoe UI Emoji 字体不支持区域指示符国旗渲染（显示为白框）。
        if (string.IsNullOrEmpty(code) || code.Length != 2) return "---";
        return code.ToUpper();
    }
}
