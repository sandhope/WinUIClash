using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 规则页 ViewModel — 展示当前活跃的路由规则，支持按类型和目标代理筛选
/// </summary>
public partial class RulesViewModel : ObservableObject
{
    private readonly IClashService _clash;
    private bool _initialized;

    public RulesViewModel(IClashService clash)
    {
        _clash = clash;
    }

    [ObservableProperty] private ObservableCollection<Rule> _rules = new();
    [ObservableProperty] private ObservableCollection<Rule> _filteredRules = new();
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _totalCount;

    /// <summary>规则类型统计项</summary>
    public record RuleTypeOption(string Label, string Value, int Count);

    /// <summary>可选的规则类型列表</summary>
    [ObservableProperty] private ObservableCollection<RuleTypeOption> _ruleTypes = new();

    /// <summary>当前选中的类型筛选（"ALL" = 全部）</summary>
    [ObservableProperty] private string _selectedTypeFilter = "ALL";

    /// <summary>可选的目标代理列表</summary>
    [ObservableProperty] private ObservableCollection<string> _proxyOptions = new();

    /// <summary>当前选中的目标代理筛选（"" = 全部）</summary>
    [ObservableProperty] private string _selectedProxyFilter = "";

    /// <summary>筛选后的规则数</summary>
    [ObservableProperty] private int _filteredCount;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedProxyFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        var list = await _clash.GetRulesAsync();
        Rules = new ObservableCollection<Rule>(list);
        TotalCount = list.Count;

        // 构建类型统计
        var typeGroups = list.GroupBy(r => r.Type)
            .OrderByDescending(g => g.Count())
            .ToList();

        var types = new ObservableCollection<RuleTypeOption>
        {
            new("全部类型", "ALL", list.Count)
        };
        foreach (var g in typeGroups)
        {
            types.Add(new RuleTypeOption($"{g.Key} ({g.Count()})", g.Key, g.Count()));
        }
        RuleTypes = types;

        // 构建代理选项
        var proxies = list.Select(r => r.Proxy)
            .Distinct()
            .OrderBy(p => p)
            .ToList();
        var proxyOpts = new ObservableCollection<string> { "" };
        foreach (var p in proxies)
        {
            proxyOpts.Add(p);
        }
        ProxyOptions = proxyOpts;

        ApplyFilter();
        IsLoading = false;
    }

    private void ApplyFilter()
    {
        IEnumerable<Rule> query = Rules;

        // 按类型筛选
        if (!string.IsNullOrEmpty(SelectedTypeFilter) && SelectedTypeFilter != "ALL")
        {
            query = query.Where(r => r.Type == SelectedTypeFilter);
        }

        // 按目标代理筛选
        if (!string.IsNullOrEmpty(SelectedProxyFilter))
        {
            query = query.Where(r => r.Proxy == SelectedProxyFilter);
        }

        // 按关键词搜索
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var keyword = SearchText.Trim();
            query = query.Where(r =>
                r.Type.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                r.Payload.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                r.Proxy.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var result = query.ToList();
        FilteredRules = new ObservableCollection<Rule>(result);
        FilteredCount = result.Count;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await LoadAsync();
    }
}
