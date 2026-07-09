using System.Net.Http;
using Microsoft.Extensions.Logging;
using WinUIClash.Models;

namespace WinUIClash.Services;

/// <summary>
/// Coordinates CoreProcessService (process lifecycle) and IClashService (API client).
/// Delegates all IClashService calls to either HttpClashService (real core) or MockClashService (fallback).
/// </summary>
public class ClashOrchestrator : IClashService
{
    private readonly CoreProcessService _processService;
    private readonly HttpClashService _httpClashService;
    private readonly MockClashService _mockClashService;
    private readonly NotificationService _notificationService;
    private readonly AppSettings _settings;
    private readonly ILogger<ClashOrchestrator> _logger;

    private IClashService _activeService;
    private bool _intentionalStop;
    private int _restartAttempts;
    private const int MaxRestartAttempts = 3;

    // ── Network change detection ──
    private bool _wasRunningBeforeNetworkLoss;
    private bool _networkLost;
    private CancellationTokenSource? _networkDebounceCts;

    // ── Events (forwarded from active service) ──
    public event Action<Traffic>? TrafficUpdated;
    public event Action<CoreState>? CoreStateChanged;
    public event Action<LogEntry>? LogReceived;
    public event Action<OutboundMode>? OutboundModeChanged;

    public ClashOrchestrator(
        CoreProcessService processService,
        HttpClashService httpClashService,
        MockClashService mockClashService,
        NotificationService notificationService,
        AppSettings settings,
        ILogger<ClashOrchestrator> logger)
    {
        _processService = processService;
        _httpClashService = httpClashService;
        _mockClashService = mockClashService;
        _notificationService = notificationService;
        _settings = settings;
        _logger = logger;

        // Start with mock service as the active backend
        _activeService = _mockClashService;
        SubscribeToEvents(_mockClashService);

        // Watch for unexpected core process exits
        _processService.ProcessStateChanged += OnProcessStateChanged;

        // Watch for network connectivity changes
        try
        {
            Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe to network status changes");
        }
    }

    /// <summary>Whether the real ClashMeta core process is running and connected.</summary>
    public bool IsRealCore => _activeService == _httpClashService;

    /// <summary>
    /// Current core state: Running when the real core is active,
    /// otherwise delegates to the mock service's state.
    /// </summary>
    public CoreState CoreState => IsRealCore ? CoreState.Running : _mockClashService.CoreState;

    // ── Lifecycle ──

