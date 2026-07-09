using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUIClash.Models;
using WinUIClash.Services;

namespace WinUIClash.ViewModels;

/// <summary>
/// 请求页 ViewModel — 历史请求追踪 + 搜索过滤
/// </summary>
public partial class RequestsViewModel : ObservableObject
{
    private readonly IClashService _clash;
    private bool _initialized;

    public RequestsViewModel(IClashService clash)
    {
        _clash = clash;
    }

    [ObservableProperty] private ObservableCollection<ConnectionInfo> _requests = new();
    [ObservableProperty] private ObservableCollection<ConnectionInfo> _filteredRequests = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _autoScroll = true;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredRequests = new ObservableCollection<ConnectionInfo>(Requests);
        }
        else
        {
            var filtered = Requests.Where(r =>
                r.Metadata.Host.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                r.Metadata.Process.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            FilteredRequests = new ObservableCollection<ConnectionInfo>(filtered);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var list = await _clash.GetConnectionsAsync();
        Requests = new ObservableCollection<ConnectionInfo>(list);
        ApplyFilter();
    }

    [RelayCommand]
    private void Clear()
    {
        Requests.Clear();
        FilteredRequests.Clear();
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await RefreshAsync();
    }
}
