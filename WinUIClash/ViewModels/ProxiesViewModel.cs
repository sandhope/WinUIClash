using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 代理页 ViewModel — 代理组展示、代理切换、延迟测试、排序
/// </summary>
public partial class ProxiesViewModel : ObservableObject
{
    private readonly IClashService _clash;
    private readonly NotificationService _notification;
    private readonly SemaphoreSlim _testSemaphore = new(5, 5);
    private bool _initialized;

    public ProxiesViewModel(IClashService clash, NotificationService notification)
    {
        _clash = clash;
        _notification = notification;
    }

    [ObservableProperty] private ObservableCollection<ProxyGroup> _groups = new();
    [ObservableProperty] private ProxyGroup? _selectedGroup;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private int _testProgress;
    [ObservableProperty] private int _testTotal;
    [ObservableProperty] private string _testSummaryText = "";
    [ObservableProperty] private bool _hasTestSummary;

    // Test URL selector
    public string[] TestUrlOptions { get; } =
    [
        "http://www.gstatic.com/generate_204",
        "https://cp.cloudflare.com/generate_204",
        "https://www.apple.com/library/test/success.html",
        "https://connectivitycheck.gstatic.com/generate_204",
    ];

    public string[] TestUrlLabels { get; } =
    [
        "Google",
        "Cloudflare",
        "Apple",
        "Google (alt)",
    ];

    [ObservableProperty] private int _selectedTestUrlIndex = 0;

    public string SelectedTestUrl =>
        SelectedTestUrlIndex >= 0 && SelectedTestUrlIndex < TestUrlOptions.Length
            ? TestUrlOptions[SelectedTestUrlIndex]
            : TestUrlOptions[0];

    public enum SortMode { Default, Name, Delay, Type }

