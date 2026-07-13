using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using System.Net.Http;
using System.Net.Sockets;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// 协调 CoreProcessService（进程生命周期）与 HttpClashService（REST/WebSocket 客户端）。
/// 始终使用真实核心；核心未运行时状态为 Stopped，数据调用将抛出并由各 ViewModel 处理为空状态。
/// 不提供任何 mock/假数据回退。
/// </summary>
public class ClashOrchestrator : IClashService
{
    private readonly CoreProcessService _processService;
    private readonly HttpClashService _httpClashService;
    private readonly ConfigBuildService _configBuild;
    private readonly CoreDownloadService _coreDownload;
    private readonly ProfileStorageService _profileStorage;
    private readonly NotificationService _notificationService;
    private readonly HelperServiceManager _helperServiceManager;
    private readonly AppSettings _settings;
    private readonly ILogger<ClashOrchestrator> _logger;

    // UI 线程调度器：核心事件（CoreStateChanged/TrafficUpdated/...）大多在后台线程
    // （进程退出线程、WebSocket 接收线程、Task.Run）上抛出，订阅者多为 UI 绑定，
    // 必须派发回 UI 线程，否则会抛 RPC_E_WRONG_THREAD / 跨线程修改集合异常。
    private readonly DispatcherQueue? _uiDispatcher;

    private CoreState _coreState = CoreState.Stopped;
    private bool _intentionalStop;
    private int _restartAttempts;
    private const int MaxRestartAttempts = 3;

    // 代理是否已激活（对应 UI 的 IsRunning）。核心常驻，此标志与进程存活解耦。
    private bool _proxyActive;

    // ── 网络变化检测 ──
    private bool _wasRunningBeforeNetworkLoss;
    private bool _networkLost;
    private CancellationTokenSource? _networkDebounceCts;

    // ── 事件 ──
    public event Action<Traffic>? TrafficUpdated;
    public event Action<CoreState>? CoreStateChanged;
    public event Action<LogEntry>? LogReceived;
    public event Action<OutboundMode>? OutboundModeChanged;
    public event Action<long>? MemoryUpdated;

