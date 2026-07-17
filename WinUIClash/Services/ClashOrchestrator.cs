using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using System.Net;
using System.Net.Http;
using System.ComponentModel;
using System.Net.Sockets;
using System.Net.WebSockets;
using WinUIClash.Helpers;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// 协调 CoreProcessService（进程生命周期）与 HttpClashService（REST/WebSocket 客户端）。
/// 始终使用真实核心；核心未运行时状态为 Stopped，数据调用将抛出并由各 ViewModel 处理为空状态。
/// 不提供任何 mock/假数据回退。
/// </summary>
/// <summary>启动端口冲突信息：被占用的端口列表。</summary>
public sealed class PortConflictInfo(int[] Ports)
{
    public int[] Ports { get; } = Ports;
    /// <summary>占用这些端口的进程 PID 列表（可能含重复，调用方去重）。</summary>
    public int[] Pids { get; set; } = [];
}

/// <summary>端口冲突解决策略。</summary>
public enum ConflictResolution
{
    /// <summary>忽略冲突，继续启动（关闭弹窗/用户选择不做处理）。</summary>
    Proceed,
    /// <summary>结束占用进程后继续启动。</summary>
    KillProcess,
}

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

    // 当前运行的 mihomo 是否由 Helper Service（SYSTEM）拉起。
    // 由 SYSTEM 拉起时才能创建 TUN 虚拟网卡；该标志决定停止方式与“首次开 TUN 是否需重建核心”。
    private bool _launchedViaHelper;

    // ── 网络变化检测 ──
    private bool _wasRunningBeforeNetworkLoss;
    private bool _networkLost;
    private readonly DebounceHelper _networkDebounce;

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

        // 网络变化防抖：静默期（3s）内的多次变化只触发一次实际处理
        _networkDebounce = new DebounceHelper(_ => OnNetworkDebouncedAsync(), TimeSpan.FromSeconds(3));

        try { _uiDispatcher = DispatcherQueue.GetForCurrentThread(); }
        catch { _uiDispatcher = null; }

        // 转发真实核心的事件
        _httpClashService.TrafficUpdated += OnTrafficUpdated;
        _httpClashService.LogReceived += OnLogReceived;
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

    /// <summary>
    /// 判断是否为「预期的」核心运行期异常：环境/操作失败（核心缺失、配置写入被拒、
    /// 网络不可达、API 超时、进程启动失败等），应被优雅降级为「启动/停止/TUN 失败」状态并提示用户。
    /// 真正的程序错误（NullReferenceException / ArgumentException 等）不在此列，
    /// 按 fail-fast 原则向上冒泡，避免被静默吞掉而掩盖 bug。
    /// </summary>
    private static bool IsExpectedCoreException(Exception ex) => ex is
        TimeoutException
        or HttpRequestException
        or OperationCanceledException
        or WebSocketException
        or IOException
        or UnauthorizedAccessException
        or FileNotFoundException
        or DirectoryNotFoundException
        or InvalidOperationException
        or Win32Exception
        or SocketException;

    // ── 生命周期 ──

    /// <summary>实现 IClashService：无参启动（静默路径）。</summary>
    public Task StartAsync() => StartAsync(null);

    /// <summary>
    /// 启动核心。preKillPids 非空时，先结束占用端口的进程再启动（供用户从端口冲突对话框选择“结束进程”）。
    /// 静默路径（崩溃恢复、网络恢复、TUN 切换、手动启动）传 null，走正常清理+启动。
    /// </summary>
    public async Task StartAsync(int[]? preKillPids)
    {
        if (_coreState == CoreState.Running || _coreState == CoreState.Starting) return;

        // 用户选择“结束进程”：在清理残留之前结束占用端口的外部进程（如手动启动的 mihomo），
        // 避免后续清理误杀后我们自己的启动仍因端口被占而失败。
            if (preKillPids != null && preKillPids.Length > 0)
            {
                _logger.LogInformation("用户选择结束占用进程：将终止 {Count} 个占用端口的进程后再启动", preKillPids.Length);
                KillProcesses(preKillPids);
            }

        // 清理可能残留的核心（上一轮异常退出可能让 SYSTEM/用户态 mihomo 仍占用端口）
        try { await _helperServiceManager.StopCoreViaHelperAsync(); } catch { }
        try { await _processService.StopAsync(); } catch { }

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

            // 3. 启动核心进程
            // 对齐 FlClash：优先经 Helper(SYSTEM) 服务拉起 mihomo，使其天生拥有 SYSTEM 权限（TUN 随时可用）。
            // 关键稳健性保证：无论 SYSTEM 路径是否成功，核心一定在严格预算内被拉起——
            // SYSTEM 路径未在预算内就绪时，回退“当前用户直接启动”，核心常驻、绝不卡死/绝不只报失败。
            // 仅当 Helper Service 完全不可用且直接启动也失败时，才报“启动失败”。

            bool launchedViaHelper = false;
            var serviceRegistered = await _helperServiceManager.RegisterServiceAsync();
            if (serviceRegistered && !string.IsNullOrWhiteSpace(_processService.BinaryPath))
            {
                var coreArgs = $"-d \"{Path.GetDirectoryName(configPath)}\" -f \"{configPath}\"";
                if (await _helperServiceManager.StartCoreViaHelperAsync(_processService.BinaryPath, coreArgs))
                {
                    // Helper 已接受启动请求。等待核心 REST API 就绪（SYSTEM 路径预算 4s）。
                    // 若核心未就绪（如 mihomo 在 SYSTEM 下因端口被残留进程占用 / 配置权限而启动失败），
                    // 则回退直接启动，确保核心一定拉起。
                    if (await WaitForApiAsync(_settings.ApiPort, TimeSpan.FromSeconds(4)))
                        launchedViaHelper = true;
                    else
                        _logger.LogWarning("经 Helper 启动的核心 API 未在预算内就绪，将回退直接启动");
                }
            }

            if (!launchedViaHelper)
            {
                if (serviceRegistered)
                {
                    // SYSTEM 路径失败：先停掉 Helper 可能拉起的异常核心，释放端口，避免直接启动冲突
                    try { await _helperServiceManager.StopCoreViaHelperAsync(); } catch { }
                }
                _logger.LogWarning("回退到直接启动核心（TUN 可能不可用，但核心常驻）");
                var directResult = await _processService.StartAsync();
                if (!directResult.Success)
                    throw new InvalidOperationException(directResult.ErrorMessage ?? "直接启动核心失败");
                launchedViaHelper = false;
            }

            _launchedViaHelper = launchedViaHelper;
            _logger.LogInformation("mihomo 核心进程已启动（viaHelper={ViaHelper}）", _launchedViaHelper);

            // 4. 等待 REST API 就绪（最多 5 秒；有内置 Geo 数据时 mihomo 在 1-2 秒内就绪）
            int apiPort = _settings.ApiPort;
            bool apiReady = await WaitForApiAsync(apiPort, TimeSpan.FromSeconds(5));
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

            // 注意：config.yaml 中的 tun 块恒为 enable:false（核心启动不带虚拟网卡）。
            // 虚拟网卡的创建/卸载由“开始/停止按钮”在代理连接生命周期内通过 PATCH /configs 控制。

            _logger.LogInformation("ClashOrchestrator: 已连接真实核心");
        }
        catch (Exception ex) when (IsExpectedCoreException(ex))
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

            // 经 Helper(SYSTEM) 拉起的核心，由 Helper API 停止（同时清理 TUN/wintun 虚拟网卡）；
            // 否则直接停止本地进程。
            // ⚠️ 关键退出契约：此处只 Kill 核心进程，绝不卸载/停止 WinUIClashHelperService 本身——
            // 服务保持常驻（开机自启、后台待命），低权限 UI 退出时既无权也无需销毁 SYSTEM 服务。
            if (_launchedViaHelper)
            {
                await _helperServiceManager.StopCoreViaHelperAsync();
            }
            else
            {
                await _processService.StopAsync();
            }

            // 兜底：无论 Helper 是否可达，强制确保本 App 对应的 mihomo 核心进程被结束，
            // 释放本地端口（7890/9090）并卸载 TUN 虚拟网卡，避免用户退出 UI 后电脑断网
            // 或下次启动因“Address already in use”而崩溃。服务常驻规则不受影响。
            try { await _processService.KillByBinaryPathAsync(); } catch { }

            SetCoreState(CoreState.Stopped);

            _logger.LogInformation("ClashOrchestrator: 核心已停止");
        }
        catch (Exception ex) when (IsExpectedCoreException(ex))
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

    /// <summary>
    /// 把出站模式 PATCH 到核心。这是纯副作用：<b>绝不回写 _settings.OutboundMode</b>——
    /// 单一来源（用户设置）只由 UI（仪表盘/托盘）写入，从根上切断“核心 → settings → UI”回写循环。
    /// </summary>
    public async Task SetOutboundModeAsync(OutboundMode mode)
    {
        // 记录代理激活状态：直连 = 未连接，rule/global = 已连接
        _proxyActive = mode != OutboundMode.Direct;
        await SafeRunAsync(() => _httpClashService.SetOutboundModeAsync(mode));
    }

    // ── TUN 模式 ──

    public Task<bool> GetTunEnabledAsync() =>
        SafeFetchAsync(() => _httpClashService.GetTunEnabledAsync(), false);

    /// <summary>
    /// 设置 TUN 开关。核心常驻优先，且全程不弹 UAC：
    /// - 核心未运行：由调用方（连接流程）保证核心已拉起后再调用本方法以创建网卡，故此处直接返回 true（无操作）。
    /// - 核心已运行且由 SYSTEM 服务拉起（_launchedViaHelper==true）：仅 PATCH /configs 完整 tun 配置（不重启、不弹 UAC）。
    /// - 核心已运行但为用户态进程（异常降级，仅当首次启动未成功提权时才会发生）：创建虚拟网卡需要 SYSTEM 权限，
    ///   **此处不弹 UAC 重新注册**，直接返回 false 交由 UI 提示“请重启软件并在启动时允许 UAC 提权”。
    ///   严格保证“UAC 只在软件首次启动弹一次”，TUN 切换绝不触发第二次 UAC。
    /// SYSTEM 态在软件首次启动时的一次 UAC 中确立（注册/替换 Helper Service 后恒由 SYSTEM 拉起核心）。
    /// </summary>
    public async Task<bool> SetTunEnabledAsync(bool enabled, string? stack = null)
    {
        if (_coreState != CoreState.Running)
            return true; // 未运行：由 config.yaml 接管，下次启动生效

        // SYSTEM 态（_launchedViaHelper==true）下仅 PATCH /configs：不重启、不弹 UAC。
        // SYSTEM 态已在“软件首次启动那一次 UAC”中确立，之后全程无感。
        // 若核心处于用户态降级（首次启动未成功提权），创建虚拟网卡需要 SYSTEM 权限，
        // 此处【不再弹 UAC 重新注册】，直接返回 false，由 UI 提示“请重启软件并在启动时允许 UAC 提权”——
        // 这是为了避免 TUN 切换触发第二次 UAC，严格满足“仅首次启动弹一次”的诉求。
        if (enabled && !_launchedViaHelper)
        {
            _logger.LogWarning("TUN 需要 SYSTEM 权限，但当前核心为用户态（首次启动未成功提权），不弹 UAC，交由 UI 提示");
            return false;
        }

        var tunStack = string.IsNullOrWhiteSpace(stack) ? _settings.TunStack : stack;
        try
        {
            if (enabled)
                await _httpClashService.SetTunEnabledAsync(true, tunStack);
            else
                await _httpClashService.SetTunEnabledAsync(false, tunStack);
            return true;
        }
        catch (Exception ex) when (IsExpectedCoreException(ex))
        {
            _logger.LogWarning(ex, "PATCH TUN 配置失败");
            return false;
        }
    }

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
            // 快速 TCP 预检：端口未监听时立即跳过，避免 HttpClient 超时刷出大量 TaskCanceledException
            if (await IsTcpPortOpenAsync("127.0.0.1", port, 400))
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
            }

            await Task.Delay(300);
        }

        return false;
    }

    /// <summary>在 timeoutMs 内探测 TCP 端口是否可连接。端口关闭/拒绝时快速返回 false，
    /// 绝不抛出 TaskCanceledException（与 HelperServiceManager 中同名方法行为一致）。</summary>
    private static async Task<bool> IsTcpPortOpenAsync(string host, int port, int timeoutMs)
    {
        try
        {
            using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            using var cts = new CancellationTokenSource(timeoutMs);
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), cts.Token);
            return socket.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 预检本应用依赖的端口（API / mixed / socks / http）是否已被其他进程占用。
    /// 在清理残留之前调用，被占用的端口判定为外部冲突。
    /// 同时查出占用这些端口的进程 PID，供"结束进程"使用。
    /// </summary>
    public async Task<PortConflictInfo?> DetectPortConflictAsync()
    {
        var ports = new HashSet<int> { _settings.ApiPort, _settings.MixedPort, _settings.SocksPort, _settings.HttpPort };
        var occupied = new List<int>(ports.Count);
        foreach (var p in ports)
        {
            if (p > 0 && await IsTcpPortOpenAsync("127.0.0.1", p, 400))
                occupied.Add(p);
        }
        if (occupied.Count == 0) return null;

        var info = new PortConflictInfo(occupied.ToArray());
        info.Pids = FindPidsOnPorts(occupied);
        return info;
    }

    /// <summary>查询占用指定端口的进程 PID 列表。</summary>
    private static int[] FindPidsOnPorts(IEnumerable<int> ports)
    {
        try
        {
            var portSet = new HashSet<string>(ports.Select(p => p.ToString()));
            var pids = new HashSet<int>();

            // 使用 netstat -ano 获取本地监听端口的 PID
            using var proc = new System.Diagnostics.Process();
            proc.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            // 解析行格式: TCP    127.0.0.1:7890    0.0.0.0:0    LISTENING    12345
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                // LISTENING 行至少 5 段: proto, local-addr, foreign-addr, state, pid
                if (parts.Length >= 5 && parts[3] == "LISTENING")
                {
                    var localAddr = parts[1]; // e.g. "127.0.0.1:7890"
                    var colonIdx = localAddr.LastIndexOf(':');
                    if (colonIdx >= 0 && portSet.Contains(localAddr[(colonIdx + 1)..]))
                    {
                        if (int.TryParse(parts[4], out var pid) && pid > 0)
                            pids.Add(pid);
                    }
                }
            }
            return pids.ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>结束指定 PID 的进程。</summary>
    private static void KillProcesses(int[] pids)
    {
        foreach (var pid in pids)
        {
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById(pid);
                proc.Kill(entireProcessTree: true);
            }
            catch { /* 进程可能已退出 */ }
        }
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
    /// 网络变化事件处理：仅触发防抖，实际逻辑在静默期（3s）末尾由
    /// <see cref="OnNetworkDebouncedAsync"/> 执行一次，避免瞬时变化频繁触发。
    /// </summary>
    private void OnNetworkStatusChanged(object? sender)
    {
        _networkDebounce.Pulse();
    }

    /// <summary>
    /// 网络变化防抖后的实际处理：断网时记录核心状态；恢复后若此前在运行，则自动重启核心。
    /// 由 <see cref="DebounceHelper"/> 在静默期末尾触发一次，无需手动判断取消。
    /// </summary>
    private async Task OnNetworkDebouncedAsync()
    {
        try
        {
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

                    await Task.Delay(TimeSpan.FromSeconds(2));

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
