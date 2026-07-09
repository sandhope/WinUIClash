using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 规则页 ViewModel — 展示当前活跃的路由规则
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

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        var list = await _clash.GetRulesAsync();
        Rules = new ObservableCollection<Rule>(list);
        TotalCount = list.Count;
        ApplyFilter();
        IsLoading = false;
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredRules = new ObservableCollection<Rule>(Rules);
        }
        else
        {
            var keyword = SearchText.Trim();
            FilteredRules = new ObservableCollection<Rule>(
                Rules.Where(r =>
                    r.Type.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    r.Payload.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    r.Proxy.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await LoadAsync();
    }
}