    public ClashOrchestrator(
        CoreProcessService processService,
        HttpClashService httpClashService,
        ConfigBuildService configBuild,
        CoreDownloadService coreDownload,
        ProfileStorageService profileStorage,
        NotificationService notificationService,
        HelperServiceManager helperServiceManager,
        AppSettings settings,
        ILogger<ClashOrchestrator> logger)
    {
        _processService = processService;
        _httpClashService = httpClashService;
        _configBuild = configBuild;
        _coreDownload = coreDownload;
        _profileStorage = profileStorage;
        _notificationService = notificationService;
        _helperServiceManager = helperServiceManager;
        _settings = settings;
        _logger = logger;

        try { _uiDispatcher = DispatcherQueue.GetForCurrentThread(); }
        catch { _uiDispatcher = null; }

        // 转发真实核心的事件
        _httpClashService.TrafficUpdated += OnTrafficUpdated;
        _httpClashService.LogReceived += OnLogReceived;
        _httpClashService.OutboundModeChanged += OnOutboundModeChanged;
        _httpClashService.MemoryUpdated += OnMemoryUpdated;

        // 监听核心进程意外退出
        _processService.ProcessStateChanged += OnProcessStateChanged;

        try
        {
            Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "无法订阅网络状态变化");
        }
    }

    public CoreState CoreState => _coreState;

    private void SetCoreState(CoreState state)
    {
        if (_coreState == state) return;
        _coreState = state;
        RaiseOnUiThread(() => CoreStateChanged?.Invoke(state));
    }

    /// <summary>将事件派发回 UI 线程，避免后台线程（进程退出/WS/Task.Run）直接触发 UI 绑定。</summary>
    private void RaiseOnUiThread(Action action)
    {
        if (_uiDispatcher == null) action();
        else _uiDispatcher.TryEnqueue(() => action());
    }

    /// <summary>
    /// 核心未运行时，数据获取直接返回空结果/默认值，避免向未监听的 REST 端口发起请求而导致崩溃。
    /// 同时捕获核心短暂不可达（如启动中、重启、崩溃恢复窗口）时的连接异常。
    /// </summary>
    private async Task<T> SafeFetchAsync<T>(Func<Task<T>> fetch, T fallback)
    {
        if (_coreState != CoreState.Running)
            return fallback;
        try
        {
            return await fetch();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "核心请求失败（核心可能未运行）");
            return fallback;
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "核心连接失败（核心可能未运行）");
            return fallback;
        }
        catch (TaskCanceledException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// 命令型操作（写/控制）在核心未运行或短暂不可达时静默忽略，避免崩溃。
    /// </summary>
    private async Task SafeRunAsync(Func<Task> action)
    {
        if (_coreState != CoreState.Running)
            return;
        try
        {
            await action();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "核心控制请求失败（核心可能未运行）");
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "核心连接失败（核心可能未运行）");
        }
        catch (TaskCanceledException) { }
    }

    // ── 生命周期 ──

    public async Task StartAsync()
    {
        if (_coreState == CoreState.Running || _coreState == CoreState.Starting) return;

        _intentionalStop = false;
        _restartAttempts = 0;
        SetCoreState(CoreState.Starting);

        try
        {
            // 1. 确保核心二进制存在（缺失则运行时下载）
            if (string.IsNullOrWhiteSpace(_settings.CoreBinaryPath) && _processService.BinaryPath == null)
            {
                _notificationService.Info(
                    LocalizationHelper.GetString("CoreDownloadingTitle.Text"),
                    LocalizationHelper.GetString("CoreDownloadingMsg.Text"));

                var downloaded = await _coreDownload.DownloadAsync();
                if (downloaded != null)
                {
                    _processService.SetBinaryPath(downloaded);
                    _notificationService.Success(
                        LocalizationHelper.GetString("CoreDownloadedTitle.Text"),
                        LocalizationHelper.GetString("CoreDownloadedMsg.Text"));
                }
            }
            else if (!string.IsNullOrWhiteSpace(_settings.CoreBinaryPath))
            {
                _processService.SetBinaryPath(_settings.CoreBinaryPath);
            }

            if (_processService.BinaryPath == null)
            {
                _logger.LogWarning("未找到 mihomo 核心二进制");
                _notificationService.Error(
                    LocalizationHelper.GetString("ErrorCoreBinaryNotFound.Text"),
                    LocalizationHelper.GetString("ErrorCoreBinaryNotFoundMsg.Text"));
                SetCoreState(CoreState.Stopped);
                return;
            }

            // 2. 生成运行时 config.yaml（端口/secret/external-controller 与应用一致）
            var configPath = await _configBuild.BuildConfigAsync();
            _processService.SetConfigPath(configPath);

            // 2.5 将内置 Geo 数据文件复制到核心 -d 目录（首次启动或 MMDB 无效时 mihomo 会尝试
            // 下载，但下载期间不监听端口导致超时。内置 Geo 数据可让 mihomo 在 1-2 秒内就绪）。
            CopyBundledGeoData(configPath);

            // 3. 启动核心进程（TUN 模式需 Helper Service 提权运行）
            if (_settings.TunMode)
            {
                // TUN 模式：注册 Helper Service（UAC 提权）并通过 API 启动 mihomo
                var serviceRegistered = await _helperServiceManager.RegisterServiceAsync();
                if (serviceRegistered)
                {
                    var coreArgs = $"-d \"{Path.GetDirectoryName(configPath)}\" -f \"{configPath}\"";
                    var coreStarted = await _helperServiceManager.StartCoreViaHelperAsync(
                        _processService.BinaryPath ?? "", coreArgs);
                    if (!coreStarted)
                    {
                        _logger.LogWarning("通过 Helper Service 启动核心失败，回退到直接启动");
                        var directResult = await _processService.StartAsync();
                        if (!directResult.Success)
                            throw new InvalidOperationException(directResult.ErrorMessage ?? "直接启动核心失败");
                    }
                }
                else
                {
                    _logger.LogWarning("Helper Service 注册失败，回退到直接启动（TUN 可能无法创建虚拟网卡）");
                    var directResult = await _processService.StartAsync();
                    if (!directResult.Success)
                        throw new InvalidOperationException(directResult.ErrorMessage ?? "直接启动核心失败");
                }
            }
            else
            {
                // 非 TUN 模式：直接启动 mihomo 进程（无需提权）
                var directResult = await _processService.StartAsync();
                if (!directResult.Success)
                    throw new InvalidOperationException(directResult.ErrorMessage ?? "直接启动核心失败");
            }
            _logger.LogInformation("mihomo 核心进程已启动");

            // 4. 等待 REST API 就绪（最多 8 秒）
            int apiPort = _settings.ApiPort;
            bool apiReady = await WaitForApiAsync(apiPort, TimeSpan.FromSeconds(8));
            if (!apiReady)
                throw new TimeoutException(
                    $"mihomo REST API 在端口 {apiPort} 上 {LocalizationHelper.GetString("CoreApiTimeout.Text")}");

            // 5. 配置 HTTP 客户端端点并连接（含 WebSocket）
            _httpClashService.SetApiEndpoint("127.0.0.1", apiPort, _settings.ApiSecret);
            await _httpClashService.StartAsync();
            _ = _httpClashService.StartTrafficStreamAsync();
            _ = _httpClashService.StartMemoryStreamAsync();

            // 6. 启动成功
            SetCoreState(CoreState.Running);

            // 核心常驻：若本次启动是重启（如 TUN 切换），且此前处于“已连接”状态，
            // 则恢复代理模式，避免重启后回落到直连。
            if (_proxyActive)
                await SafeRunAsync(() => _httpClashService.SetOutboundModeAsync(GetOutboundMode()));

            // TUN 配置已在 config.yaml 中注入（ConfigBuildService.InjectControllerSettings），
            // mihomo 启动时即知 TUN 参数，不再需要运行时 PATCH（对齐 FlClash 写入 config 的方式）

            _logger.LogInformation("ClashOrchestrator: 已连接真实核心");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动 mihomo 核心失败");
            try { await _processService.StopAsync(); } catch { }
            SetCoreState(CoreState.Stopped);
            _notificationService.Error(
                LocalizationHelper.GetString("ErrorCoreStartFailed.Text"),
                string.Format(LocalizationHelper.GetString("ErrorCoreStartFailedMsg.Text"), ex.Message));
        }
    }

    public async Task ShutdownAsync()
    {
        try
        {
            _intentionalStop = true;

            // 先记录是否在运行，再改状态
            var wasRunning = _coreState == CoreState.Running;
            SetCoreState(CoreState.Stopping);

            if (wasRunning)
                await _httpClashService.ShutdownAsync();

            // TUN 模式下通过 Helper Service API 停止核心
            if (_settings.TunMode)
            {
                await _helperServiceManager.StopCoreViaHelperAsync();
            }
            else
            {
                await _processService.StopAsync();
            }

            SetCoreState(CoreState.Stopped);

            _logger.LogInformation("ClashOrchestrator: 核心已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止核心时出错");
            SetCoreState(CoreState.Stopped);
            _notificationService.Error(
                LocalizationHelper.GetString("ErrorCoreStopFailed.Text"),
                string.Format(LocalizationHelper.GetString("ErrorCoreStopFailedMsg.Text"), ex.Message));
        }
    }

    /// <summary>
    /// 重启常驻核心进程（用于 TUN 模式切换、托盘“重启核心”等需要重建进程的场景）。
    /// 重启后会自动恢复此前的代理激活状态（_proxyActive）。
    /// </summary>
    public async Task RestartAsync()
    {
        await ShutdownAsync();
        await Task.Delay(500);
        await StartAsync();
    }

    public Task<string> GetVersionAsync() =>
        SafeFetchAsync(() => _httpClashService.GetVersionAsync(), string.Empty);

    // ── 流量 ──

    public Traffic GetCurrentTraffic() => _httpClashService.GetCurrentTraffic();
    public Traffic GetTotalTraffic() => _httpClashService.GetTotalTraffic();
    public Task ResetTrafficAsync() => SafeRunAsync(_httpClashService.ResetTrafficAsync);
    public Task StartTrafficStreamAsync() => _httpClashService.StartTrafficStreamAsync();

    // ── 出站模式 ──

    public OutboundMode GetOutboundMode() => _settings.OutboundMode?.ToLowerInvariant() switch
    {
        "global" => OutboundMode.Global,
        "direct" => OutboundMode.Direct,
        _ => OutboundMode.Rule,
    };

    public async Task SetOutboundModeAsync(OutboundMode mode)
    {
        _settings.OutboundMode = mode switch
        {
            OutboundMode.Global => "global",
            OutboundMode.Direct => "direct",
            _ => "rule",
        };
        // 记录代理激活状态：直连 = 未连接，rule/global = 已连接
        _proxyActive = mode != OutboundMode.Direct;
        await SafeRunAsync(() => _httpClashService.SetOutboundModeAsync(mode));
    }

    // ── TUN 模式 ──

    public Task<bool> GetTunEnabledAsync() =>
        SafeFetchAsync(() => _httpClashService.GetTunEnabledAsync(), false);
    public Task SetTunEnabledAsync(bool enabled) => SafeRunAsync(() => _httpClashService.SetTunEnabledAsync(enabled));
    public Task SetTunStackAsync(string stack) => SafeRunAsync(() => _httpClashService.SetTunStackAsync(stack));

    // ── 代理 ──

    public async Task<IReadOnlyList<ProxyGroup>> GetProxyGroupsAsync()
    {
        var groups = await SafeFetchAsync(() => _httpClashService.GetProxyGroupsAsync(), Array.Empty<ProxyGroup>());
        if (groups.Count == 0) return groups;

        // mihomo REST /proxies 返回 Go map（JSON 序列化按键 Unicode 排序），
        // 不保留 config.yaml 中 proxy-groups 的原始顺序。
        // 从 config 文件读取原始顺序并对齐（1:1 对齐 FlClash gRPC/FFI 的 all 列表行为）。
        var order = _configBuild.GetProxyGroupOrder();
        if (order.Count == 0) return groups;

        return groups
            .OrderBy(g =>
            {
                var idx = order.IndexOf(g.Name);
                return idx >= 0 ? idx : int.MaxValue;
            })
            .ToList();
    }
    public Task ChangeProxyAsync(string groupName, string proxyName) =>
        SafeRunAsync(() => _httpClashService.ChangeProxyAsync(groupName, proxyName));
    public Task<int> TestDelayAsync(string proxyName, string? testUrl = null) =>
        SafeFetchAsync(() => _httpClashService.TestDelayAsync(proxyName, testUrl), -1);
    public Task<Dictionary<string, int>> TestGroupDelayAsync(string groupName, string? testUrl = null) =>
        SafeFetchAsync(() => _httpClashService.TestGroupDelayAsync(groupName, testUrl), new Dictionary<string, int>());

    // ── 配置 ──

    public async Task<IReadOnlyList<Profile>> GetProfilesAsync()
    {
        try
        {
            var list = await _profileStorage.LoadProfileListAsync();
            return list;
        }
        catch
        {
            return Array.Empty<Profile>();
        }
    }

    public Task AddProfileAsync(Profile profile) => Task.CompletedTask;
    public Task UpdateProfileAsync(Profile profile) => Task.CompletedTask;
    public Task DeleteProfileAsync(string profileId) => Task.CompletedTask;

    public async Task SwitchProfileAsync(string profileId, string configPath = "")
    {
        // 重新生成 config.yaml（已包含新活动配置），运行中对核心热重载
        var builtPath = await _configBuild.BuildConfigAsync();
        if (_coreState == CoreState.Running && File.Exists(builtPath))
        {
            await _httpClashService.SwitchProfileAsync(profileId, builtPath);
            // 热重载会重置 config 中的 mode；若当前已连接，恢复代理模式
            if (_proxyActive)
                await _httpClashService.SetOutboundModeAsync(GetOutboundMode());
        }
    }

    public async Task SyncProfileAsync(string profileId, string? url = null, string configPath = "")
    {
        // VM 已负责下载订阅内容；此处重新生成 config.yaml 并在运行时热重载
        var builtPath = await _configBuild.BuildConfigAsync();
        if (_coreState == CoreState.Running && File.Exists(builtPath))
        {
            await _httpClashService.SwitchProfileAsync(profileId, builtPath);
            // 热重载会重置 config 中的 mode；若当前已连接，恢复代理模式
            if (_proxyActive)
                await _httpClashService.SetOutboundModeAsync(GetOutboundMode());
        }
    }

    // ── 连接 ──

    public Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync() =>
        SafeFetchAsync(() => _httpClashService.GetConnectionsAsync(), Array.Empty<ConnectionInfo>());
    public Task CloseConnectionAsync(string connectionId) =>
        SafeRunAsync(() => _httpClashService.CloseConnectionAsync(connectionId));
    public Task CloseAllConnectionsAsync() =>
        SafeRunAsync(_httpClashService.CloseAllConnectionsAsync);

    // ── 日志 ──

    public Task StartLogAsync(string level = "info") => SafeRunAsync(() => _httpClashService.StartLogAsync(level));
    public Task StopLogAsync() => SafeRunAsync(_httpClashService.StopLogAsync);

    // ── 网络检测 ──

    public Task<IpInfo> GetIpInfoAsync() =>
        SafeFetchAsync(() => _httpClashService.GetIpInfoAsync(), new IpInfo());

    // ── 外部提供者 ──

    public Task<IReadOnlyList<ExternalProvider>> GetExternalProvidersAsync() =>
        SafeFetchAsync(() => _httpClashService.GetExternalProvidersAsync(), Array.Empty<ExternalProvider>());
    public Task UpdateExternalProviderAsync(string name, string category = "proxy") =>
        SafeRunAsync(() => _httpClashService.UpdateExternalProviderAsync(name, category));
    public Task UpdateGeoDatabaseAsync(string name) =>
        SafeRunAsync(() => _httpClashService.UpdateGeoDatabaseAsync(name));
    public Task PatchCoreConfigAsync(AppSettings settings) =>
        SafeRunAsync(() => _httpClashService.PatchCoreConfigAsync(settings));
    public Task HealthCheckProviderAsync(string name, string category = "proxy") =>
        SafeRunAsync(() => _httpClashService.HealthCheckProviderAsync(name, category));

    // ── 规则 ──

    public Task<IReadOnlyList<Rule>> GetRulesAsync() =>
        SafeFetchAsync(() => _httpClashService.GetRulesAsync(), Array.Empty<Rule>());

    // ── 内存 ──

    public Task<long> GetCoreMemoryAsync() =>
        SafeFetchAsync(() => _httpClashService.GetCoreMemoryAsync(), 0L);
    public Task ForceGcAsync() => SafeRunAsync(_httpClashService.ForceGcAsync);
    public Task StartMemoryStreamAsync() => SafeRunAsync(_httpClashService.StartMemoryStreamAsync);
    public Task FlushFakeIpCacheAsync() => SafeRunAsync(_httpClashService.FlushFakeIpCacheAsync);

    // ── 私有辅助 ──

    /// <summary>
    /// 将内置 Geo 数据文件从打包的 Core 目录复制到核心 -d 目录。
    /// mihomo 发现 MMDB 无效/不存在时会删除并从网络下载，但下载期间不监听端口。
    /// 内置 Geo 数据可确保 mihomo 启动时即有有效数据，1-2 秒内就绪。
    /// 仅在目标文件缺失或比内置文件更旧时才复制，避免每次启动都 IO。
    /// </summary>
    private void CopyBundledGeoData(string configPath)
    {
        var destDir = Path.GetDirectoryName(configPath);
        if (string.IsNullOrEmpty(destDir)) return;

        var bundledDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Core");
        if (!Directory.Exists(bundledDir)) return;

        // mihomo 需要的关键 Geo 文件（小写文件名，与 mihomo 源码一致）
        var geoFiles = new[] { "geoip.metadb", "geosite.dat", "geoip.dat", "GeoIP.dat", "GeoSite.dat" };

        foreach (var fileName in geoFiles)
        {
            var srcPath = Path.Combine(bundledDir, fileName);
            if (!File.Exists(srcPath)) continue;

            var destPath = Path.Combine(destDir, fileName);

            // 仅在目标文件缺失或比内置文件更旧时复制
            if (!File.Exists(destPath))
            {
                try
                {
                    Directory.CreateDirectory(destDir);
                    File.Copy(srcPath, destPath, overwrite: false);
                    _logger.LogInformation("复制内置 Geo 文件: {File}", fileName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "复制内置 Geo 文件 {File} 失败", fileName);
                }
            }
            else
            {
                // 目标文件已存在，但可能无效（mihomo 删掉后只剩空文件/损坏文件）
                // 检查大小：如果目标比源小很多（损坏），用内置覆盖
                try
                {
                    var srcSize = new FileInfo(srcPath).Length;
                    var destSize = new FileInfo(destPath).Length;
                    if (destSize < srcSize * 0.5 || destSize < 1024)
                    {
                        File.Copy(srcPath, destPath, overwrite: true);
                        _logger.LogInformation("覆盖损坏的 Geo 文件: {File} (src={SrcSize}, dest={DestSize})",
                            fileName, srcSize, destSize);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "检查 Geo 文件 {File} 大小失败", fileName);
                }
            }
        }
    }

    private async Task<bool> WaitForApiAsync(int port, TimeSpan timeout)
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
        if (!string.IsNullOrWhiteSpace(_settings.ApiSecret))
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiSecret);

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var resp = await http.GetAsync($"http://127.0.0.1:{port}/version");
                if (resp.IsSuccessStatusCode)
                    return true;
            }
            catch
            {
                // API 尚未就绪
            }

            await Task.Delay(300);
        }

        return false;
    }

    private void OnTrafficUpdated(Traffic traffic) => RaiseOnUiThread(() => TrafficUpdated?.Invoke(traffic));
    private void OnLogReceived(LogEntry entry) => RaiseOnUiThread(() => LogReceived?.Invoke(entry));
    private void OnOutboundModeChanged(OutboundMode mode) => RaiseOnUiThread(() => OutboundModeChanged?.Invoke(mode));
    private void OnMemoryUpdated(long memory) => RaiseOnUiThread(() => MemoryUpdated?.Invoke(memory));

    /// <summary>
    /// 崩溃恢复看门狗：核心进程意外退出且启用自动重启时，尝试重启（最多 MaxRestartAttempts 次）。
    /// </summary>
    private async void OnProcessStateChanged(bool isRunning)
    {
        if (isRunning || _intentionalStop || !_settings.AutoRestart) return;
        if (_coreState != CoreState.Running) return; // 仅在我们确实在运行核心时才反应

        _restartAttempts++;
        if (_restartAttempts > MaxRestartAttempts)
        {
            _logger.LogWarning("核心崩溃恢复：已达到最大重启次数 ({Max})", MaxRestartAttempts);
            _notificationService.Error(
                LocalizationHelper.GetString("ErrorCoreStartFailed.Text"),
                LocalizationHelper.GetString("ErrorCoreCrashMaxRestarts.Text"));
            SetCoreState(CoreState.Stopped);
            return;
        }

        _logger.LogWarning("核心进程意外退出，尝试重启 ({Attempt}/{Max})", _restartAttempts, MaxRestartAttempts);
        _notificationService.Warning(
            LocalizationHelper.GetString("CoreCrashedTitle.Text"),
            string.Format(LocalizationHelper.GetString("CoreCrashedMsg.Text"), _restartAttempts, MaxRestartAttempts));

        await Task.Delay(TimeSpan.FromSeconds(2));
        SetCoreState(CoreState.Stopped);

        await StartAsync();
    }

    /// <summary>
    /// 网络变化检测：断网时记录核心状态；恢复后若此前在运行，则自动重启核心。
    /// 使用防抖避免瞬时变化频繁触发。
    /// </summary>
    private async void OnNetworkStatusChanged(object? sender)
    {
        _networkDebounceCts?.Cancel();
        _networkDebounceCts = new CancellationTokenSource();
        var token = _networkDebounceCts.Token;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token);
            if (token.IsCancellationRequested) return;

            var hasInternet = HasInternetConnectivity();

            if (!hasInternet && _coreState == CoreState.Running && !_networkLost)
            {
                _networkLost = true;
                _wasRunningBeforeNetworkLoss = true;
                _logger.LogWarning("网络断开，核心可能不可达");
                _notificationService.Warning(
                    LocalizationHelper.GetString("NetworkChanged.Text"),
                    LocalizationHelper.GetString("NetworkChangedMsg.Text"));
            }
            else if (hasInternet && _networkLost)
            {
                _networkLost = false;
                _logger.LogInformation("网络已恢复");

                if (_wasRunningBeforeNetworkLoss && _coreState != CoreState.Running)
                {
                    _wasRunningBeforeNetworkLoss = false;
                    _notificationService.Info(
                        LocalizationHelper.GetString("NetworkRestored.Text"),
                        LocalizationHelper.GetString("NetworkRestoredMsg.Text"));

                    await Task.Delay(TimeSpan.FromSeconds(2), token);
                    if (token.IsCancellationRequested) return;

                    _restartAttempts = 0;
                    await StartAsync();
                }
                else
                {
                    _wasRunningBeforeNetworkLoss = false;
                    _notificationService.Info(
                        LocalizationHelper.GetString("NetworkRestored.Text"),
                        LocalizationHelper.GetString("NetworkRestoredMsg.Text"));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "处理网络状态变化出错");
        }
    }

    private static bool HasInternetConnectivity()
    {
        try
        {
            var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (profile == null) return false;

            var level = profile.GetNetworkConnectivityLevel();
            return level == Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess
                || level == Windows.Networking.Connectivity.NetworkConnectivityLevel.ConstrainedInternetAccess;
        }
        catch
        {
            return false;
        }
    }
}
