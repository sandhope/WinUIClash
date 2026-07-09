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
        var list = await _clash.GetProxyGroupsAsync();
        Groups = new ObservableCollection<ProxyGroup>(list);
        SelectedGroup = Groups.FirstOrDefault();
        IsLoading = false;
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
            proxy.Delay = await _clash.TestDelayAsync(proxy.Name);
            OnPropertyChanged(nameof(FilteredProxies));
        }
        catch (Exception ex)
        {
            proxy.Delay = -1;
            _notification.Error(
                LocalizationHelper.GetString("ErrorUpdateTitle.Text"),
                $"{proxy.Name}: {ex.Message}");
        }
    }

    /// <summary>对当前组所有代理并行测速（节流：最多5个并发）</summary>
    [RelayCommand]
    private async Task TestAllDelaysAsync()
    {
        if (SelectedGroup == null) return;
        IsTesting = true;

        var testable = SelectedGroup.Proxies
            .Where(p => p.Type is not ("Direct" or "Reject"))
            .ToList();
        TestTotal = testable.Count;
        TestProgress = 0;

        var tasks = testable.Select(async p =>
        {
            await _testSemaphore.WaitAsync();
            try
            {
                p.Delay = await _clash.TestDelayAsync(p.Name);
            }
            catch
            {
                p.Delay = -1;
            }
            finally
            {
                _testSemaphore.Release();
                TestProgress++;
            }
        });

        await Task.WhenAll(tasks);
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
