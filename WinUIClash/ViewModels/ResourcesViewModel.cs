using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 资源页 ViewModel — GeoIP/GeoSite/ASN 等数据文件管理，支持筛选和自动更新
/// </summary>
public partial class ResourcesViewModel : ObservableObject, IDisposable
{
    private readonly IClashService _clash;
    private readonly NotificationService _notification;
    private readonly DispatcherQueue _dispatcher;
    private readonly System.Threading.Timer _autoUpdateTimer;
    private bool _initialized;

    public ResourcesViewModel(IClashService clash, NotificationService notification)
    {
        _clash = clash;
        _notification = notification;
        _dispatcher = DispatcherQueue.GetForCurrentThread()!;

        // 自动更新定时器：每 5 分钟检查一次
        _autoUpdateTimer = new System.Threading.Timer(
            async _ => await AutoUpdateCheckAsync(),
            null,
            System.Threading.Timeout.Infinite,
            System.Threading.Timeout.Infinite);
    }

    [ObservableProperty] private ObservableCollection<ExternalProvider> _providers = new();
    [ObservableProperty] private ObservableCollection<ExternalProvider> _filteredProviders = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _typeFilter = "ALL";
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _proxyProviderCount;
    [ObservableProperty] private int _ruleProviderCount;

    /// <summary>上次全量更新时间</summary>
    [ObservableProperty] private DateTime? _lastUpdateAllTime;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnTypeFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        var list = await _clash.GetExternalProvidersAsync();
        Providers = new ObservableCollection<ExternalProvider>(list);
        TotalCount = list.Count;
        ProxyProviderCount = list.Count(p => IsProxyProvider(p));
        RuleProviderCount = list.Count(p => !IsProxyProvider(p));
        ApplyFilter();
        IsLoading = false;

        // 启动自动更新定时器
        _autoUpdateTimer.Change(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    [RelayCommand]
    private async Task UpdateProviderAsync(ExternalProvider? provider)
    {
        if (provider == null) return;
        try
        {
            await _clash.UpdateExternalProviderAsync(provider.Name);
            provider.UpdateAt = DateTime.Now;
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("ErrorUpdateTitle.Text"),
                $"{provider.Name}: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task UpdateAllAsync()
    {
        foreach (var p in Providers)
        {
            try
            {
                await _clash.UpdateExternalProviderAsync(p.Name);
                p.UpdateAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                _notification.Warning(
                    LocalizationHelper.GetString("ErrorUpdateTitle.Text"),
                    $"{p.Name}: {ex.Message}");
            }
        }
        LastUpdateAllTime = DateTime.Now;
    }

    /// <summary>自动更新：更新超过 24 小时未更新的 provider</summary>
    private async Task AutoUpdateCheckAsync()
    {
        var threshold = DateTime.Now.AddHours(-24);
        foreach (var p in Providers)
        {
            if (p.UpdateAt < threshold)
            {
                try
                {
                    await _clash.UpdateExternalProviderAsync(p.Name);
                    _dispatcher.TryEnqueue(() => p.UpdateAt = DateTime.Now);
                }
                catch { /* 自动更新失败静默 */ }
            }
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<ExternalProvider> query = Providers;

        // 按类型筛选
        if (TypeFilter == "Proxy")
        {
            query = query.Where(p => IsProxyProvider(p));
        }
        else if (TypeFilter == "Rule")
        {
            query = query.Where(p => !IsProxyProvider(p));
        }

        // 按关键词搜索
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var keyword = SearchText.Trim();
            query = query.Where(p =>
                p.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                p.Type.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                p.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        FilteredProviders = new ObservableCollection<ExternalProvider>(query);
    }

    /// <summary>根据 Type 字段判断是否为代理提供者</summary>
    public static bool IsProxyProvider(ExternalProvider p) =>
        p.Type.Contains("Proxy", StringComparison.OrdinalIgnoreCase) ||
        p.VehicleType.Contains("Proxy", StringComparison.OrdinalIgnoreCase);

    /// <summary>获取提供者类型标签</summary>
    public static string GetProviderTypeLabel(ExternalProvider p) =>
        IsProxyProvider(p)
            ? LocalizationHelper.GetString("ProviderTypeProxy.Text")
            : LocalizationHelper.GetString("ProviderTypeRule.Text");

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await LoadAsync();
    }

    public void Dispose()
    {
        _autoUpdateTimer.Dispose();
    }
}