    [ObservableProperty] private SortMode _currentSort = SortMode.Default;

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredProxies));
    partial void OnCurrentSortChanged(SortMode value) => OnPropertyChanged(nameof(FilteredProxies));
    partial void OnSelectedGroupChanged(ProxyGroup? value)
    {
        OnPropertyChanged(nameof(FilteredProxies));
        OnPropertyChanged(nameof(HasSelectedGroup));
    }

    public bool HasSelectedGroup => SelectedGroup != null;

    /// <summary>根据搜索文本和排序模式过滤代理列表</summary>
    public IEnumerable<Proxy> FilteredProxies
    {
        get
        {
            if (SelectedGroup == null) return [];

            var proxies = SelectedGroup.Proxies.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
                proxies = proxies.Where(p =>
                    p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    p.Type.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            proxies = CurrentSort switch
            {
                SortMode.Name  => proxies.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
                SortMode.Delay => proxies.OrderBy(p => p.Delay <= 0 ? int.MaxValue : p.Delay),
                SortMode.Type  => proxies.OrderBy(p => p.Type, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name),
                _              => proxies,
            };

            return proxies;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _clash.GetProxyGroupsAsync();
            Groups = new ObservableCollection<ProxyGroup>(list);
            SelectedGroup = Groups.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("ErrorLoadProxies.Text"),
                ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>切换代理组 Tab</summary>
    [RelayCommand]
    private void SelectGroup(ProxyGroup? group)
    {
        if (group != null) SelectedGroup = group;
    }

    /// <summary>选中某个代理节点</summary>
    [RelayCommand]
    private async Task SelectProxyAsync(Proxy? proxy)
    {
        if (SelectedGroup == null || proxy == null) return;
        try
        {
            await _clash.ChangeProxyAsync(SelectedGroup.Name, proxy.Name);
            SelectedGroup.Now = proxy.Name;
        }
        catch (Exception ex)
        {
            _notification.Error(
                LocalizationHelper.GetString("ErrorCloseTitle.Text"),
                ex.Message);
        }
    }

    /// <summary>测试单个代理延迟</summary>
    [RelayCommand]
    private async Task TestDelayAsync(Proxy? proxy)
    {
        if (proxy == null || proxy.Type is "Direct" or "Reject") return;
        try
        {
            proxy.IsTestingDelay = true;
            proxy.Delay = await _clash.TestDelayAsync(proxy.Name, SelectedTestUrl);
            OnPropertyChanged(nameof(FilteredProxies));
        }
        catch (Exception ex)
        {
            proxy.Delay = -1;
            _notification.Error(
                LocalizationHelper.GetString("ErrorUpdateTitle.Text"),
                $"{proxy.Name}: {ex.Message}");
        }
        finally
        {
            proxy.IsTestingDelay = false;
        }
    }

    /// <summary>对当前组所有代理并行测速（优先使用批量 API，失败回退到逐个测试）</summary>
    [RelayCommand]
    private async Task TestAllDelaysAsync()
    {
        if (SelectedGroup == null) return;
        IsTesting = true;
        HasTestSummary = false;
        TestSummaryText = "";

        var testable = SelectedGroup.Proxies
            .Where(p => p.Type is not ("Direct" or "Reject"))
            .ToList();
        TestTotal = testable.Count;
        TestProgress = 0;

        // Mark all as testing
        foreach (var p in testable) p.IsTestingDelay = true;

        try
        {
            // Try batch group delay API first
            var results = await _clash.TestGroupDelayAsync(SelectedGroup.Name, SelectedTestUrl);
            foreach (var p in testable)
            {
                p.Delay = results.TryGetValue(p.Name, out var delay) ? delay : -1;
                p.IsTestingDelay = false;
                TestProgress++;
            }
        }
        catch
        {
            // Fallback: individual testing with semaphore throttling
            TestProgress = 0;
            var tasks = testable.Select(async p =>
            {
                await _testSemaphore.WaitAsync();
                try
                {
                    p.Delay = await _clash.TestDelayAsync(p.Name, SelectedTestUrl);
                }
                catch
                {
                    p.Delay = -1;
                }
                finally
                {
                    _testSemaphore.Release();
                    TestProgress++;
                    p.IsTestingDelay = false;
                }
            });
            await Task.WhenAll(tasks);
        }

        // Compute summary stats
        var delays = testable.Where(p => p.Delay > 0).Select(p => p.Delay).ToList();
        var failed = testable.Count - delays.Count;
        if (delays.Count > 0)
        {
            var avg = (int)delays.Average();
            var min = delays.Min();
            var max = delays.Max();
            TestSummaryText = string.Format(
                LocalizationHelper.GetString("ProxyTestSummary.Text"),
                avg, min, max, delays.Count, failed);
        }
        else
        {
            TestSummaryText = string.Format(
                LocalizationHelper.GetString("ProxyTestSummaryFailed.Text"),
                failed);
        }
        HasTestSummary = true;

        IsTesting = false;
        OnPropertyChanged(nameof(FilteredProxies));
    }

    /// <summary>循环切换排序模式</summary>
    [RelayCommand]
    private void CycleSortMode()
    {
        CurrentSort = CurrentSort switch
        {
            SortMode.Default => SortMode.Name,
            SortMode.Name    => SortMode.Delay,
            SortMode.Delay   => SortMode.Type,
            _                => SortMode.Default,
        };
    }

    /// <summary>自动选择延迟最低的代理节点</summary>
    [RelayCommand]
    private async Task SelectBestAsync()
    {
        if (SelectedGroup == null) return;
        var best = SelectedGroup.Proxies
            .Where(p => p.Delay > 0 && p.Type is not ("Direct" or "Reject"))
            .OrderBy(p => p.Delay)
            .FirstOrDefault();

        if (best == null) return;
        await SelectProxyAsync(best);
        OnPropertyChanged(nameof(FilteredProxies));
    }

    /// <summary>重新加载代理数据</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    public string SortModeLabel => CurrentSort switch
    {
        SortMode.Name  => LocalizationHelper.GetString("ProxySortName.Text"),
        SortMode.Delay => LocalizationHelper.GetString("ProxySortDelay.Text"),
        SortMode.Type  => LocalizationHelper.GetString("ProxySortType.Text"),
        _              => LocalizationHelper.GetString("ProxySortDefault.Text"),
    };

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await LoadAsync();
    }
}
