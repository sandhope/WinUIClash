using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 仪表盘 ViewModel — 网速图表、出站模式、流量统计、网络检测等
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly IClashService _clash;
    private readonly DispatcherQueue _dispatcher;
    private bool _initialized;

    public DashboardViewModel(IClashService clash)
    {
        _clash = clash;
        _dispatcher = DispatcherQueue.GetForCurrentThread()!;
        _clash.TrafficUpdated += OnTrafficUpdated;
        _clash.CoreStateChanged += HandleCoreStateChanged;

        // 初始化历史数据（60秒模拟）
        var rng = new Random();
        for (int i = 60; i > 0; i--)
        {
            TrafficHistory.Add(new Traffic
            {
                Up = rng.NextInt64(50_000, 1_500_000),
                Down = rng.NextInt64(200_000, 8_000_000),
                Timestamp = DateTime.Now.AddSeconds(-i)
            });
        }
    }

    // ── 核心状态 ──

    [ObservableProperty] private CoreState _coreState = CoreState.Stopped;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "已停止";
    [ObservableProperty] private SolidColorBrush _statusBrush = new(Color.FromArgb(255, 255, 107, 107));
    [ObservableProperty] private string _runtimeText = "";

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(CoreToggleText));

    private DateTime? _startTime;

    private void HandleCoreStateChanged(CoreState state)
    {
        _dispatcher.TryEnqueue(() =>
        {
            CoreState = state;
            IsRunning = state == CoreState.Running;
            StatusText = state switch
            {
                CoreState.Running => "运行中",
                CoreState.Starting => "启动中…",
                CoreState.Stopping => "停止中…",
                _ => "已停止"
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
                RuntimeText = "运行 0s";
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
        if (IsRunning)
            await _clash.StopAsync();
        else
            await _clash.StartAsync();
    }

    // ── 实时网速 ──

    [ObservableProperty] private string _uploadSpeed = "0 B/s";
    [ObservableProperty] private string _downloadSpeed = "0 B/s";
    [ObservableProperty] private long _uploadValue;
    [ObservableProperty] private long _downloadValue;

    public ObservableCollection<Traffic> TrafficHistory { get; } = new();

    private void OnTrafficUpdated(Traffic t)
    {
        _dispatcher.TryEnqueue(() =>
        {
            UploadValue = t.Up;
            DownloadValue = t.Down;
            UploadSpeed = Converters.ByteFormatter.FormatSpeed(t.Up);
            DownloadSpeed = Converters.ByteFormatter.FormatSpeed(t.Down);

            TrafficHistory.Add(t);
            if (TrafficHistory.Count > 120) TrafficHistory.RemoveAt(0);

            if (_startTime.HasValue)
            {
                RuntimeText = "运行 " + Converters.TimeFormatter.Duration(DateTime.Now - _startTime.Value);
            }
        });
    }

    // ── 出站模式 ──

    [ObservableProperty] private OutboundMode _outboundMode = OutboundMode.Rule;
    [ObservableProperty] private bool _isModeRule = true;
    [ObservableProperty] private bool _isModeGlobal;
    [ObservableProperty] private bool _isModeDirect;

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
        _isModeRule = mode == OutboundMode.Rule;
        _isModeGlobal = mode == OutboundMode.Global;
        _isModeDirect = mode == OutboundMode.Direct;
        OnPropertyChanged(nameof(IsModeRule));
        OnPropertyChanged(nameof(IsModeGlobal));
        OnPropertyChanged(nameof(IsModeDirect));
    }

    // ── 核心开关文本 ──

    public string CoreToggleText => IsRunning ? "停止" : "启动";

    // ── 流量统计 ──

    [ObservableProperty] private string _totalUpload = "0 B";
    [ObservableProperty] private string _totalDownload = "0 B";

    public async Task RefreshTotalTrafficAsync()
    {
        var total = _clash.GetTotalTraffic();
        TotalUpload = Converters.ByteFormatter.Format(total.Up);
        TotalDownload = Converters.ByteFormatter.Format(total.Down);
    }

    [RelayCommand]
    private async Task ResetTrafficAsync()
    {
        await _clash.ResetTrafficAsync();
        TotalUpload = "0 B";
        TotalDownload = "0 B";
    }

    // ── 网络检测 ──

    [ObservableProperty] private string _externalIp = "检测中…";
    [ObservableProperty] private string _countryFlag = "🌐";
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
            ExternalIp = "检测失败";
            CountryFlag = "❌";
        }
        finally
        {
            IsCheckingIp = false;
        }
    }

    // ── 内存 ──

    [ObservableProperty] private string _coreMemory = "--";

    [RelayCommand]
    private async Task RefreshMemoryAsync()
    {
        var bytes = await _clash.GetCoreMemoryAsync();
        CoreMemory = Converters.ByteFormatter.Format(bytes);
    }

    // ── 内网 IP ──

    [ObservableProperty] private string _localIp = "获取中…";

    public async Task RefreshLocalIpAsync()
    {
        try
        {
            var host = System.Net.Dns.GetHostName();
            var addresses = await System.Net.Dns.GetHostAddressesAsync(host);
            var ipv4 = addresses
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            LocalIp = ipv4?.ToString() ?? "未获取";
        }
        catch
        {
            LocalIp = "未获取";
        }
    }

    // ── 初始化 ──

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        SyncModeState(_clash.GetOutboundMode());
        await RefreshTotalTrafficAsync();
        await CheckIpAsync();
        await RefreshLocalIpAsync();
        await RefreshMemoryAsync();
    }

    private static string CountryCodeToFlag(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 2) return "🌐";
        return string.Concat(code.ToUpper().Select(c => (char)(0x1F1E6 + c - 'A')));
    }
}
