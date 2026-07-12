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
    // FAB 启动按钮：状态联动色（停止=绿引导启动 / 运行=橙红提示停止）
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
            IsRunning = state == CoreState.Running;
            StatusText = state switch
            {
                CoreState.Running => LocalizationHelper.GetString("DashRunning.Text"),
                CoreState.Starting => LocalizationHelper.GetString("DashStarting.Text"),
                CoreState.Stopping => LocalizationHelper.GetString("DashStopping.Text"),
                _ => LocalizationHelper.GetString("DashStopped.Text")
            };
            StatusBrush = state switch
            {
                CoreState.Running => new SolidColorBrush(Color.FromArgb(255, 76, 175, 80)),
                CoreState.Starting => new SolidColorBrush(Color.FromArgb(255, 255, 193, 7)),
                CoreState.Stopping => new SolidColorBrush(Color.FromArgb(255, 255, 152, 0)),
                _ => new SolidColorBrush(Color.FromArgb(255, 255, 107, 107))
            };
            if (state == CoreState.Running)
            {
                _startTime = DateTime.Now;
                RuntimeText = LocalizationHelper.GetString("DashRuntime.Text") + Converters.TimeFormatter.Duration(TimeSpan.Zero);
                // Refresh profile, proxy, and IP info when core starts
                _ = RefreshActiveProfileAsync();
                _ = RefreshActiveProxyNodeAsync();
                _ = CheckIpAsync();
            }
            else
            {
                _startTime = null;
                RuntimeText = "";
            }
        });
    }

    [RelayCommand]
    private async Task ToggleProxyAsync()
    {
        try
        {
            // 仅切换系统代理（核心由应用启动/退出时自动管理，用户操作不启停核心）
            IsSystemProxyOn = !IsSystemProxyOn;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ToggleProxy error: {ex.Message}");
        }
        await Task.CompletedTask;
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
        if (value && OutboundMode != OutboundMode.Rule)
            _ = SetModeInternalAsync(OutboundMode.Rule);
    }
    partial void OnIsModeGlobalChanged(bool value)
    {
        if (value && OutboundMode != OutboundMode.Global)
            _ = SetModeInternalAsync(OutboundMode.Global);
    }
    partial void OnIsModeDirectChanged(bool value)
    {
        if (value && OutboundMode != OutboundMode.Direct)
            _ = SetModeInternalAsync(OutboundMode.Direct);
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

    // ── 代理开关文本（FAB 按钮：启动/停止系统代理，不启停核心）──

    public string CoreToggleText => IsSystemProxyOn
        ? LocalizationHelper.GetString("DashProxyStop.Text")
        : LocalizationHelper.GetString("DashProxyStart.Text");

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
                // FAB 颜色随代理状态联动（关=绿引导启动 / 开=橙红提示停止）
                StartButtonBrush = value
                    ? new SolidColorBrush(Color.FromArgb(255, 255, 87, 34))
                    : new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));
                OnPropertyChanged(nameof(CoreToggleText));
                // 代理状态切换后重新检测 IP（对齐 FlClash checkIpNumProvider 触发机制）
                // 延迟 1.5 秒等待系统代理生效
                _ = Task.Delay(1500).ContinueWith(_ => _dispatcher.TryEnqueue(() =>
                {
                    _ = CheckIpAsync();
                }));
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
        try
        {
            var newState = !IsTunEnabled;
            await _clash.SetTunEnabledAsync(newState);
            IsTunEnabled = newState;
            _settings.TunMode = newState;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toggle TUN error: {ex.Message}");
        }
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