    public async Task StartAsync()
    {
        try
        {
            _intentionalStop = false;
            _restartAttempts = 0;

            // 1. Start the core process
            try
            {
                // Apply custom binary path if configured
                if (!string.IsNullOrWhiteSpace(_settings.CoreBinaryPath))
                    _processService.SetBinaryPath(_settings.CoreBinaryPath);

                await _processService.StartAsync();
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "ClashMeta core binary not found");
                _notificationService.Error(
                    LocalizationHelper.GetString("ErrorCoreBinaryNotFound.Text"),
                    LocalizationHelper.GetString("ErrorCoreBinaryNotFoundMsg.Text"));
                return;
            }

            _logger.LogInformation("ClashMeta core process started");

            // 2. Wait for the REST API to become available (up to 5 seconds)
            int apiPort = _settings.HttpPort + 1900;
            bool apiReady = await WaitForApiAsync(apiPort, TimeSpan.FromSeconds(5));

            if (!apiReady)
            {
                throw new TimeoutException(
                    $"ClashMeta REST API did not respond within 5 seconds on port {apiPort}");
            }

            _logger.LogInformation("ClashMeta REST API is ready on port {Port}", apiPort);

            // 3. Configure the HTTP client endpoint and start (connects WebSockets)
            _httpClashService.SetApiEndpoint("127.0.0.1", apiPort, _settings.ApiSecret);
            await _httpClashService.StartAsync();

            // 4. Start the real-time traffic WebSocket stream
            _ = _httpClashService.StartTrafficStreamAsync();

            // 5. Switch active service to the real backend
            SwitchActiveService(_httpClashService);

            // 6. Fire state change and notify
            CoreStateChanged?.Invoke(CoreState.Running);
            _notificationService.Success(
                LocalizationHelper.GetString("CoreStartedTitle.Text"),
                LocalizationHelper.GetString("CoreStartedMsg.Text"));

            // 7. Apply saved TUN mode if enabled
            if (_settings.TunMode)
            {
                try
                {
                    await _httpClashService.SetTunEnabledAsync(true);
                    await _httpClashService.SetTunStackAsync(_settings.TunStack);
                }
                catch (Exception tunEx)
                {
                    _logger.LogWarning(tunEx, "Failed to apply TUN mode on startup");
                }
            }

            _logger.LogInformation("ClashOrchestrator: switched to HttpClashService");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ClashMeta core, falling back to mock service");

            // Clean up: stop the process if it was started
            try
            {
                await _processService.StopAsync();
            }
            catch (Exception stopEx)
            {
                _logger.LogWarning(stopEx, "Error stopping core process during fallback");
            }

            // Fall back to mock
            SwitchActiveService(_mockClashService);
            CoreStateChanged?.Invoke(_mockClashService.CoreState);

            _notificationService.Error(
                LocalizationHelper.GetString("ErrorCoreStartFailed.Text"),
                string.Format(LocalizationHelper.GetString("ErrorCoreStartFailedMsg.Text"), ex.Message));
        }
    }

    public async Task StopAsync()
    {
        try
        {
            _intentionalStop = true;

            // 1. Disconnect HttpClashService WebSockets
            if (IsRealCore)
            {
                await _httpClashService.StopAsync();
            }

            // 2. Stop the core process
            await _processService.StopAsync();

            // 3. Switch back to mock service
            SwitchActiveService(_mockClashService);

            // 4. Fire state change
            CoreStateChanged?.Invoke(_mockClashService.CoreState);

            // 5. Notify
            _notificationService.Info(
                LocalizationHelper.GetString("CoreStoppedTitle.Text"),
                LocalizationHelper.GetString("CoreStoppedMsg.Text"));

            _logger.LogInformation("ClashOrchestrator: stopped core, switched to MockClashService");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during stop");
            _notificationService.Error(
                LocalizationHelper.GetString("ErrorCoreStopFailed.Text"),
                string.Format(LocalizationHelper.GetString("ErrorCoreStopFailedMsg.Text"), ex.Message));
        }
    }

    public Task<string> GetVersionAsync() => _activeService.GetVersionAsync();

    // ── Traffic ──

    public Traffic GetCurrentTraffic() => _activeService.GetCurrentTraffic();
    public Traffic GetTotalTraffic() => _activeService.GetTotalTraffic();
    public Task ResetTrafficAsync() => _activeService.ResetTrafficAsync();
    public Task StartTrafficStreamAsync() => _activeService.StartTrafficStreamAsync();

    // ── Outbound Mode ──

    public OutboundMode GetOutboundMode() => _activeService.GetOutboundMode();
    public Task SetOutboundModeAsync(OutboundMode mode) => _activeService.SetOutboundModeAsync(mode);

    // ── TUN Mode ──

    public Task<bool> GetTunEnabledAsync() => _activeService.GetTunEnabledAsync();
    public Task SetTunEnabledAsync(bool enabled) => _activeService.SetTunEnabledAsync(enabled);
    public Task SetTunStackAsync(string stack) => _activeService.SetTunStackAsync(stack);

    // ── Proxy ──

    public Task<IReadOnlyList<ProxyGroup>> GetProxyGroupsAsync() => _activeService.GetProxyGroupsAsync();
    public Task ChangeProxyAsync(string groupName, string proxyName) => _activeService.ChangeProxyAsync(groupName, proxyName);
    public Task<int> TestDelayAsync(string proxyName, string? testUrl = null) => _activeService.TestDelayAsync(proxyName, testUrl);
    public Task<Dictionary<string, int>> TestGroupDelayAsync(string groupName, string? testUrl = null) => _activeService.TestGroupDelayAsync(groupName, testUrl);

    // ── Config ──

    public Task<IReadOnlyList<Profile>> GetProfilesAsync() => _activeService.GetProfilesAsync();
    public Task AddProfileAsync(Profile profile) => _activeService.AddProfileAsync(profile);
    public Task UpdateProfileAsync(Profile profile) => _activeService.UpdateProfileAsync(profile);
    public Task DeleteProfileAsync(string profileId) => _activeService.DeleteProfileAsync(profileId);
    public Task SwitchProfileAsync(string profileId, string configPath = "") => _activeService.SwitchProfileAsync(profileId, configPath);
    public Task SyncProfileAsync(string profileId, string? url = null, string configPath = "") => _activeService.SyncProfileAsync(profileId, url, configPath);

    // ── Connections ──

    public Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync() => _activeService.GetConnectionsAsync();
    public Task CloseConnectionAsync(string connectionId) => _activeService.CloseConnectionAsync(connectionId);
    public Task CloseAllConnectionsAsync() => _activeService.CloseAllConnectionsAsync();

    // ── Log ──

    public Task StartLogAsync(string level = "info") => _activeService.StartLogAsync(level);
    public Task StopLogAsync() => _activeService.StopLogAsync();

    // ── Network ──

    public Task<IpInfo> GetIpInfoAsync() => _activeService.GetIpInfoAsync();
    public Task<string> QueryDnsAsync(string name, string type = "A") => _activeService.QueryDnsAsync(name, type);

    // ── External Providers ──

    public Task<IReadOnlyList<ExternalProvider>> GetExternalProvidersAsync() => _activeService.GetExternalProvidersAsync();
    public Task UpdateExternalProviderAsync(string name, string category = "proxy") => _activeService.UpdateExternalProviderAsync(name, category);
    public Task UpdateGeoDatabaseAsync(string name) => _activeService.UpdateGeoDatabaseAsync(name);
    public Task PatchCoreConfigAsync(AppSettings settings) => _activeService.PatchCoreConfigAsync(settings);
    public Task HealthCheckProviderAsync(string name, string category = "proxy") => _activeService.HealthCheckProviderAsync(name, category);

    // ── Rules ──

    public Task<IReadOnlyList<Rule>> GetRulesAsync() => _activeService.GetRulesAsync();

    // ── Memory ──

    public Task<long> GetCoreMemoryAsync() => _activeService.GetCoreMemoryAsync();
    public Task ForceGcAsync() => _activeService.ForceGcAsync();
    public Task FlushFakeIpCacheAsync() => _activeService.FlushFakeIpCacheAsync();

    // ── Private helpers ──

    /// <summary>
    /// Polls the /version endpoint until it responds or the timeout expires.
    /// </summary>
    private async Task<bool> WaitForApiAsync(int port, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
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
                // API not ready yet
            }

            await Task.Delay(300);
        }

        return false;
    }

    /// <summary>
    /// Switches the active service, unsubscribing from the old one and subscribing to the new one.
    /// </summary>
    private void SwitchActiveService(IClashService newService)
    {
        UnsubscribeFromEvents(_activeService);
        _activeService = newService;
        SubscribeToEvents(newService);
    }

    private void SubscribeToEvents(IClashService service)
    {
        service.TrafficUpdated += OnTrafficUpdated;
        service.CoreStateChanged += OnCoreStateChanged;
        service.LogReceived += OnLogReceived;
        service.OutboundModeChanged += OnOutboundModeChanged;
    }

    private void UnsubscribeFromEvents(IClashService service)
    {
        service.TrafficUpdated -= OnTrafficUpdated;
        service.CoreStateChanged -= OnCoreStateChanged;
        service.LogReceived -= OnLogReceived;
        service.OutboundModeChanged -= OnOutboundModeChanged;
    }

    private void OnTrafficUpdated(Traffic traffic) => TrafficUpdated?.Invoke(traffic);
    private void OnCoreStateChanged(CoreState state) => CoreStateChanged?.Invoke(state);
    private void OnLogReceived(LogEntry entry) => LogReceived?.Invoke(entry);
    private void OnOutboundModeChanged(OutboundMode mode) => OutboundModeChanged?.Invoke(mode);

    /// <summary>
    /// Crash recovery watchdog: if the core process exits unexpectedly and auto-restart is enabled,
    /// attempt to restart it (up to MaxRestartAttempts consecutive times).
    /// </summary>
    private async void OnProcessStateChanged(bool isRunning)
    {
        if (isRunning || _intentionalStop || !_settings.AutoRestart) return;
        if (!IsRealCore) return; // Only react when we were using the real core

        _restartAttempts++;
        if (_restartAttempts > MaxRestartAttempts)
        {
            _logger.LogWarning("Core crash recovery: max restart attempts ({Max}) reached", MaxRestartAttempts);
            _notificationService.Error(
                LocalizationHelper.GetString("ErrorCoreStartFailed.Text"),
                LocalizationHelper.GetString("ErrorCoreCrashMaxRestarts.Text"));
            SwitchActiveService(_mockClashService);
            CoreStateChanged?.Invoke(CoreState.Stopped);
            return;
        }

        _logger.LogWarning("Core process exited unexpectedly, attempting restart ({Attempt}/{Max})",
            _restartAttempts, MaxRestartAttempts);

        _notificationService.Warning(
            LocalizationHelper.GetString("CoreCrashedTitle.Text"),
            string.Format(LocalizationHelper.GetString("CoreCrashedMsg.Text"), _restartAttempts, MaxRestartAttempts));

        // Wait a bit before restarting
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Switch to mock temporarily
        SwitchActiveService(_mockClashService);
        CoreStateChanged?.Invoke(CoreState.Stopped);

        // Attempt restart
        await StartAsync();
    }

    /// <summary>
    /// Network change detection: when connectivity is lost, remember core state.
    /// When connectivity is restored, auto-restart the core if it was previously running.
    /// Uses a debounce delay to avoid reacting to transient changes.
    /// </summary>
    private async void OnNetworkStatusChanged(object? sender)
    {
        // Cancel any pending debounce
        _networkDebounceCts?.Cancel();
        _networkDebounceCts = new CancellationTokenSource();
        var token = _networkDebounceCts.Token;

        try
        {
            // Debounce: wait 3 seconds before reacting
            await Task.Delay(TimeSpan.FromSeconds(3), token);
            if (token.IsCancellationRequested) return;

            var hasInternet = HasInternetConnectivity();

            if (!hasInternet && IsRealCore && !_networkLost)
            {
                // Network lost while core was running
                _networkLost = true;
                _wasRunningBeforeNetworkLoss = true;
                _logger.LogWarning("Network connectivity lost, core may become unreachable");
                _notificationService.Warning(
                    LocalizationHelper.GetString("NetworkChanged.Text"),
                    LocalizationHelper.GetString("NetworkChangedMsg.Text"));
            }
            else if (hasInternet && _networkLost)
            {
                // Network restored
                _networkLost = false;
                _logger.LogInformation("Network connectivity restored");

                if (_wasRunningBeforeNetworkLoss && !IsRealCore)
                {
                    _wasRunningBeforeNetworkLoss = false;
                    _notificationService.Info(
                        LocalizationHelper.GetString("NetworkRestored.Text"),
                        LocalizationHelper.GetString("NetworkRestoredMsg.Text"));

                    // Wait a bit for the network to stabilize, then restart
                    await Task.Delay(TimeSpan.FromSeconds(2), token);
                    if (token.IsCancellationRequested) return;

                    _restartAttempts = 0; // Reset restart counter
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
        catch (OperationCanceledException)
        {
            // Debounce cancelled, ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling network status change");
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
