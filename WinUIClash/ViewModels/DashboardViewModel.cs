using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flags.Icons;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUIClash.Models;
using WinUIClash.Services;
using WinUIClash.ViewModels.Settings;
using System.Collections.Generic;
using System.Linq;

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
        _themeVm = ServiceLocator.Get<ThemeSettingsViewModel>();
        _langVm = ServiceLocator.Get<LanguageSettingsViewModel>();
        _clash.TrafficUpdated += OnTrafficUpdated;
        _clash.CoreStateChanged += HandleCoreStateChanged;
        _clash.MemoryUpdated += OnMemoryUpdated;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        _themeVm.PropertyChanged += OnThemeVmPropertyChanged;
        _langVm.PropertyChanged += OnLangVmPropertyChanged;
        DashboardTiles = BuildTiles();

        // 只把"可见"磁贴放进绑定到 GridView 的集合。隐藏磁贴根本不进 GridView，
        // 从根本上避免"折叠 GridViewItem 容器"与虚拟化回收冲突（旧方案会导致取消一个全消失/残留空位）。
        VisibleDashboardTiles = new ObservableCollection<DashboardTile>();
        foreach (var tile in DashboardTiles)
        {
            tile.PropertyChanged += OnTilePropertyChanged;
            if (tile.IsVisible) VisibleDashboardTiles.Add(tile);
        }
    }

    // ── 仪表盘磁贴（可拖拽重排 + 显示/隐藏）──

    // 系统代理 / 虚拟网卡为固定卡片（网速图右侧），永不进入可拖拽磁贴集合
    private static readonly HashSet<DashboardTileType> FixedTileTypes =
        new() { DashboardTileType.SystemProxy, DashboardTileType.Tun };

    private static readonly DashboardTileType[] DefaultTileOrder =
    {
        DashboardTileType.OutboundMode,
        DashboardTileType.NetworkCheck,
        DashboardTileType.TrafficStats,
        DashboardTileType.ActiveNode,
        DashboardTileType.ActiveProfile,
        DashboardTileType.Memory,
    };

    // 新增磁贴默认隐藏，需用户在“编辑磁贴”中手动启用
    private static readonly HashSet<DashboardTileType> DefaultHiddenTileTypes =
        new()
        {
            DashboardTileType.Uptime,
            DashboardTileType.Connections,
            DashboardTileType.Language,
            DashboardTileType.Theme,
            DashboardTileType.AccentColor,
            DashboardTileType.ClipboardDetect,
        };

    /// <summary>全部磁贴（含隐藏）——供"编辑磁贴"对话框勾选用。</summary>
    public ObservableCollection<DashboardTile> DashboardTiles { get; }

    /// <summary>仅可见磁贴——绑定到 GridView。IsVisible 变化时增删此集合，保证 GridView 中永远没有隐藏项。</summary>
    public ObservableCollection<DashboardTile> VisibleDashboardTiles { get; }

    /// <summary>磁贴可见性切换时，同步维护 <see cref="VisibleDashboardTiles"/>。
    /// 显示时按其在全集中的相对顺序插入到可见集合的正确位置。</summary>
    private void OnTilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DashboardTile.IsVisible) || sender is not DashboardTile tile) return;

        if (tile.IsVisible)
        {
            if (!VisibleDashboardTiles.Contains(tile))
            {
                int sourceIndex = DashboardTiles.IndexOf(tile);
                int insertAt = VisibleDashboardTiles
                    .Select(item => DashboardTiles.IndexOf(item))
                    .TakeWhile(index => index < sourceIndex)
                    .Count();
                VisibleDashboardTiles.Insert(insertAt, tile);
            }
        }
        else
        {
            VisibleDashboardTiles.Remove(tile);
        }
    }

    private ObservableCollection<DashboardTile> BuildTiles()
    {
        var hidden = new HashSet<string>(_settings.DashboardTileHidden ?? new List<string>());
        var order = _settings.DashboardTileOrder;
        var types = new List<DashboardTileType>();
        if (order != null)
        {
            foreach (var id in order)
                if (Enum.TryParse<DashboardTileType>(id, out var t) && !types.Contains(t) && !FixedTileTypes.Contains(t))
                    types.Add(t);
        }
        foreach (var t in DefaultTileOrder)
            if (!types.Contains(t)) types.Add(t);
        // 默认隐藏的磁贴也进入集合，供"编辑磁贴"勾选启用
        foreach (var t in DefaultHiddenTileTypes)
            if (!types.Contains(t)) types.Add(t);

        var tiles = new ObservableCollection<DashboardTile>();
        foreach (var t in types)
        {
            var id = t.ToString();
            bool visible;
            if (hidden.Contains(id))
                visible = false;
            else if (DefaultHiddenTileTypes.Contains(t))
                // 默认隐藏：除非用户已显式加入顺序（即启用），否则不显示
                visible = order != null && order.Contains(id);
            else
                visible = true;
            tiles.Add(new DashboardTile(t, this) { IsVisible = visible });
        }
        return tiles;
    }

    /// <summary>拖拽重排后把当前顺序写回设置（经 EnableAutoSave 自动持久化）。
    /// 仅持久化"可见"磁贴：折叠的默认隐藏磁贴不写入，避免被误判为已启用。</summary>
    public void SaveTileOrder()
    {
        // 拖拽重排作用于 VisibleDashboardTiles，直接以其顺序持久化（保留用户拖拽后的可见磁贴顺序）。
        _settings.DashboardTileOrder = VisibleDashboardTiles.Select(t => t.Id).ToList();
    }

    /// <summary>可见性编辑后把隐藏的磁贴 id 写回设置；同时同步顺序，使默认隐藏磁贴的"启用"状态可持久化。</summary>
    public void SaveTileVisibility()
    {
        _settings.DashboardTileHidden = DashboardTiles.Where(t => !t.IsVisible).Select(t => t.Id).ToList();
        _settings.DashboardTileOrder = VisibleDashboardTiles.Select(t => t.Id).ToList();
    }

    // ── 新增可隐藏磁贴的逻辑（默认隐藏，需手动启用）──
    // 运行时长 / 活跃连接：复用 RuntimeText / ActiveConnections（已从网速图底部移出）
    // 语言 / 主题 / 主题色 / 剪贴板检测：直接作用于对应设置 VM

    private readonly ThemeSettingsViewModel _themeVm;
    private readonly LanguageSettingsViewModel _langVm;

    // 主题色预设（来自 ThemeSettingsViewModel.PrimaryColors）
    public ThemeSettingsViewModel.ThemeColor[] AccentColors => _themeVm.PrimaryColors;
    public string CurrentAccentColorName =>
        _themeVm.PrimaryColors.ElementAtOrDefault(_themeVm.PrimaryColorIndex)?.Name ?? "";

    /// <summary>当前选中的预设色索引（用于色板选中态黑框）。</summary>
    public int CurrentAccentColorIndex => _themeVm.PrimaryColorIndex;

    /// <summary>使用系统主题色开关（与设置页共用同一状态）。</summary>
    public bool TileUseSystemAccentColor
    {
        get => _themeVm.UseSystemAccentColor;
        set => _themeVm.UseSystemAccentColor = value;
    }

    /// <summary>启用系统色时禁用预设色板（与设置页 UsePresetColors 一致）。</summary>
    public bool TileUsePresetColors => _themeVm.UsePresetColors;

    public void SelectAccentColor(ThemeSettingsViewModel.ThemeColor color)
    {
        var idx = Array.FindIndex(_themeVm.PrimaryColors, c => c.Hex == color.Hex);
        if (idx >= 0) _themeVm.PrimaryColorIndex = idx;
    }

    // 语言：RadioButton 绑定（直接操作 _langVm）
    public bool TileIsChinese { get => _langVm.IsChinese; set => _langVm.IsChinese = value; }
    public bool TileIsEnglish { get => _langVm.IsEnglish; set => _langVm.IsEnglish = value; }

    // 主题：RadioButton 绑定（直接操作 _themeVm）
    public bool TileIsSystemTheme { get => _themeVm.IsSystemTheme; set => _themeVm.IsSystemTheme = value; }
    public bool TileIsLightTheme { get => _themeVm.IsLightTheme; set => _themeVm.IsLightTheme = value; }
    public bool TileIsDarkTheme { get => _themeVm.IsDarkTheme; set => _themeVm.IsDarkTheme = value; }

    // 剪贴板订阅检测开关（复用既有 EnableClipboardDetection 设置）
    public bool ClipboardDetectionEnabled
    {
        get => _settings.EnableClipboardDetection;
        set => _settings.EnableClipboardDetection = value;
    }

    private void OnThemeVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeSettingsViewModel.PrimaryColorIndex))
            _dispatcher.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(CurrentAccentColorName));
                OnPropertyChanged(nameof(CurrentAccentColorIndex));
            });
        else if (e.PropertyName == nameof(ThemeSettingsViewModel.UseSystemAccentColor))
            _dispatcher.TryEnqueue(() => OnPropertyChanged(nameof(TileUseSystemAccentColor)));
        else if (e.PropertyName == nameof(ThemeSettingsViewModel.UsePresetColors))
            _dispatcher.TryEnqueue(() => OnPropertyChanged(nameof(TileUsePresetColors)));
    }

    // 语言切换（IsChinese/IsEnglish 翻转）后：
    // ① 磁贴 Title 是 ObservableProperty 字段，需逐个重新赋值触发 source-generated 通知；
    // ② 主题色名称缓存（PrimaryColors ??=）在首次访问时固化，需重置并通知刷新。
    private void OnLangVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LanguageSettingsViewModel.IsChinese) or nameof(LanguageSettingsViewModel.IsEnglish))
            _dispatcher.TryEnqueue(() =>
            {
                foreach (var tile in DashboardTiles) tile.RefreshTitle();
                _themeVm.RefreshPrimaryColors();
                OnPropertyChanged(nameof(CurrentAccentColorName));
            });
    }

    // 语言/主题的 RadioButton 直接绑定 _langVm/_themeVm 的公开属性，
    // 无需在 DashboardViewModel 层转发通知。

    // ── 核心状态 ──

    [ObservableProperty] private CoreState _coreState = CoreState.Stopped;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private SolidColorBrush _statusBrush = new(Color.FromArgb(255, 255, 107, 107));
    // FAB 启动按钮：颜色随核心状态联动（停止=绿引导启动 / 运行=橙红提示停止）
    [ObservableProperty] private SolidColorBrush _startButtonBrush = new(Color.FromArgb(255, 76, 175, 80));
    [ObservableProperty] private string _runtimeText = "";

    /// <summary>运行时长（短格式，与状态栏一致：m:ss 或 h:mm:ss），供运行时长磁贴使用</summary>
    public string UptimeShort
    {
        get
        {
            if (!_startTime.HasValue) return "";
            var elapsed = DateTime.Now - _startTime.Value;
            return elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
                : $"{elapsed.Minutes}:{elapsed.Seconds:D2}";
        }
    }

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
                // 核心启动后才可取真实内存占用与版本号，刷新避免停留在 "--"
                _ = RefreshMemoryAsync();
                _ = RefreshCoreVersionAsync();
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
            if (IsRunning)
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
            var mode = OutboundMode == OutboundMode.Direct ? OutboundMode.Rule : OutboundMode;
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
                OnPropertyChanged(nameof(UptimeShort));
            }

            var total = _clash.GetTotalTraffic();
            TotalUpload = Converters.ByteFormatter.Format(total.Up);
            TotalDownload = Converters.ByteFormatter.Format(total.Down);
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
    [ObservableProperty] private LipisFlag _countryFlag = 0;
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
            CountryFlag = 0;
        }
        finally
        {
            IsCheckingIp = false;
        }
    }

    // ── 内存（实时：订阅 MemoryUpdated 事件，核心运行时持续推送）──

    /// <summary>核心内存实时更新（与状态栏一致，订阅 MemoryUpdated 事件）。</summary>
    private void OnMemoryUpdated(long memory)
    {
        _dispatcher.TryEnqueue(() =>
        {
            CoreMemory = Converters.ByteFormatter.Format(memory);
            // 内存事件仅在核心就绪后推送。每次都尝试取版本号（成功后会显示真实值，不再重写）。
            _ = RefreshCoreVersionAsync();
        });
    }

    [ObservableProperty] private string _coreMemory = "--";
    [ObservableProperty] private string _coreVersion = "--";

    [RelayCommand]
    private async Task RefreshMemoryAsync()
    {
        var bytes = await _clash.GetCoreMemoryAsync();
        CoreMemory = Converters.ByteFormatter.Format(bytes);
    }

    /// <summary>核心启动后刷新版本号（/version 接口需核心运行)</summary>
    private async Task RefreshCoreVersionAsync()
    {
        try
        {
            var ver = await _clash.GetVersionAsync();
            if (!string.IsNullOrEmpty(ver) && ver != "unknown")
                CoreVersion = ver;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Dashboard] GetVersionAsync failed: {ex.Message}");
        }
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
        if (e.PropertyName == nameof(AppSettings.EnableClipboardDetection))
            _dispatcher.TryEnqueue(() => OnPropertyChanged(nameof(ClipboardDetectionEnabled)));
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
        _clash.MemoryUpdated -= OnMemoryUpdated;
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        _themeVm.PropertyChanged -= OnThemeVmPropertyChanged;
        _langVm.PropertyChanged -= OnLangVmPropertyChanged;
        foreach (var tile in DashboardTiles) tile.PropertyChanged -= OnTilePropertyChanged;
    }

    private static LipisFlag CountryCodeToFlag(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 2) return 0;
        return Enum.TryParse<LipisFlag>(code, ignoreCase: true, out var flag) ? flag : 0;
    }
}
